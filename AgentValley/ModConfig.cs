using StardewModdingAPI.Utilities;

namespace AgentValley;

public sealed class ModConfig
{
    /// <summary>Opens the "phone": the list of all agents, wherever they are.</summary>
    public KeybindList OpenAgentList { get; set; } = KeybindList.Parse("F7");

    /// <summary>Creates a new agent, then places it at the cursor.</summary>
    public KeybindList NewAgent { get; set; } = KeybindList.Parse("F8");

    /// <summary>Answers the last agent that called you.</summary>
    public KeybindList AnswerLastCall { get; set; } = KeybindList.Parse("F6");

    /// <summary>Expands / collapses the agents side panel.</summary>
    public KeybindList ToggleSidebar { get; set; } = KeybindList.Parse("F5");

    /// <summary>Shell for "!" commands on Windows: powershell, pwsh or cmd. (macOS uses your $SHELL.)</summary>
    public string WindowsShell { get; set; } = "powershell";

    /// <summary>Path to the Claude Code CLI. Empty = auto-detect.</summary>
    public string ClaudePath { get; set; } = "";

    /// <summary>Path to the Codex CLI. Empty = auto-detect.</summary>
    public string CodexPath { get; set; } = "";

    /// <summary>Default Claude model (e.g. "opus", "sonnet"). Empty = the one from your Claude Code config.</summary>
    public string ClaudeModel { get; set; } = "";

    /// <summary>Default Codex model. Empty = the one from your Codex config.</summary>
    public string CodexModel { get; set; } = "";

    /// <summary>Permission level of new agents: Yolo, Edits or ReadOnly.</summary>
    public PermissionLevel DefaultPermission { get; set; } = PermissionLevel.Yolo;

    /// <summary>Working folder suggested for new agents.</summary>
    public string DefaultWorkingDirectory { get; set; } = "";

    /// <summary>System notification (Windows/macOS) when an agent finishes while the game isn't focused.</summary>
    public bool DesktopNotifications { get; set; } = true;

    /// <summary>Phone ring when an agent calls you from another map.</summary>
    public bool PlaySounds { get; set; } = true;

    /// <summary>Shows agent names above their heads.</summary>
    public bool AlwaysShowNames { get; set; } = true;

    /// <summary>Instructions appended to every agent's system prompt. {name} is replaced by the agent's name.</summary>
    public string AgentInstructions { get; set; } =
        "You are {name}, a coding agent driven from the game Stardew Valley (Agent Valley mod). " +
        "The user reads your replies in a small in-game chat window: be concise, avoid big code blocks, " +
        "wide tables and emojis. You can't ask interactive questions: when you're done or need a decision, " +
        "end with a short summary of what you did, then one clear question about what to do next. " +
        "Reply in the user's language.";
}
