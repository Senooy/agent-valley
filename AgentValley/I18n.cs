using StardewModdingAPI;

namespace AgentValley;

/// <summary>
/// In-game text, read from i18n/*.json. SMAPI picks the file matching the game language
/// (i18n/fr.json for French, for example) and falls back to i18n/default.json (English).
/// </summary>
internal static class I18n
{
    private static ITranslationHelper? translations;

    public static void Init(ITranslationHelper helper) => translations = helper;

    /// <summary>Get a translation. Tokens use the {{name}} syntax in the JSON files.</summary>
    public static string Get(string key, object? tokens = null) =>
        translations?.Get(key, tokens).ToString() ?? key;
}
