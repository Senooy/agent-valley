using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;

namespace AgentValley.World;

/// <summary>Agents are tinted Junimos: orange for Claude, blue/green for Codex.</summary>
public static class AgentVisuals
{
    private const int EmoteExclamation = 16;
    private const int EmoteX = 36;
    private const int EmotePause = 40;

    private static readonly Color[] ClaudeColors =
    {
        new(217, 119, 87), new(240, 160, 110), new(196, 92, 70), new(230, 190, 120)
    };

    private static readonly Color[] CodexColors =
    {
        new(110, 150, 255), new(90, 205, 190), new(170, 135, 255), new(225, 230, 240)
    };

    private static Texture2D? junimo;

    public static Texture2D Junimo => junimo ??= Game1.content.Load<Texture2D>("Characters\\Junimo");

    public static Color Tint(AgentRecord a)
    {
        var palette = a.Kind == AgentKind.Codex ? CodexColors : ClaudeColors;
        return palette[Math.Abs(a.ColorIndex) % palette.Length];
    }

    private static double Now => Game1.currentGameTime?.TotalGameTime.TotalMilliseconds ?? 0;

    public static Rectangle Frame(AgentRecord a)
    {
        var perRow = Math.Max(1, Junimo.Width / 16);
        // Ligne 0 : petits sauts (au travail). Frames 8-11 : respiration (au repos).
        var frame = a.IsBusy
            ? (int)(Now / 90) % 8
            : 8 + (int)((Now + a.Id.GetHashCode() % 1000) / 180) % 4;
        return new Rectangle(frame % perRow * 16, frame / perRow * 16, 16, 16);
    }

    /// <summary>Draws the agent at a screen position (top-left corner of the tile).</summary>
    public static void DrawInWorld(SpriteBatch b, AgentRecord a, Vector2 local, float layerDepth, float alpha, bool showName)
    {
        var shadow = Game1.shadowTexture;
        b.Draw(shadow, local + new Vector2(32, 58), shadow.Bounds, Color.White * (0.6f * alpha), 0f,
            new Vector2(shadow.Bounds.Center.X, shadow.Bounds.Center.Y), 3.5f, SpriteEffects.None, Math.Max(0f, layerDepth - 0.0001f));

        b.Draw(Junimo, local + new Vector2(0, -8), Frame(a), Tint(a) * alpha, 0f, Vector2.Zero, 4f, SpriteEffects.None, layerDepth);

        var top = local.Y - 12;
        if (showName)
        {
            var font = Game1.smallFont;
            var name = UI.TextUtil.Sanitize(a.Name, font);
            var size = font.MeasureString(name) * 0.8f;
            var pos = new Vector2(local.X + 32 - size.X / 2, top - size.Y);
            b.DrawString(font, name, pos + new Vector2(2, 2), Color.Black * (0.35f * alpha), 0f, Vector2.Zero, 0.8f, SpriteEffects.None, 0.9999f);
            b.DrawString(font, name, pos, Color.White * alpha, 0f, Vector2.Zero, 0.8f, SpriteEffects.None, 1f);
            top = pos.Y;
        }

        var emote = a.IsBusy ? EmotePause : a.Unread ? (a.Status == AgentStatus.Error ? EmoteX : EmoteExclamation) : -1;
        if (emote >= 0 && Game1.emoteSpriteSheet != null)
        {
            var index = emote + (int)(Now / 200) % 4;
            var perRow = Game1.emoteSpriteSheet.Width / 16;
            var src = new Rectangle(index % perRow * 16, index / perRow * 16, 16, 16);
            b.Draw(Game1.emoteSpriteSheet, new Vector2(local.X, top - 60), src, Color.White * alpha, 0f, Vector2.Zero, 4f, SpriteEffects.None, 1f);
        }
    }

    /// <summary>Icon for menus.</summary>
    public static void DrawIcon(SpriteBatch b, AgentRecord a, Vector2 pos, float scale = 3f)
    {
        b.Draw(Junimo, pos, Frame(a), Tint(a), 0f, Vector2.Zero, scale, SpriteEffects.None, 1f);
    }
}
