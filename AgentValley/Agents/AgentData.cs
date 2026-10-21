using System;
using System.Collections.Generic;

namespace AgentValley;

public enum AgentKind { Claude, Codex }

public enum AgentStatus { Idle, Working, Done, Error, Interrupted }

public enum PermissionLevel { Yolo, Edits, ReadOnly }

public enum EntryRole { User, Agent, Tool, System, Error, Shell, Output }

public sealed class ChatEntry
{
    public EntryRole Role { get; set; }
    public string Text { get; set; } = "";
    public DateTime Time { get; set; } = DateTime.Now;

    public ChatEntry() { }

    public ChatEntry(EntryRole role, string text)
    {
        Role = role;
        Text = text;
    }
}

/// <summary>An agent placed in the world. Serialized as-is in the mod's data/ folder (never in the game save).</summary>
public sealed class AgentRecord
{
    public const int MaxTranscript = 400;

    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Agent";
    public AgentKind Kind { get; set; }
    public PermissionLevel Permission { get; set; } = PermissionLevel.Yolo;
    public string Model { get; set; } = "";
    public string WorkingDirectory { get; set; } = "";

    public string LocationName { get; set; } = "";
    public int TileX { get; set; }
    public int TileY { get; set; }
    public int ColorIndex { get; set; }

    /// <summary>Claude: UUID chosen by the mod. Codex: thread_id returned by the CLI.</summary>
    public string SessionId { get; set; } = "";

    /// <summary>Claude only: the session exists on the CLI side, so we can use --resume.</summary>
    public bool SessionStarted { get; set; }

    public AgentStatus Status { get; set; } = AgentStatus.Idle;
    public bool Unread { get; set; }
    public string LastPreview { get; set; } = "";
    public DateTime? LastActivity { get; set; }
    public int TurnCount { get; set; }

    public List<ChatEntry> Transcript { get; set; } = new();
    public List<string> PendingMessages { get; set; } = new();

    /// <summary>Output of "!" commands the agent hasn't seen yet: attached to the next message.</summary>
    public List<string> ShellContext { get; set; } = new();

    /// <summary>Changes on every transcript update (to refresh the display). Internal, so not serialized.</summary>
    internal int Revision { get; private set; }

    public void Touch() => Revision++;

    public bool IsBusy => Status == AgentStatus.Working;

    public void Add(EntryRole role, string text)
    {
        Add(new ChatEntry(role, text));
    }

    public void Add(ChatEntry entry)
    {
        Transcript.Add(entry);
        if (Transcript.Count > MaxTranscript)
            Transcript.RemoveRange(0, Transcript.Count - MaxTranscript);
        LastActivity = DateTime.Now;
        Touch();
    }

    public void ResetSession()
    {
        SessionId = "";
        SessionStarted = false;
        TurnCount = 0;
    }
}

public sealed class StoreData
{
    public List<AgentRecord> Agents { get; set; } = new();
    public List<string> RecentFolders { get; set; } = new();

    /// <summary>Last list of "/" commands announced by Claude Code (for autocompletion).</summary>
    public List<string> ClaudeCommands { get; set; } = new();

    public bool SidebarExpanded { get; set; } = true;
}
