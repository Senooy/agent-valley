using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewValley;
using StardewValley.Menus;
using AgentValley.World;

namespace AgentValley.UI;

/// <summary>The "phone": every agent of the save, wherever they are.</summary>
public sealed class AgentListMenu : IClickableMenu
{
    private const int Pad = 40;
    private const int RowH = 104;

    private readonly AgentStore store;
    private readonly Button newAgent;
    private readonly List<(AgentRecord Agent, Rectangle Row, Button Chat, Button Go, Button Here)> rows = new();
    private int scroll;
    private int visibleRows;

    public AgentListMenu(AgentStore store)
    {
        this.store = store;
        newAgent = new Button(I18n.Get("list.new-agent"), () =>
        {
            exitThisMenu();
            ModEntry.Instance.OpenCreateMenu();
        });
        Layout();
    }

    private void Layout()
    {
        width = Math.Min(1180, Game1.uiViewport.Width - 64);
        height = Math.Min(860, Game1.uiViewport.Height - 64);
        xPositionOnScreen = (Game1.uiViewport.Width - width) / 2;
        yPositionOnScreen = (Game1.uiViewport.Height - height) / 2;
        initializeUpperRightCloseButton();
        newAgent.Bounds = new Rectangle(xPositionOnScreen + width - Pad - 300, yPositionOnScreen + height - Pad - 64, 300, 64);
        visibleRows = Math.Max(1, (height - Pad * 2 - 100 - 80) / RowH);
    }

    public override void gameWindowSizeChanged(Rectangle oldBounds, Rectangle newBounds)
    {
        base.gameWindowSizeChanged(oldBounds, newBounds);
        Layout();
    }

    private IEnumerable<AgentRecord> Ordered() =>
        store.Agents.OrderByDescending(a => a.Unread).ThenByDescending(a => a.IsBusy).ThenByDescending(a => a.LastActivity ?? DateTime.MinValue);

    private void BuildRows()
    {
        rows.Clear();
        var x = xPositionOnScreen + Pad;
        var w = width - Pad * 2;
        var y = yPositionOnScreen + Pad + 100;
        foreach (var agent in Ordered().Skip(scroll).Take(visibleRows))
        {
            var a = agent;
            var row = new Rectangle(x, y, w, RowH - 12);
            var bx = row.Right - 16;
            var here = new Button(I18n.Get("list.here"), () => { exitThisMenu(); ModEntry.Instance.Placement.Begin(store, a, isNew: false); });
            var go = new Button(I18n.Get("list.go"), () => { exitThisMenu(); ModEntry.Instance.WarpTo(a); });
            var chat = new Button(I18n.Get("list.chat"), () => ModEntry.Instance.OpenChat(a));
            here.Bounds = new Rectangle(bx -= 120, row.Y + 14, 120, 64);
            go.Bounds = new Rectangle(bx -= 160 + 10, row.Y + 14, 160, 64);
            chat.Bounds = new Rectangle(bx -= 170 + 10, row.Y + 14, 170, 64);
            rows.Add((a, row, chat, go, here));
            y += RowH;
        }
    }

    public override void receiveKeyPress(Keys key)
    {
        if (key == Keys.Escape || Game1.options.doesInputListContain(Game1.options.menuButton, key))
            exitThisMenu();
    }

    public override void receiveScrollWheelAction(int direction)
    {
        scroll = Math.Clamp(scroll + (direction > 0 ? -1 : 1), 0, Math.Max(0, store.Agents.Count - visibleRows));
    }

    public override void receiveLeftClick(int x, int y, bool playSound = true)
    {
        if (upperRightCloseButton != null && upperRightCloseButton.containsPoint(x, y))
        {
            exitThisMenu();
            return;
        }
        if (newAgent.TryClick(x, y))
            return;
        foreach (var r in rows)
        {
            if (r.Chat.TryClick(x, y) || r.Go.TryClick(x, y) || r.Here.TryClick(x, y))
                return;
            if (r.Row.Contains(x, y))
            {
                Game1.playSound("smallSelect");
                ModEntry.Instance.OpenChat(r.Agent);
                return;
            }
        }
    }

    public override void draw(SpriteBatch b)
    {
        BuildRows();
        b.Draw(Game1.fadeToBlackRect, Game1.graphics.GraphicsDevice.Viewport.Bounds, Color.Black * 0.55f);
        Widgets.Panel(b, new Rectangle(xPositionOnScreen, yPositionOnScreen, width, height));

        var font = Game1.smallFont;
        var x = xPositionOnScreen + Pad;
        Widgets.Text(b, I18n.Get("list.title"), new Vector2(x, yPositionOnScreen + Pad), Widgets.TextColor, Game1.dialogueFont);
        var working = store.Agents.Count(a => a.IsBusy);
        var waiting = store.Agents.Count(a => a.Unread);
        Widgets.Text(b, I18n.Get("list.summary", new { count = store.Agents.Count, working, waiting }), new Vector2(x + 260, yPositionOnScreen + Pad + 14), Widgets.Muted);

        if (store.Agents.Count == 0)
            Widgets.Text(b, I18n.Get("list.empty"), new Vector2(x, yPositionOnScreen + Pad + 120), Widgets.Muted);

        var mx = Game1.getMouseX();
        var my = Game1.getMouseY();
        foreach (var (agent, row, chat, go, here) in rows)
        {
            Widgets.Inset(b, row, row.Contains(mx, my) ? 0.12f : 0.06f);
            AgentVisuals.DrawIcon(b, agent, new Vector2(row.X + 16, row.Y + 18), 3.5f);
            if (agent.Unread)
                b.Draw(Game1.mouseCursors, new Vector2(row.X + 60, row.Y + 6), new Rectangle(403, 496, 5, 14), Color.White, 0f, Vector2.Zero, 3f, SpriteEffects.None, 1f);

            var tx = row.X + 92;
            var textW = chat.Bounds.X - tx - 16;
            Widgets.Text(b, TextUtil.Ellipsize(TextUtil.Sanitize(agent.Name, font), font, textW * 0.55f), new Vector2(tx, row.Y + 10), Widgets.TextColor);
            var status = Widgets.StatusLabel(agent);
            var nameW = Math.Min(font.MeasureString(TextUtil.Sanitize(agent.Name, font)).X, textW * 0.55f);
            Widgets.Text(b, status, new Vector2(tx + nameW + 24, row.Y + 10), Widgets.StatusColor(agent));

            var loc = Game1.getLocationFromName(agent.LocationName)?.DisplayName ?? agent.LocationName;
            var preview = agent.IsBusy ? (agent.Transcript.LastOrDefault()?.Text ?? "") : agent.LastPreview;
            var second = $"{Widgets.KindLabel(agent.Kind)} - {loc} - {Backends.OneLine(preview, 200)}";
            Widgets.Text(b, TextUtil.Ellipsize(TextUtil.Sanitize(second, font), font, textW), new Vector2(tx, row.Y + 50), Widgets.Muted);

            chat.Draw(b);
            go.Draw(b);
            here.Draw(b);
        }

        if (store.Agents.Count > visibleRows)
            Widgets.Text(b, I18n.Get("list.scroll", new { from = scroll + 1, to = Math.Min(store.Agents.Count, scroll + visibleRows), count = store.Agents.Count }), new Vector2(x, newAgent.Bounds.Y + 18), Widgets.Muted);
        else
            Widgets.Text(b, I18n.Get("list.hint"), new Vector2(x, newAgent.Bounds.Y + 18), Widgets.Muted);

        newAgent.Draw(b);
        base.draw(b);
        drawMouse(b);
    }
}
