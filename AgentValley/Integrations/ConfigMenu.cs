using System;
using System.Linq;
using StardewModdingAPI;

namespace AgentValley.Integrations;

/// <summary>Registers the mod's options in Generic Mod Config Menu, when it's installed.</summary>
internal static class ConfigMenu
{
    private const string GmcmId = "spacechase0.GenericModConfigMenu";

    public static void Register(ModEntry mod)
    {
        var api = mod.Helper.ModRegistry.GetApi<IGenericModConfigMenuApi>(GmcmId);
        if (api == null)
            return;

        var manifest = mod.ModManifest;
        ModConfig C() => mod.Config;

        api.Register(manifest, reset: mod.ResetConfig, save: mod.SaveConfig);

        // Keys
        api.AddSectionTitle(manifest, () => I18n.Get("config.section.keys"));
        api.AddKeybindList(manifest, () => C().NewAgent, v => C().NewAgent = v, Name("new-agent"), Tooltip("new-agent"));
        api.AddKeybindList(manifest, () => C().ToggleSidebar, v => C().ToggleSidebar = v, Name("toggle-sidebar"), Tooltip("toggle-sidebar"));
        api.AddKeybindList(manifest, () => C().AnswerLastCall, v => C().AnswerLastCall = v, Name("answer-last-call"), Tooltip("answer-last-call"));
        api.AddKeybindList(manifest, () => C().OpenAgentList, v => C().OpenAgentList = v, Name("open-agent-list"), Tooltip("open-agent-list"));

        // New agents
        api.AddSectionTitle(manifest, () => I18n.Get("config.section.agents"));
        var permissions = Enum.GetNames(typeof(PermissionLevel));
        api.AddTextOption(manifest,
            () => C().DefaultPermission.ToString(),
            v => C().DefaultPermission = Enum.TryParse<PermissionLevel>(v, out var p) ? p : PermissionLevel.Yolo,
            Name("default-permission"), Tooltip("default-permission"),
            allowedValues: permissions,
            formatAllowedValue: v => UI.Widgets.PermissionLabel(Enum.Parse<PermissionLevel>(v)));
        api.AddTextOption(manifest, () => C().DefaultWorkingDirectory, v => C().DefaultWorkingDirectory = v.Trim(), Name("default-folder"), Tooltip("default-folder"));
        api.AddTextOption(manifest, () => C().ClaudeModel, v => C().ClaudeModel = v.Trim(), Name("claude-model"), Tooltip("claude-model"));
        api.AddTextOption(manifest, () => C().CodexModel, v => C().CodexModel = v.Trim(), Name("codex-model"), Tooltip("codex-model"));
        api.AddTextOption(manifest, () => C().AgentInstructions, v => C().AgentInstructions = v, Name("instructions"), Tooltip("instructions"));

        // Notifications & display
        api.AddSectionTitle(manifest, () => I18n.Get("config.section.notifications"));
        api.AddBoolOption(manifest, () => C().PlaySounds, v => C().PlaySounds = v, Name("sounds"), Tooltip("sounds"));
        api.AddBoolOption(manifest, () => C().DesktopNotifications, v => C().DesktopNotifications = v, Name("desktop-notifications"), Tooltip("desktop-notifications"));
        api.AddBoolOption(manifest, () => C().AlwaysShowNames, v => C().AlwaysShowNames = v, Name("show-names"), Tooltip("show-names"));

        // CLIs
        api.AddSectionTitle(manifest, () => I18n.Get("config.section.cli"));
        api.AddParagraph(manifest, () => I18n.Get("config.cli.paragraph"));
        api.AddTextOption(manifest, () => C().ClaudePath, v => C().ClaudePath = v.Trim().Trim('"'), Name("claude-path"), Tooltip("claude-path"));
        api.AddTextOption(manifest, () => C().CodexPath, v => C().CodexPath = v.Trim().Trim('"'), Name("codex-path"), Tooltip("codex-path"));
        if (CliLocator.IsWindows)
        {
            var shells = new[] { "powershell", "pwsh", "cmd" };
            api.AddTextOption(manifest,
                () => shells.Contains(C().WindowsShell.Trim().ToLowerInvariant()) ? C().WindowsShell.Trim().ToLowerInvariant() : "powershell",
                v => C().WindowsShell = v,
                Name("windows-shell"), Tooltip("windows-shell"),
                allowedValues: shells);
        }
    }

    private static Func<string> Name(string key) => () => I18n.Get($"config.{key}.name");

    private static Func<string> Tooltip(string key) => () => I18n.Get($"config.{key}.tooltip");
}
