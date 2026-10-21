using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace AgentValley;

/// <summary>What a backend reports during a turn. Called from a background thread.</summary>
public interface ITurnSink
{
    void Entry(EntryRole role, string text);
    void SessionId(string id);
    void Commands(IReadOnlyList<string> names);
    void Result(string text);
    void Completed();
    void Failed(string message);
}

/// <summary>Snapshot of an agent, taken on the game thread when the turn starts.</summary>
public sealed record TurnRequest(
    string AgentId,
    string Name,
    string WorkingDirectory,
    string SessionId,
    bool SessionStarted,
    bool IsFirstTurn,
    PermissionLevel Permission,
    string Model,
    string Message,
    string Instructions,
    string SystemPromptFile);

public interface IAgentBackend
{
    string CliName { get; }
    string DisplayName { get; }
    string InstallHint { get; }
    List<string> BuildArgs(TurnRequest req);
    string BuildStdin(TurnRequest req);
    void HandleLine(TurnRequest req, string line, ITurnSink sink);
    string? ExplainError(string text);
}

public static class Backends
{
    public static readonly IAgentBackend Claude = new ClaudeBackend();
    public static readonly IAgentBackend Codex = new CodexBackend();

    public static IAgentBackend For(AgentKind kind) => kind == AgentKind.Codex ? Codex : Claude;

    internal static string? Str(JsonElement el, string prop) =>
        el.ValueKind == JsonValueKind.Object && el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    internal static string OneLine(string? text, int max = 140)
    {
        if (string.IsNullOrEmpty(text))
            return "";
        var line = text.Replace("\r", "").Split('\n')[0].Trim();
        return line.Length > max ? line[..max] + "..." : line;
    }

    internal static string ShortPath(string? path, string cwd)
    {
        if (string.IsNullOrEmpty(path))
            return "";
        try
        {
            var rel = Path.GetRelativePath(cwd, path);
            return rel.StartsWith("..") ? path : rel;
        }
        catch
        {
            return path;
        }
    }
}

/// <summary>Headless Claude Code: one process per turn, stream-json in and out, resumed with --resume.</summary>
public sealed class ClaudeBackend : IAgentBackend
{
    public string CliName => "claude";
    public string DisplayName => "Claude Code";
    public string InstallHint => I18n.Get("claude.install-hint");

    public List<string> BuildArgs(TurnRequest req)
    {
        var args = new List<string>
        {
            "-p",
            "--input-format", "stream-json",
            "--output-format", "stream-json",
            "--verbose",
            req.SessionStarted ? "--resume" : "--session-id", req.SessionId,
            "--disallowedTools", "AskUserQuestion",
            "--append-system-prompt-file", req.SystemPromptFile
        };
        switch (req.Permission)
        {
            case PermissionLevel.Yolo:
                args.Add("--dangerously-skip-permissions");
                break;
            case PermissionLevel.Edits:
                args.AddRange(new[] { "--permission-mode", "acceptEdits" });
                break;
            case PermissionLevel.ReadOnly:
                args.AddRange(new[] { "--permission-mode", "plan" });
                break;
        }
        if (!string.IsNullOrWhiteSpace(req.Model))
            args.AddRange(new[] { "--model", req.Model.Trim() });
        return args;
    }

    public string BuildStdin(TurnRequest req)
    {
        var msg = new { type = "user", message = new { role = "user", content = req.Message } };
        return JsonSerializer.Serialize(msg) + "\n";
    }

    public void HandleLine(TurnRequest req, string line, ITurnSink sink)
    {
        using var doc = JsonDocument.Parse(line);
        var root = doc.RootElement;
        switch (Backends.Str(root, "type"))
        {
            case "assistant":
                if (!root.TryGetProperty("message", out var message) || !message.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
                    return;
                // API errors also arrive as a fake "<synthetic>" message; the result event already reports them.
                if (Backends.Str(message, "model") == "<synthetic>")
                    return;
                foreach (var block in content.EnumerateArray())
                {
                    switch (Backends.Str(block, "type"))
                    {
                        case "text":
                            var text = Backends.Str(block, "text");
                            if (!string.IsNullOrWhiteSpace(text))
                                sink.Entry(EntryRole.Agent, text.Trim());
                            break;
                        case "tool_use":
                            var name = Backends.Str(block, "name") ?? "tool";
                            block.TryGetProperty("input", out var input);
                            sink.Entry(EntryRole.Tool, SummarizeTool(name, input, req.WorkingDirectory));
                            break;
                    }
                }
                break;

            case "system":
                switch (Backends.Str(root, "subtype"))
                {
                    case "init" when root.TryGetProperty("slash_commands", out var cmds) && cmds.ValueKind == JsonValueKind.Array:
                        var names = new List<string>();
                        foreach (var c in cmds.EnumerateArray())
                            if (c.ValueKind == JsonValueKind.String && c.GetString() is { Length: > 0 } n && !n.StartsWith("_"))
                                names.Add(n);
                        sink.Commands(names);
                        break;
                    case "api_retry" when root.TryGetProperty("attempt", out var attempt) && attempt.ValueKind == JsonValueKind.Number && attempt.GetInt32() == 1:
                        sink.Entry(EntryRole.System, I18n.Get("claude.api-retry"));
                        break;
                }
                break;

            case "result":
                var isError = root.TryGetProperty("is_error", out var e) && e.ValueKind == JsonValueKind.True;
                var subtype = Backends.Str(root, "subtype") ?? "";
                if (isError || subtype.StartsWith("error"))
                    sink.Failed(Backends.Str(root, "result") ?? subtype);
                else
                {
                    // Local commands (/context, /usage...) only answer in the result.
                    if (Backends.Str(root, "result") is { Length: > 0 } result)
                        sink.Result(result);
                    sink.Completed();
                }
                break;
        }
    }

    public string? ExplainError(string text)
    {
        if (text.Contains("authenticate", StringComparison.OrdinalIgnoreCase) || text.Contains("401") || text.Contains("/login"))
            return I18n.Get("claude.auth-expired");
        if (text.Contains("No conversation found", StringComparison.OrdinalIgnoreCase))
            return I18n.Get("claude.session-not-found");
        return null;
    }

    private static string SummarizeTool(string name, JsonElement input, string cwd)
    {
        var detail = name switch
        {
            "Bash" or "PowerShell" => Backends.Str(input, "command"),
            "Read" or "Write" or "Edit" or "MultiEdit" or "NotebookEdit" => Backends.ShortPath(Backends.Str(input, "file_path") ?? Backends.Str(input, "notebook_path"), cwd),
            "Glob" or "Grep" => Backends.Str(input, "pattern"),
            "WebFetch" => Backends.Str(input, "url"),
            "WebSearch" => Backends.Str(input, "query"),
            "Task" or "Agent" => Backends.Str(input, "description"),
            "TodoWrite" => I18n.Get("tool.todo"),
            _ => null
        };
        detail = Backends.OneLine(detail);
        return string.IsNullOrEmpty(detail) ? name : $"{name}: {detail}";
    }
}

/// <summary>Codex CLI: "codex exec --json", resumed with "codex exec resume &lt;thread_id&gt;", prompt on stdin.</summary>
public sealed class CodexBackend : IAgentBackend
{
    public string CliName => "codex";
    public string DisplayName => "Codex";
    public string InstallHint => I18n.Get("codex.install-hint");

    public List<string> BuildArgs(TurnRequest req)
    {
        var args = new List<string> { "exec", "--json", "--skip-git-repo-check" };
        switch (req.Permission)
        {
            case PermissionLevel.Yolo:
                args.Add("--dangerously-bypass-approvals-and-sandbox");
                break;
            case PermissionLevel.Edits:
                args.AddRange(new[] { "--sandbox", "workspace-write" });
                break;
            case PermissionLevel.ReadOnly:
                args.AddRange(new[] { "--sandbox", "read-only" });
                break;
        }
        if (!string.IsNullOrWhiteSpace(req.Model))
            args.AddRange(new[] { "-m", req.Model.Trim() });
        if (!string.IsNullOrEmpty(req.SessionId))
            args.AddRange(new[] { "resume", req.SessionId });
        args.Add("-");
        return args;
    }

    public string BuildStdin(TurnRequest req)
    {
        if (!string.IsNullOrEmpty(req.SessionId))
            return req.Message;
        return $"[Instructions]\n{req.Instructions}\n\n[User message]\n{req.Message}";
    }

    public void HandleLine(TurnRequest req, string line, ITurnSink sink)
    {
        using var doc = JsonDocument.Parse(line);
        var root = doc.RootElement;
        var type = Backends.Str(root, "type");
        switch (type)
        {
            case "thread.started":
                var id = Backends.Str(root, "thread_id");
                if (!string.IsNullOrEmpty(id))
                    sink.SessionId(id);
                break;

            case "item.started":
            case "item.completed":
                if (root.TryGetProperty("item", out var item))
                    HandleItem(req, type == "item.completed", item, sink);
                break;

            case "turn.completed":
                sink.Completed();
                break;

            case "turn.failed":
                root.TryGetProperty("error", out var err);
                sink.Failed(Backends.Str(err, "message") ?? I18n.Get("codex.turn-failed"));
                break;

            case "error":
                sink.Failed(Backends.Str(root, "message") ?? I18n.Get("codex.error"));
                break;
        }
    }

    private static void HandleItem(TurnRequest req, bool completed, JsonElement item, ITurnSink sink)
    {
        switch (Backends.Str(item, "type"))
        {
            case "agent_message":
                var text = Backends.Str(item, "text");
                if (completed && !string.IsNullOrWhiteSpace(text))
                    sink.Entry(EntryRole.Agent, text.Trim());
                break;

            case "command_execution":
                if (!completed)
                    sink.Entry(EntryRole.Tool, "$ " + Backends.OneLine(Backends.Str(item, "command")));
                else if (item.TryGetProperty("exit_code", out var code) && code.ValueKind == JsonValueKind.Number && code.GetInt32() != 0)
                    sink.Entry(EntryRole.Tool, "  -> " + I18n.Get("shell.exit-code", new { code = code.GetInt32() }));
                break;

            case "file_change":
                if (completed && item.TryGetProperty("changes", out var changes) && changes.ValueKind == JsonValueKind.Array)
                    foreach (var change in changes.EnumerateArray())
                        sink.Entry(EntryRole.Tool, $"{KindLabel(Backends.Str(change, "kind"))}: {Backends.ShortPath(Backends.Str(change, "path"), req.WorkingDirectory)}");
                break;

            case "mcp_tool_call":
                if (!completed)
                    sink.Entry(EntryRole.Tool, $"{Backends.Str(item, "server")}.{Backends.Str(item, "tool")}");
                break;

            case "web_search":
                if (!completed)
                    sink.Entry(EntryRole.Tool, I18n.Get("tool.search", new { query = Backends.OneLine(Backends.Str(item, "query")) }));
                break;

            case "error":
                if (completed)
                    sink.Entry(EntryRole.System, Backends.OneLine(Backends.Str(item, "message"), 300));
                break;
        }
    }

    private static string KindLabel(string? kind) => kind switch
    {
        "add" => I18n.Get("file.created"),
        "delete" => I18n.Get("file.deleted"),
        _ => I18n.Get("file.updated")
    };

    public string? ExplainError(string text)
    {
        if (text.Contains("login", StringComparison.OrdinalIgnoreCase) || text.Contains("401") || text.Contains("auth", StringComparison.OrdinalIgnoreCase))
            return I18n.Get("codex.auth");
        return null;
    }
}
