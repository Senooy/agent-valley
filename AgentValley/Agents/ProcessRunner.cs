using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace AgentValley;

/// <summary>A running CLI process. Reads stdout line by line on its own thread.</summary>
public sealed class RunningProcess
{
    private readonly Process process;
    private readonly StringBuilder stderr = new();

    public volatile bool Cancelled;

    private RunningProcess(Process process) => this.process = process;

    public string StderrTail
    {
        get
        {
            lock (stderr)
            {
                var s = stderr.ToString().Trim();
                return s.Length > 1500 ? s[^1500..] : s;
            }
        }
    }

    public void Cancel()
    {
        Cancelled = true;
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch { }
    }

    public static RunningProcess Start(string exe, IReadOnlyList<string> args, string cwd, string stdin, Action<string> onLine, Action<RunningProcess, int> onExit, Action<string>? onErrorLine = null)
    {
        var psi = new ProcessStartInfo
        {
            WorkingDirectory = cwd,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            StandardInputEncoding = new UTF8Encoding(false)
        };

        var isScript = CliLocator.IsWindows && (exe.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) || exe.EndsWith(".bat", StringComparison.OrdinalIgnoreCase));
        if (isScript)
        {
            // npm shims (.cmd) go through cmd.exe. The user's text goes to stdin,
            // so the command line only holds options and paths.
            psi.FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";
            psi.Arguments = "/d /s /c \"" + string.Join(" ", new[] { exe }.Concat(args).Select(QuoteForCmd)) + "\"";
        }
        else
        {
            psi.FileName = exe;
            foreach (var a in args)
                psi.ArgumentList.Add(a);
        }

        psi.Environment["PATH"] = CliLocator.EffectivePath;
        psi.Environment["NO_COLOR"] = "1";

        var proc = Process.Start(psi) ?? throw new InvalidOperationException("Couldn't start " + exe);
        var running = new RunningProcess(proc);

        proc.ErrorDataReceived += (_, e) =>
        {
            if (e.Data == null)
                return;
            onErrorLine?.Invoke(e.Data);
            lock (running.stderr)
            {
                running.stderr.AppendLine(e.Data);
                if (running.stderr.Length > 8000)
                    running.stderr.Remove(0, running.stderr.Length - 8000);
            }
        };
        proc.BeginErrorReadLine();

        var thread = new Thread(() =>
        {
            try
            {
                proc.StandardInput.Write(stdin);
                proc.StandardInput.Close();
            }
            catch (IOException) { }

            string? line;
            try
            {
                while ((line = proc.StandardOutput.ReadLine()) != null)
                    if (line.Length > 0)
                        onLine(line);
            }
            catch (Exception) { }

            proc.WaitForExit();
            var code = proc.ExitCode;
            proc.Dispose();
            onExit(running, code);
        })
        {
            IsBackground = true,
            Name = "AgentValley " + Path.GetFileNameWithoutExtension(exe)
        };
        thread.Start();
        return running;
    }

    private static string QuoteForCmd(string arg)
    {
        if (arg.Length > 0 && arg.IndexOfAny(new[] { ' ', '\t', '&', '|', '<', '>', '^', '(', ')' }) < 0)
            return arg;
        return "\"" + arg.Replace("\"", "") + "\"";
    }
}
