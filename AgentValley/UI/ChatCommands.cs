using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using StardewValley;

namespace AgentValley.UI;

/// <summary>
/// Chat "/" commands. The mod's own commands are handled locally; everything else is passed as-is
/// to the CLI (Claude Code understands /compact, /context, its skills, your custom commands...).
/// </summary>
public static class ChatCommands
{
    public sealed record Command(string Name, string[] Aliases)
    {
        public string Help => I18n.Get("command." + Name);
    }

    public static readonly Command[] Local =
    {
        new("help", Array.Empty<string>()),
        new("stop", Array.Empty<string>()),
        new("new", new[] { "clear" }),
        new("mode", Array.Empty<string>()),
        new("model", Array.Empty<string>()),
        new("rename", Array.Empty<string>()),
        new("cd", new[] { "folder" }),
        new("terminal", Array.Empty<string>()),
        new("login", Array.Empty<string>()),
        new("move", Array.Empty<string>()),
        new("copy", Array.Empty<string>()),
        new("delete", Array.Empty<string>()),
    };

    private static readonly HashSet<string> Hidden = new(StringComparer.OrdinalIgnoreCase)
    {
        // Claude Code commands that are interactive or replaced by the mod's own.
        "clear", "model", "rename", "config", "color", "resume", "exit", "login", "logout", "doctor", "heapdump", "ide", "terminal-setup", "vim"
    };

    /// <summary>Suggestions for "/prefix": the mod's commands first, then the CLI's.</summary>
    public static List<(string Name, string Help)> Suggest(AgentStore store, AgentRecord agent, string prefix)
    {
        var result = new List<(string, string)>();
        foreach (var c in Local)
            if (c.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || c.Aliases.Any(a => a.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && prefix.Length > 0))
                result.Add((c.Name, c.Help));
        if (agent.Kind == AgentKind.Claude)
            foreach (var name in store.Data.ClaudeCommands)
                if (!Hidden.Contains(name) && name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && result.All(r => r.Item1 != name))
                    result.Add((name, "Claude Code"));
        return result;
    }

    /// <summary>Returns true if the mod handled the command (otherwise it goes to the CLI).</summary>
    public static bool TryHandle(ChatMenu menu, AgentStore store, AgentRecord agent, string text)
    {
        var body = text[1..].Trim();
        var space = body.IndexOf(' ');
        var name = (space < 0 ? body : body[..space]).ToLowerInvariant();
        var arg = space < 0 ? "" : body[(space + 1)..].Trim();
        var cmd = Local.FirstOrDefault(c => c.Name == name || c.Aliases.Contains(name));
        if (cmd == null)
        {
            if (agent.Kind == AgentKind.Codex)
                agent.Add(EntryRole.System, I18n.Get("command.codex-passthrough"));
            return false;
        }

        var manager = ModEntry.Instance.Manager;
        void Info(string message) => agent.Add(EntryRole.System, message);

        switch (cmd.Name)
        {
            case "help":
                Info(I18n.Get("command.help.header") + "\n" + string.Join("\n", Local.Select(c => $"  /{c.Name} - {c.Help}"))
                    + "\n\n  " + I18n.Get("command.help.shell")
                    + (agent.Kind == AgentKind.Claude ? "\n  " + I18n.Get("command.help.passthrough") : ""));
                break;

            case "stop":
                manager.Stop(store, agent);
                break;

            case "new":
                manager.Stop(store, agent);
                agent.ResetSession();
                agent.ShellContext.Clear();
                agent.Status = AgentStatus.Idle;
                Info(I18n.Get("command.new.done"));
                break;

            case "mode":
                agent.Permission = arg.ToLowerInvariant() switch
                {
                    "yolo" => PermissionLevel.Yolo,
                    "edits" or "edit" or "auto" => PermissionLevel.Edits,
                    "readonly" or "read" or "plan" => PermissionLevel.ReadOnly,
                    _ => Widgets.Next(agent.Permission)
                };
                Info(I18n.Get("command.mode.done", new { mode = Widgets.PermissionLabel(agent.Permission) }));
                break;

            case "model":
                agent.Model = arg;
                Info(arg.Length == 0 ? I18n.Get("command.model.default") : I18n.Get("command.model.done", new { model = arg }));
                break;

            case "rename":
                if (arg.Length == 0)
                    Info(I18n.Get("command.rename.usage"));
                else
                {
                    agent.Name = arg.Length > 32 ? arg[..32] : arg;
                    Info(I18n.Get("command.rename.done", new { name = agent.Name }));
                }
                break;

            case "cd":
                if (arg.Length == 0)
                {
                    Shell.OpenFolder(agent.WorkingDirectory);
                    break;
                }
                var dir = CliLocator.ExpandHome(arg.Trim('"'));
                if (!Path.IsPathRooted(dir))
                    dir = Path.Combine(agent.WorkingDirectory, dir);
                if (!Directory.Exists(dir))
                {
                    agent.Add(EntryRole.Error, I18n.Get("error.missing-folder", new { path = dir }));
                    break;
                }
                manager.Stop(store, agent);
                agent.WorkingDirectory = Path.GetFullPath(dir);
                agent.ResetSession();
                store.RememberFolder(agent.WorkingDirectory);
                Info(I18n.Get("command.cd.done", new { path = agent.WorkingDirectory }));
                break;

            case "terminal":
                if (agent.IsBusy)
                    Info(I18n.Get("command.terminal.busy"));
                else
                    Shell.OpenTerminal(agent);
                break;

            case "login":
                if (agent.Kind == AgentKind.Claude)
                {
                    Shell.OpenTerminal(agent.WorkingDirectory, "claude");
                    Info(I18n.Get("command.login.claude"));
                }
                else
                {
                    Shell.OpenTerminal(agent.WorkingDirectory, "codex login");
                    Info(I18n.Get("command.login.codex"));
                }
                break;

            case "move":
                menu.exitThisMenu();
                ModEntry.Instance.Placement.Begin(store, agent, isNew: false);
                break;

            case "copy":
                var last = agent.Transcript.LastOrDefault(e => e.Role == EntryRole.Agent);
                if (last != null && DesktopClipboard.SetText(last.Text))
                    Info(I18n.Get("command.copy.done"));
                else
                    Info(I18n.Get("command.copy.empty"));
                break;

            case "delete":
                if (!arg.Equals("confirm", StringComparison.OrdinalIgnoreCase))
                {
                    Info(I18n.Get("command.delete.confirm"));
                    break;
                }
                manager.Forget(store, agent);
                Game1.playSound("trashcan");
                menu.exitThisMenu();
                break;
        }
        store.MarkDirty();
        return true;
    }
}
