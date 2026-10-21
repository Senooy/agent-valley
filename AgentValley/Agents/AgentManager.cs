using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using StardewModdingAPI;

namespace AgentValley;

/// <summary>
/// Starts and tracks agent turns. Processes live outside any map: changing location, opening a menu
/// or sleeping never interrupts them. Every <see cref="AgentRecord"/> mutation happens on the game
/// thread through <see cref="Update"/>.
/// </summary>
public sealed class AgentManager
{
    private readonly IMonitor monitor;
    private readonly Func<ModConfig> config;
    private readonly string promptDir;
    private readonly ConcurrentQueue<Action> mainThread = new();
    private readonly Dictionary<string, RunningProcess> running = new();
    private readonly Dictionary<string, ShellRun> shells = new();

    /// <summary>Game thread: a turn ended (success, error or stop).</summary>
    public event Action<AgentStore, AgentRecord, bool>? TurnFinished;

    /// <summary>Background thread: lets us notify even while the game is paused out of focus.</summary>
    public event Action<string, string, bool>? TurnFinishedBackground;

    public AgentManager(IMonitor monitor, Func<ModConfig> config, string modDir)
    {
        this.monitor = monitor;
        this.config = config;
        promptDir = Path.Combine(modDir, "data", "prompts");
    }

    public bool IsRunning(AgentRecord agent) => running.ContainsKey(agent.Id);

    public bool IsShellRunning(AgentRecord agent) => shells.ContainsKey(agent.Id);

    public void Update()
    {
        while (mainThread.TryDequeue(out var action))
        {
            try { action(); }
            catch (Exception ex) { monitor.Log("Internal error: " + ex, LogLevel.Error); }
        }

        // Output of "!" commands is shown as it arrives.
        foreach (var shell in shells.Values)
            if (shell.Output.TakeChanged() is { } text)
            {
                shell.Entry.Text = text;
                shell.Agent.Touch();
            }
    }

    public void Send(AgentStore store, AgentRecord agent, string text)
    {
        text = text.Trim();
        if (text.Length == 0)
            return;

        agent.Add(EntryRole.User, text);
        agent.Unread = false;
        store.MarkDirty();

        if (agent.IsBusy)
        {
            agent.PendingMessages.Add(text);
            agent.Add(EntryRole.System, I18n.Get("chat.queued"));
            return;
        }
        StartTurn(store, agent, text, retried: false);
    }

    public void Stop(AgentStore store, AgentRecord agent)
    {
        agent.PendingMessages.Clear();
        if (running.TryGetValue(agent.Id, out var proc))
            proc.Cancel();
        if (shells.TryGetValue(agent.Id, out var shell))
            shell.Process.Cancel();
        store.MarkDirty();
    }

    /// <summary>"!command": runs a shell command in the agent's folder, with live output in the chat.</summary>
    public void RunShell(AgentStore store, AgentRecord agent, string command)
    {
        command = command.Trim();
        if (command.Length == 0)
            return;
        if (shells.ContainsKey(agent.Id))
        {
            agent.Add(EntryRole.System, I18n.Get("shell.busy"));
            return;
        }
        if (!Directory.Exists(agent.WorkingDirectory))
        {
            agent.Add(EntryRole.Error, I18n.Get("error.missing-folder", new { path = agent.WorkingDirectory }));
            return;
        }

        agent.Add(EntryRole.Shell, "$ " + command);
        var entry = new ChatEntry(EntryRole.Output, "");
        agent.Add(entry);
        store.MarkDirty();

        var output = new ShellOutput();
        var (exe, args) = ShellInvocation(command);
        try
        {
            var proc = RunningProcess.Start(exe, args, agent.WorkingDirectory, "", output.Append,
                (self, code) => Post(() => FinishShell(store, agent, command, code, self.Cancelled)),
                output.Append);
            shells[agent.Id] = new ShellRun(proc, output, entry, agent);
        }
        catch (Exception ex)
        {
            entry.Text = I18n.Get("shell.start-failed", new { error = ex.Message });
            entry.Role = EntryRole.Error;
            agent.Touch();
        }
    }

    private void FinishShell(AgentStore store, AgentRecord agent, string command, int code, bool cancelled)
    {
        if (!shells.Remove(agent.Id, out var shell))
            return;
        var text = shell.Output.Snapshot();
        shell.Entry.Text = (text.Length == 0 ? I18n.Get("shell.no-output") : text)
            + "\n-> " + (cancelled ? I18n.Get("shell.stopped") : I18n.Get("shell.exit-code", new { code }));
        agent.Touch();

        // Like Claude Code's "!" mode: the agent sees the command and its output with your next message.
        var tail = text.Length > 3000 ? "..." + text[^3000..] : text;
        agent.ShellContext.Add($"$ {command}\n{tail}\n(exit code {(cancelled ? "stopped" : code.ToString())})");
        if (agent.ShellContext.Count > 5)
            agent.ShellContext.RemoveAt(0);
        store.MarkDirty();
    }

    private (string Exe, List<string> Args) ShellInvocation(string command)
    {
        if (!CliLocator.IsWindows)
        {
            var sh = Environment.GetEnvironmentVariable("SHELL");
            if (string.IsNullOrEmpty(sh) || !File.Exists(sh))
                sh = File.Exists("/bin/zsh") ? "/bin/zsh" : "/bin/bash";
            return (sh, new List<string> { "-lc", command });
        }

        var shell = config().WindowsShell.Trim().ToLowerInvariant();
        if (shell == "cmd")
            return ("cmd.exe", new List<string> { "/d", "/c", command });
        // Errors merged into the output (otherwise PowerShell serializes them as CLIXML), objects rendered
        // as text, and the exit code of the last native command preserved.
        var script = "[Console]::OutputEncoding=[Text.Encoding]::UTF8;$ProgressPreference='SilentlyContinue';$LASTEXITCODE=0;$__failed=$false\n"
            + "& {\n" + command + "\n} 2>&1 | ForEach-Object { if ($_ -is [System.Management.Automation.ErrorRecord]) { $__failed=$true; 'ERROR: ' + $_.ToString() } else { $_ } } | Out-String -Stream -Width 160\n"
            + "if ($LASTEXITCODE) { exit $LASTEXITCODE } elseif ($__failed) { exit 1 } else { exit 0 }";
        var encoded = Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(script));
        return (shell == "pwsh" ? "pwsh.exe" : "powershell.exe",
            new List<string> { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-EncodedCommand", encoded });
    }

    public void Forget(AgentStore store, AgentRecord agent)
    {
        Stop(store, agent);
        running.Remove(agent.Id);
        store.Agents.Remove(agent);
        try { File.Delete(PromptFile(agent)); } catch { }
        store.MarkDirty();
    }

    public void KillAll()
    {
        foreach (var proc in running.Values.ToList())
            proc.Cancel();
        foreach (var shell in shells.Values.ToList())
            shell.Process.Cancel();
    }

    private string PromptFile(AgentRecord agent) => Path.Combine(promptDir, agent.Id + ".txt");

    private void StartTurn(AgentStore store, AgentRecord agent, string message, bool retried)
    {
        var cfg = config();
        var backend = Backends.For(agent.Kind);

        if (agent.Kind == AgentKind.Claude && string.IsNullOrEmpty(agent.SessionId))
        {
            agent.SessionId = Guid.NewGuid().ToString();
            agent.SessionStarted = false;
        }

        if (agent.ShellContext.Count > 0)
        {
            message = "[Shell commands the user ran from the chat, for context]\n"
                + string.Join("\n\n", agent.ShellContext) + "\n\n[Message]\n" + message;
            agent.ShellContext.Clear();
        }

        var instructions = cfg.AgentInstructions.Replace("{name}", agent.Name);
        var promptFile = PromptFile(agent);
        try
        {
            Directory.CreateDirectory(promptDir);
            File.WriteAllText(promptFile, instructions);
        }
        catch (Exception ex)
        {
            monitor.Log("Couldn't write the system prompt: " + ex.Message, LogLevel.Warn);
        }

        var model = !string.IsNullOrWhiteSpace(agent.Model) ? agent.Model : agent.Kind == AgentKind.Claude ? cfg.ClaudeModel : cfg.CodexModel;
        var req = new TurnRequest(agent.Id, agent.Name, agent.WorkingDirectory, agent.SessionId, agent.SessionStarted,
            agent.TurnCount == 0, agent.Permission, model, message, instructions, promptFile);
        var configuredPath = agent.Kind == AgentKind.Claude ? cfg.ClaudePath : cfg.CodexPath;

        agent.Status = AgentStatus.Working;
        agent.Unread = false;
        store.MarkDirty();

        Task.Run(() => RunTurn(store, backend, req, configuredPath, retried));
    }

    private void RunTurn(AgentStore store, IAgentBackend backend, TurnRequest req, string configuredPath, bool retried)
    {
        var sink = new TurnSink(this, store, req.AgentId);

        if (!Directory.Exists(req.WorkingDirectory))
        {
            Finish(store, req, sink, backend, null, retried, I18n.Get("error.missing-folder", new { path = req.WorkingDirectory }));
            return;
        }

        var exe = CliLocator.Find(backend.CliName, configuredPath);
        if (exe == null)
        {
            Finish(store, req, sink, backend, null, retried, I18n.Get("error.cli-not-found", new { cli = backend.DisplayName, hint = backend.InstallHint }));
            return;
        }

        var args = backend.BuildArgs(req);
        monitor.Log($"[{req.Name}] {exe} {string.Join(" ", args)}", LogLevel.Trace);

        RunningProcess? proc = null;
        try
        {
            proc = RunningProcess.Start(exe, args, req.WorkingDirectory, backend.BuildStdin(req),
                line =>
                {
                    try { backend.HandleLine(req, line, sink); }
                    catch (JsonException) { monitor.Log($"[{req.Name}] ignored line: {Backends.OneLine(line, 200)}", LogLevel.Trace); }
                    catch (Exception ex) { monitor.Log($"[{req.Name}] read error: {ex.Message}", LogLevel.Warn); }
                },
                (self, exitCode) => Finish(store, req, sink, backend, self, retried, exitCode == 0 || sink.Done ? null : I18n.Get("error.cli-exit", new { code = exitCode })));
        }
        catch (Exception ex)
        {
            Finish(store, req, sink, backend, null, retried, I18n.Get("error.cli-start", new { cli = backend.DisplayName, error = ex.Message }));
            return;
        }

        Post(() =>
        {
            if (sink.Finished)
                return;
            if (store.Get(req.AgentId) != null)
                running[req.AgentId] = proc;
            else
                proc.Cancel();
        });
    }

    private void Finish(AgentStore store, TurnRequest req, TurnSink sink, IAgentBackend backend, RunningProcess? proc, bool retried, string? exitError)
    {
        sink.Finished = true;
        var cancelled = proc?.Cancelled == true;
        var error = sink.Done ? null : sink.Error ?? exitError ?? I18n.Get("error.no-response");
        var stderr = proc?.StderrTail ?? "";
        if (error != null && !cancelled && !string.IsNullOrWhiteSpace(stderr))
            monitor.Log($"[{req.Name}] stderr: {stderr}", LogLevel.Debug);

        if (!cancelled)
            TurnFinishedBackground?.Invoke(req.Name, error ?? sink.LastAgentText ?? I18n.Get("status.finished"), error != null);

        Post(() =>
        {
            running.Remove(req.AgentId);
            var agent = store.Get(req.AgentId);
            if (agent == null)
                return;

            // Claude: the session didn't exist yet (or already existed), so flip the flag and retry once.
            if (agent.Kind == AgentKind.Claude && !cancelled && error != null && !retried)
            {
                var all = error + "\n" + stderr;
                if (agent.SessionStarted && all.Contains("No conversation found", StringComparison.OrdinalIgnoreCase))
                {
                    agent.SessionStarted = false;
                    StartTurn(store, agent, req.Message, retried: true);
                    return;
                }
                if (!agent.SessionStarted && all.Contains("already in use", StringComparison.OrdinalIgnoreCase))
                {
                    agent.SessionStarted = true;
                    StartTurn(store, agent, req.Message, retried: true);
                    return;
                }
            }

            if (agent.Kind == AgentKind.Claude && sink.SawResult)
                agent.SessionStarted = true;

            agent.TurnCount++;
            if (cancelled)
            {
                agent.Status = AgentStatus.Interrupted;
                agent.Add(EntryRole.System, I18n.Get("chat.stopped"));
                agent.LastPreview = I18n.Get("chat.stopped");
            }
            else if (error != null)
            {
                var hint = backend.ExplainError(error + "\n" + stderr);
                var detail = string.IsNullOrWhiteSpace(stderr) || sink.Error != null ? "" : "\n" + Backends.OneLine(stderr, 400);
                agent.Status = AgentStatus.Error;
                agent.Add(EntryRole.Error, error + detail + (hint != null ? "\n" + hint : ""));
                agent.LastPreview = hint ?? error;
            }
            else
            {
                if (sink.LastAgentText == null && !string.IsNullOrWhiteSpace(sink.ResultText))
                    agent.Add(EntryRole.Agent, sink.ResultText.Trim());
                agent.Status = AgentStatus.Done;
                agent.LastPreview = sink.LastAgentText ?? sink.ResultText ?? I18n.Get("status.finished");
            }
            store.MarkDirty();

            if (!cancelled && agent.PendingMessages.Count > 0)
            {
                var next = string.Join("\n\n", agent.PendingMessages);
                agent.PendingMessages.Clear();
                StartTurn(store, agent, next, retried: false);
                return;
            }

            TurnFinished?.Invoke(store, agent, error != null && !cancelled);
        });
    }

    private void Post(Action action) => mainThread.Enqueue(action);

    private sealed class TurnSink : ITurnSink
    {
        private readonly AgentManager manager;
        private readonly AgentStore store;
        private readonly string agentId;

        public volatile bool Done;
        public volatile bool SawResult;
        public volatile bool Finished;
        public string? Error;
        public string? LastAgentText;
        public string? ResultText;

        public TurnSink(AgentManager manager, AgentStore store, string agentId)
        {
            this.manager = manager;
            this.store = store;
            this.agentId = agentId;
        }

        public void Entry(EntryRole role, string text)
        {
            if (role == EntryRole.Agent)
                LastAgentText = text;
            manager.Post(() =>
            {
                var agent = store.Get(agentId);
                if (agent == null)
                    return;
                agent.Add(role, text);
                store.MarkDirty();
            });
        }

        public void SessionId(string id) => manager.Post(() =>
        {
            var agent = store.Get(agentId);
            if (agent == null)
                return;
            agent.SessionId = id;
            store.MarkDirty();
        });

        public void Commands(IReadOnlyList<string> names) => manager.Post(() =>
        {
            if (names.Count == 0)
                return;
            store.Data.ClaudeCommands = names.ToList();
            store.MarkDirty();
        });

        public void Result(string text) => ResultText = text;

        public void Completed()
        {
            SawResult = true;
            Done = true;
        }

        public void Failed(string message)
        {
            SawResult = true;
            Error ??= message;
        }
    }

    private sealed record ShellRun(RunningProcess Process, ShellOutput Output, ChatEntry Entry, AgentRecord Agent);

    /// <summary>Output buffer shared between the process thread and the game thread (keeps the end when too long).</summary>
    private sealed class ShellOutput
    {
        private const int MaxChars = 20000;
        private readonly System.Text.StringBuilder text = new();
        private bool changed;
        private bool truncated;

        public void Append(string line)
        {
            lock (text)
            {
                text.Append(line.TrimEnd()).Append('\n');
                if (text.Length > MaxChars)
                {
                    text.Remove(0, text.Length - MaxChars);
                    truncated = true;
                }
                changed = true;
            }
        }

        public string Snapshot()
        {
            lock (text)
                return (truncated ? I18n.Get("shell.truncated") + "\n" : "") + text.ToString().TrimEnd('\n');
        }

        public string? TakeChanged()
        {
            lock (text)
            {
                if (!changed)
                    return null;
                changed = false;
            }
            return Snapshot();
        }
    }
}
