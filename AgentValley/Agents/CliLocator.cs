using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace AgentValley;

/// <summary>
/// Finds the claude / codex CLIs. On macOS, a game launched from Steam doesn't inherit the terminal's PATH,
/// so we ask the user's login shell for their real PATH (nvm, Homebrew...).
/// </summary>
public static class CliLocator
{
    private static readonly object Sync = new();
    private static string? shellPath;
    private static readonly Dictionary<string, string?> Cache = new();

    public static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    /// <summary>PATH given to child processes.</summary>
    public static string EffectivePath
    {
        get
        {
            lock (Sync)
            {
                shellPath ??= ComputePath();
                return shellPath;
            }
        }
    }

    public static string? Find(string name, string configuredPath)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            var p = ExpandHome(configuredPath.Trim());
            return File.Exists(p) ? p : null;
        }

        lock (Sync)
        {
            if (Cache.TryGetValue(name, out var cached) && cached != null && File.Exists(cached))
                return cached;
        }

        var found = Search(name);
        lock (Sync)
            Cache[name] = found;
        return found;
    }

    public static void ClearCache()
    {
        lock (Sync)
            Cache.Clear();
    }

    public static string ExpandHome(string path)
    {
        if (path == "~" || path.StartsWith("~/") || path.StartsWith("~\\"))
            return Path.Combine(Home, path.Length > 2 ? path[2..] : "");
        return path;
    }

    private static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    private static string? Search(string name)
    {
        var extensions = IsWindows ? new[] { ".exe", ".cmd", ".bat" } : new[] { "" };
        foreach (var dir in EffectivePath.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries).Concat(KnownDirs()))
        {
            foreach (var ext in extensions)
            {
                string candidate;
                try { candidate = Path.Combine(dir.Trim('"'), name + ext); }
                catch { continue; }
                if (File.Exists(candidate))
                    return PreferNative(name, candidate);
            }
        }

        if (!IsWindows)
        {
            var viaShell = RunShell($"command -v {name}");
            if (viaShell != null && File.Exists(viaShell))
                return viaShell;
        }
        return null;
    }

    /// <summary>The npm shim claude.cmd starts a native claude.exe: call it directly to avoid cmd.exe.</summary>
    private static string PreferNative(string name, string candidate)
    {
        if (!IsWindows || !candidate.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) || name != "claude")
            return candidate;
        var native = Path.Combine(Path.GetDirectoryName(candidate)!, "node_modules", "@anthropic-ai", "claude-code", "bin", "claude.exe");
        return File.Exists(native) ? native : candidate;
    }

    private static IEnumerable<string> KnownDirs()
    {
        var home = Home;
        if (IsWindows)
        {
            yield return Path.Combine(home, ".local", "bin");
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            yield return Path.Combine(appData, "npm");
            yield break;
        }

        yield return Path.Combine(home, ".local", "bin");
        yield return Path.Combine(home, ".claude", "local");
        yield return "/opt/homebrew/bin";
        yield return "/usr/local/bin";
        yield return Path.Combine(home, ".npm-global", "bin");
        yield return Path.Combine(home, ".bun", "bin");
        yield return Path.Combine(home, ".volta", "bin");
        var nvm = Path.Combine(home, ".nvm", "versions", "node");
        if (Directory.Exists(nvm))
            foreach (var v in Directory.GetDirectories(nvm).OrderByDescending(d => d))
                yield return Path.Combine(v, "bin");
    }

    private static string ComputePath()
    {
        var current = Environment.GetEnvironmentVariable("PATH") ?? "";
        if (IsWindows)
            return current;

        var fromShell = RunShell("printf '%s' \"$PATH\"");
        var parts = new List<string>();
        foreach (var p in (fromShell ?? "").Split(':').Concat(current.Split(':')).Concat(KnownDirs()))
            if (!string.IsNullOrWhiteSpace(p) && !parts.Contains(p))
                parts.Add(p);
        return string.Join(':', parts);
    }

    /// <summary>Runs a command in the interactive login shell and returns the last line of output.</summary>
    private static string? RunShell(string command)
    {
        try
        {
            var shell = Environment.GetEnvironmentVariable("SHELL");
            if (string.IsNullOrEmpty(shell) || !File.Exists(shell))
                shell = File.Exists("/bin/zsh") ? "/bin/zsh" : "/bin/bash";

            var psi = new ProcessStartInfo(shell)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            psi.ArgumentList.Add("-ilc");
            psi.ArgumentList.Add(command);

            using var proc = Process.Start(psi);
            if (proc == null)
                return null;
            proc.StandardInput.Close();
            var output = proc.StandardOutput.ReadToEndAsync();
            _ = proc.StandardError.ReadToEndAsync();
            if (!proc.WaitForExit(6000))
            {
                try { proc.Kill(true); } catch { }
                return null;
            }
            var lines = output.Result.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            return lines.Length == 0 ? null : lines[^1].Trim();
        }
        catch
        {
            return null;
        }
    }
}
