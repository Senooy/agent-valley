using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;
using StardewValley.Menus;

namespace AgentValley.UI;

/// <summary>Small hand-drawn button (game box + label).</summary>
public sealed class Button
{
    public Rectangle Bounds;
    public string Label;
    public bool Enabled = true;
    public bool Visible = true;
    public bool Active;
    public Action? OnClick;

    public Button(string label, Action? onClick = null)
    {
        Label = label;
        OnClick = onClick;
    }

    public bool Contains(int x, int y) => Visible && Bounds.Contains(x, y);

    public bool TryClick(int x, int y)
    {
        if (!Contains(x, y) || !Enabled)
            return false;
        Game1.playSound("smallSelect");
        OnClick?.Invoke();
        return true;
    }

    public void Draw(SpriteBatch b)
    {
        if (!Visible)
            return;
        var hover = Enabled && Bounds.Contains(Game1.getMouseX(), Game1.getMouseY());
        var color = !Enabled ? Color.Gray * 0.6f : Active ? new Color(255, 225, 160) : hover ? Color.Wheat : Color.White;
        IClickableMenu.drawTextureBox(b, Game1.mouseCursors, new Rectangle(432, 439, 9, 9), Bounds.X, Bounds.Y, Bounds.Width, Bounds.Height, color, 4f, false);
        var font = Game1.smallFont;
        var label = TextUtil.Ellipsize(TextUtil.Sanitize(Label, font), font, Bounds.Width - 16);
        var size = font.MeasureString(label);
        var pos = new Vector2(Bounds.X + (Bounds.Width - size.X) / 2, Bounds.Y + (Bounds.Height - size.Y) / 2 + 2);
        b.DrawString(font, label, pos, Enabled ? Game1.textColor : Game1.textColor * 0.45f);
    }
}

public static class Widgets
{
    public static readonly Color TextColor = new(86, 22, 12);
    public static readonly Color Muted = new(120, 100, 80);
    public static readonly Color UserColor = new(30, 70, 150);
    public static readonly Color ToolColor = new(105, 95, 85);
    public static readonly Color ErrorColor = new(185, 35, 35);
    public static readonly Color OkColor = new(40, 120, 50);
    public static readonly Color ShellColor = new(30, 110, 60);

    /// <summary>Small badge (unread, error...).</summary>
    public static void Badge(SpriteBatch b, Vector2 pos, Color color, int size = 18)
    {
        b.Draw(Game1.staminaRect, new Rectangle((int)pos.X - 2, (int)pos.Y - 2, size + 4, size + 4), Color.Black * 0.5f);
        b.Draw(Game1.staminaRect, new Rectangle((int)pos.X, (int)pos.Y, size, size), color);
        b.Draw(Game1.staminaRect, new Rectangle((int)pos.X + size / 2 - 1, (int)pos.Y + 3, 3, size - 9), Color.White);
        b.Draw(Game1.staminaRect, new Rectangle((int)pos.X + size / 2 - 1, (int)pos.Y + size - 5, 3, 3), Color.White);
    }

    public static void Panel(SpriteBatch b, Rectangle r) =>
        IClickableMenu.drawTextureBox(b, Game1.menuTexture, new Rectangle(0, 256, 60, 60), r.X, r.Y, r.Width, r.Height, Color.White, 1f, true);

    public static void Inset(SpriteBatch b, Rectangle r, float alpha = 0.08f) =>
        b.Draw(Game1.staminaRect, r, Color.Black * alpha);

    public static void Text(SpriteBatch b, string text, Vector2 pos, Color color, SpriteFont? font = null, float scale = 1f)
    {
        font ??= Game1.smallFont;
        b.DrawString(font, TextUtil.Sanitize(text, font), pos, color, 0f, Vector2.Zero, scale, SpriteEffects.None, 1f);
    }

    public static string StatusLabel(AgentRecord a) => a.Status switch
    {
        AgentStatus.Working => I18n.Get("status.working"),
        AgentStatus.Done => I18n.Get(a.Unread ? "status.waiting" : "status.finished"),
        AgentStatus.Error => I18n.Get("status.error"),
        AgentStatus.Interrupted => I18n.Get("status.interrupted"),
        _ => I18n.Get("status.idle")
    };

    public static Color StatusColor(AgentRecord a) => a.Status switch
    {
        AgentStatus.Working => new Color(190, 120, 0),
        AgentStatus.Done => OkColor,
        AgentStatus.Error => ErrorColor,
        _ => Muted
    };

    public static string PermissionLabel(PermissionLevel p) => p switch
    {
        PermissionLevel.Yolo => I18n.Get("permission.yolo"),
        PermissionLevel.Edits => I18n.Get("permission.edits"),
        _ => I18n.Get("permission.readonly")
    };

    public static PermissionLevel Next(PermissionLevel p) => p switch
    {
        PermissionLevel.Yolo => PermissionLevel.Edits,
        PermissionLevel.Edits => PermissionLevel.ReadOnly,
        _ => PermissionLevel.Yolo
    };

    public static string KindLabel(AgentKind k) => k == AgentKind.Codex ? "Codex" : "Claude Code";

    /// <summary>Lays out buttons left to right on one row.</summary>
    public static void LayoutRow(Button[] buttons, int x, int y, int totalWidth, int height, int gap = 12)
    {
        var visible = Array.FindAll(buttons, btn => btn.Visible);
        if (visible.Length == 0)
            return;
        var w = (totalWidth - gap * (visible.Length - 1)) / visible.Length;
        for (var i = 0; i < visible.Length; i++)
            visible[i].Bounds = new Rectangle(x + i * (w + gap), y, w, height);
    }
}
