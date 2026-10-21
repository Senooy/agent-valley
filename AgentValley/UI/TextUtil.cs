using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Xna.Framework.Graphics;

namespace AgentValley.UI;

/// <summary>The game fonts have a limited character set: anything missing would crash MeasureString.</summary>
public static class TextUtil
{
    private static readonly Dictionary<SpriteFont, HashSet<char>> Charsets = new();

    public static string Sanitize(string text, SpriteFont font)
    {
        if (!Charsets.TryGetValue(font, out var chars))
            Charsets[font] = chars = new HashSet<char>(font.Characters);

        var sb = new StringBuilder(text.Length);
        foreach (var raw in text.Replace("\r\n", "\n"))
        {
            switch (raw)
            {
                case '\n': sb.Append('\n'); continue;
                case '\t': sb.Append("  "); continue;
                case '\u2018' or '\u2019' or '\u02BC': sb.Append('\''); continue;                                  // curly single quotes
                case '\u201C' or '\u201D' or '\u00AB' or '\u00BB' when !chars.Contains(raw): sb.Append('"'); continue; // curly quotes, guillemets
                case '\u2013' or '\u2014' or '\u2212': sb.Append('-'); continue;                                   // dashes, minus
                case '\u2026': sb.Append("..."); continue;                                                         // ellipsis
                case '\u2192': sb.Append("->"); continue;                                                          // right arrow
                case '\u2190': sb.Append("<-"); continue;                                                          // left arrow
                case '\u2022' or '\u25CF' or '\u25AA': sb.Append('-'); continue;                                   // bullets
                case '\u00A0' or '\u202F' or '\u2009': sb.Append(' '); continue;                                   // non-breaking / thin spaces
                case '\u2713' or '\u2714' or '\u2705': sb.Append("[ok]"); continue;                                // check marks
                case '\u2717' or '\u2718' or '\u274C': sb.Append("[x]"); continue;                                 // crosses
            }
            if (char.IsSurrogate(raw) || char.IsControl(raw))
                continue;
            if (chars.Contains(raw))
                sb.Append(raw);
            else if (font.DefaultCharacter.HasValue)
                sb.Append(font.DefaultCharacter.Value);
            else
                sb.Append('?');
        }
        return sb.ToString();
    }

    /// <summary>Lightens markdown for the small chat window.</summary>
    public static string StripMarkdown(string text)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n').Select(line =>
        {
            var t = line.TrimStart();
            if (t.StartsWith("```"))
                return "----";
            if (t.StartsWith("#"))
                return t.TrimStart('#').Trim();
            return line;
        });
        return string.Join("\n", lines).Replace("**", "").Replace("__", "").Replace("`", "'");
    }

    /// <summary>Splits already-sanitized text into lines of a max width.</summary>
    public static List<string> Wrap(string text, SpriteFont font, float maxWidth, float scale = 1f)
    {
        var result = new List<string>();
        foreach (var paragraph in text.Split('\n'))
        {
            if (paragraph.Length == 0)
            {
                result.Add("");
                continue;
            }

            var indent = new string(' ', paragraph.Length - paragraph.TrimStart(' ').Length);
            var line = new StringBuilder();
            foreach (var word in paragraph.TrimStart(' ').Split(' '))
            {
                var candidate = line.Length == 0 ? indent + word : line + " " + word;
                if (font.MeasureString(candidate).X * scale <= maxWidth)
                {
                    line.Clear().Append(candidate);
                    continue;
                }

                if (line.Length > 0)
                {
                    result.Add(line.ToString());
                    line.Clear();
                }

                // Word longer than a line (paths, URLs): cut it character by character.
                var piece = new StringBuilder(indent);
                foreach (var ch in word)
                {
                    if (font.MeasureString(piece.ToString() + ch).X * scale > maxWidth && piece.Length > 0)
                    {
                        result.Add(piece.ToString());
                        piece.Clear();
                    }
                    piece.Append(ch);
                }
                line.Append(piece);
            }
            result.Add(line.ToString());
        }
        return result;
    }

    public static string Ellipsize(string text, SpriteFont font, float maxWidth)
    {
        if (font.MeasureString(text).X <= maxWidth)
            return text;
        while (text.Length > 1 && font.MeasureString(text + "...").X > maxWidth)
            text = text[..^1];
        return text + "...";
    }
}
