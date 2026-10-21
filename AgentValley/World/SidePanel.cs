using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;
using StardewValley.Menus;
using AgentValley.UI;

namespace AgentValley.World;

/// <summary>
/// Side panel (left edge of the screen): a collapsed tab, or the list of agents with their status.
/// Clicking an agent opens its chat docked on the side, without losing sight of the farm.
/// </summary>
public sealed class SidePanel
{
    private const int PanelW = 340;
    private const int RowH = 72;
    private const int TabW = 56;
    private const int TabH = 120;

    private readonly List<(AgentRecord Agent, Rectangle Bounds)> rows = new();
    private Rectangle tab, panel, more;

    private static int Top => Math.Max(80, Game1.uiViewport.Height / 5);

    public bool Contains(AgentStore store, int x, int y) => (store.Data.SidebarExpanded ? panel : tab).Contains(x, y);

    public void Toggle(AgentStore store)
    {
        store.Data.SidebarExpanded = !store.Data.SidebarExpanded;
        store.MarkDirty();
        Game1.playSound(store.Data.SidebarExpanded ? "shwip" : "smallSelect");
    }

    /// <summary>UI coordinates. Returns true if the panel took the click.</summary>
    public bool Click(AgentStore store, int x, int y)
    {
        if (!store.Data.SidebarExpanded)
        {
            if (!tab.Contains(x, y))
                return false;
            Toggle(store);
            return true;
        }
        if (!panel.Contains(x, y))
            return false;
        if (tab.Contains(x, y))
        {
            Toggle(store);
            return true;
        }
        if (more.Contains(x, y))
        {
            ModEntry.Instance.OpenList();
            return true;
        }
        foreach (var (agent, bounds) in rows)
            if (bounds.Contains(x, y))
            {
                Game1.playSound("smallSelect");
                ModEntry.Instance.OpenChat(agent, docked: true);
                return true;
            }
        return true;
    }

    public void Draw(SpriteBatch b, AgentStore store)
    {
        rows.Clear();
        var font = Game1.smallFont;
        var working = store.Agents.Count(a => a.IsBusy);
        var waiting = store.Agents.Count(a => a.Unread);

        if (!store.Data.SidebarExpanded)
        {
            // Collapsed tab: a Junimo and two counters.
            tab = new Rectangle(-8, Top, TabW + 8, TabH);
            panel = Rectangle.Empty;
            var hover = tab.Contains(Game1.getMouseX(), Game1.getMouseY());
            IClickableMenu.drawTextureBox(b, Game1.menuTexture, new Rectangle(0, 256, 60, 60), tab.X, tab.Y, tab.Width, tab.Height, hover ? Color.Wheat : Color.White, 1f, false);
            var icon = store.Agents.FirstOrDefault(a => a.Unread) ?? store.Agents.FirstOrDefault(a => a.IsBusy) ?? store.Agents.FirstOrDefault();
            if (icon != null)
                AgentVisuals.DrawIcon(b, icon, new Vector2(8, tab.Y + 14), 2.5f);
            Widgets.Text(b, $"{store.Agents.Count}", new Vector2(14, tab.Y + 60), Widgets.TextColor);
            if (waiting > 0)
                Widgets.Badge(b, new Vector2(TabW - 22, tab.Y + 6), Widgets.OkColor);
            else if (working > 0)
                b.Draw(Game1.staminaRect, new Rectangle(10, tab.Bottom - 22, (int)(30 * (0.3 + 0.7 * Math.Abs(Math.Sin(Game1.currentGameTime.TotalGameTime.TotalSeconds * 2)))), 4), new Color(190, 120, 0));
            DrawArrow(b, new Vector2(20, tab.Bottom - 34), right: true);
            return;
        }

        var ordered = store.Agents.OrderByDescending(a => a.Unread).ThenByDescending(a => a.IsBusy).ThenByDescending(a => a.LastActivity ?? DateTime.MinValue).ToList();
        var maxRows = Math.Max(1, (Game1.uiViewport.Height - Top - 200) / RowH);
        var shown = ordered.Take(maxRows).ToList();
        var height = 70 + Math.Max(1, shown.Count) * RowH + (ordered.Count > shown.Count ? 40 : 0) + 20;
        panel = new Rectangle(-8, Top, PanelW + 8, height);
        IClickableMenu.drawTextureBox(b, Game1.menuTexture, new Rectangle(0, 256, 60, 60), panel.X, panel.Y, panel.Width, panel.Height, Color.White, 1f, false);

        // Header + collapse button.
        Widgets.Text(b, I18n.Get("sidebar.title"), new Vector2(20, Top + 20), Widgets.TextColor);
        var summary = waiting > 0 ? I18n.Get("sidebar.waiting", new { count = waiting }) : working > 0 ? I18n.Get("sidebar.working", new { count = working }) : "";
        Widgets.Text(b, summary, new Vector2(120, Top + 22), waiting > 0 ? Widgets.OkColor : Widgets.Muted, scale: 0.85f);
        tab = new Rectangle(PanelW - 52, Top + 14, 40, 40);
        DrawArrow(b, new Vector2(tab.X + 12, tab.Y + 10), right: false);

        var y = Top + 66;
        if (shown.Count == 0)
            Widgets.Text(b, I18n.Get("sidebar.empty"), new Vector2(20, y + 20), Widgets.Muted, scale: 0.9f);

        var mx = Game1.getMouseX();
        var my = Game1.getMouseY();
        foreach (var agent in shown)
        {
            var r = new Rectangle(12, y, PanelW - 24, RowH - 6);
            if (r.Contains(mx, my))
                b.Draw(Game1.staminaRect, r, Color.Black * 0.08f);
            AgentVisuals.DrawIcon(b, agent, new Vector2(r.X + 6, r.Y + 10), 3f);
            if (agent.Unread)
                Widgets.Badge(b, new Vector2(r.X + 40, r.Y + 2), agent.Status == AgentStatus.Error ? Widgets.ErrorColor : Widgets.OkColor, 16);

            var tx = r.X + 64;
            var tw = r.Width - 70;
            Widgets.Text(b, TextUtil.Ellipsize(TextUtil.Sanitize(agent.Name, font), font, tw), new Vector2(tx, r.Y + 4), Widgets.TextColor);
            var preview = agent.IsBusy ? (agent.Transcript.LastOrDefault()?.Text ?? "") : agent.LastPreview;
            var second = agent.IsBusy || agent.Unread || agent.Status == AgentStatus.Error
                ? $"{Widgets.StatusLabel(agent)} - {Backends.OneLine(preview, 120)}"
                : Widgets.StatusLabel(agent);
            var line = TextUtil.Ellipsize(TextUtil.Sanitize(second, font), font, tw / 0.8f);
            b.DrawString(font, line, new Vector2(tx, r.Y + 36), Widgets.StatusColor(agent), 0f, Vector2.Zero, 0.8f, SpriteEffects.None, 1f);
            rows.Add((agent, r));
            y += RowH;
        }

        more = Rectangle.Empty;
        if (ordered.Count > shown.Count)
        {
            more = new Rectangle(12, y, PanelW - 24, 36);
            Widgets.Text(b, I18n.Get("sidebar.more", new { count = ordered.Count - shown.Count }), new Vector2(20, y + 4), Widgets.Muted, scale: 0.9f);
        }
    }

    /// <summary>Small pixel arrow (left/right).</summary>
    private static void DrawArrow(SpriteBatch b, Vector2 pos, bool right)
    {
        for (var i = 0; i < 4; i++)
        {
            var h = (4 - i) * 6;
            var x = right ? i * 4 : (3 - i) * 4;
            b.Draw(Game1.staminaRect, new Rectangle((int)pos.X + x, (int)pos.Y + 12 - h / 2, 4, h), Widgets.TextColor);
        }
    }
}
