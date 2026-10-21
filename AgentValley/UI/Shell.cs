using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using StardewValley;

namespace AgentValley.UI;

/// <summary>OS integrations: file explorer, terminal, folder picker, notifications.</summary>
public static class Shell
{
    private static bool IsMac => RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
    private static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    public static void OpenFolder(string path)
    {
        if (!Directory.Exists(path))
        {
            Game1.addHUDMessage(new HUDMessage(I18n.Get("error.missing-folder", new { path }), HUDMessage.error_type));
            return;
        }
        Try(() =>
        {
            if (IsWindows)
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
            else
                Start(IsMac ? "open" : "xdg-open", path);
        });
    }

    /// <summary>Opens a real terminal on the agent's session (to take over outside the game).</summary>
    public static void OpenTerminal(AgentRecord agent)
    {
        string command;
        if (agent.Kind == AgentKind.Claude)
            command = agent.SessionStarted ? $"claude --resume {agent.SessionId}" : "claude";
        else
            command = string.IsNullOrEmpty(agent.SessionId) ? "codex" : $"codex resume {agent.SessionId}";
        OpenTerminal(agent.WorkingDirectory, command);
    }

    /// <summary>Opens a terminal in <paramref name="dir"/> and runs <paramref name="command"/> in it.</summary>
    public static void OpenTerminal(string dir, string command)
    {
        if (!Directory.Exists(dir))
            return;

        Try(() =>
        {
            if (IsWindows)
            {
                Process.Start(new ProcessStartInfo("cmd.exe", $"/k {command}") { UseShellExecute = true, WorkingDirectory = dir });
            }
            else if (IsMac)
            {
                var script = $"cd {ShellQuote(dir)} && {command}";
                Start("osascript", "-e", "on run argv", "-e", "tell application \"Terminal\" to do script (item 1 of argv)",
                    "-e", "tell application \"Terminal\" to activate", "-e", "end run", script);
            }
            else
            {
                Start("x-terminal-emulator", "-e", $"bash -lc {ShellQuote($"cd {ShellQuote(dir)} && {command}; exec bash")}");
            }
        });
    }

    /// <summary>Native folder picker, without blocking the game.</summary>
    public static Task<string?> PickFolderAsync() => Task.Run(() =>
    {
        try
        {
            if (IsWindows)
            {
                const string ps =
                    "[Console]::OutputEncoding=[Text.Encoding]::UTF8;" +
                    "Add-Type -AssemblyName System.Windows.Forms;" +
                    "$o=New-Object System.Windows.Forms.Form -Property @{TopMost=$true};" +
                    "$f=New-Object System.Windows.Forms.FolderBrowserDialog;" +
                    "$f.Description='Agent working folder';$f.ShowNewFolderButton=$true;" +
                    "if($f.ShowDialog($o) -eq 'OK'){Write-Output $f.SelectedPath}";
                return Capture("powershell.exe", "-NoProfile", "-STA", "-WindowStyle", "Hidden", "-EncodedCommand", Encode(ps));
            }
            if (IsMac)
                return Capture("osascript", "-e", "POSIX path of (choose folder with prompt \"Agent working folder\")")?.TrimEnd('/');
            return Capture("zenity", "--file-selection", "--directory");
        }
        catch
        {
            return null;
        }
    });

    public static void Notify(string title, string message)
    {
        Try(() =>
        {
            if (IsWindows)
            {
                var ps =
                    "Add-Type -AssemblyName System.Windows.Forms;Add-Type -AssemblyName System.Drawing;" +
                    "$n=New-Object System.Windows.Forms.NotifyIcon;$n.Icon=[System.Drawing.SystemIcons]::Information;$n.Visible=$true;" +
                    $"$n.ShowBalloonTip(8000,{PsQuote(title)},{PsQuote(message)},'Info');Start-Sleep -Seconds 9;$n.Dispose()";
                Start("powershell.exe", "-NoProfile", "-WindowStyle", "Hidden", "-EncodedCommand", Encode(ps));
            }
            else if (IsMac)
            {
                Start("osascript", "-e", "on run argv", "-e", "display notification (item 2 of argv) with title (item 1 of argv) sound name \"Glass\"",
                    "-e", "end run", title, message);
            }
            else
            {
                Start("notify-send", title, message);
            }
        });
    }

    private static void Start(string exe, params string[] args)
    {
        var psi = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true };
        foreach (var a in args)
            psi.ArgumentList.Add(a);
        Process.Start(psi)?.Dispose();
    }

    private static string? Capture(string exe, params string[] args)
    {
        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8
        };
        foreach (var a in args)
            psi.ArgumentList.Add(a);
        using var proc = Process.Start(psi);
        if (proc == null)
            return null;
        _ = proc.StandardError.ReadToEndAsync();
        var output = proc.StandardOutput.ReadToEnd().Trim();
        proc.WaitForExit();
        return output.Length == 0 ? null : output;
    }

    private static string Encode(string script) => Convert.ToBase64String(Encoding.Unicode.GetBytes(script));

    private static string PsQuote(string s) => "'" + s.Replace("'", "''") + "'";

    private static string ShellQuote(string s) => "'" + s.Replace("'", "'\\''") + "'";

    private static void Try(Action action)
    {
        try { action(); }
        catch (Exception ex) { ModEntry.Instance.Monitor.Log("OS action failed: " + ex.Message, StardewModdingAPI.LogLevel.Warn); }
    }
}
