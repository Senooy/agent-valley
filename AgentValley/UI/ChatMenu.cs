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

/// <summary>
/// Chat window with an agent, from any map. Two shapes: centered (full screen) or docked on the
/// left side to keep the farm visible. Tabs at the top switch between agents.
/// </summary>
public sealed class ChatMenu : IClickableMenu
{
    private const int Pad = 28;
    private const int TabSize = 60;
    private const int MaxSuggestions = 8;
    private const int SuggestionH = 38;

    public AgentRecord Agent { get; }
    public bool Docked { get; }

    private readonly AgentStore store;
    private readonly TextInput input = new() { Multiline = true };
    private readonly Button send, stop, mode, move, folder, terminal, reset, delete, dock;
    private readonly Button[] buttons;

    private Rectangle tabsArea, transcriptArea, inputArea;
    private readonly List<(AgentRecord Agent, Rectangle Bounds)> tabs = new();
    private readonly List<(string Text, Color Color)> lines = new();
    private int builtRevision = -1, builtWidth = -1;
    private int scroll;
    private DateTime deleteArmedUntil = DateTime.MinValue;
    private DateTime resetArmedUntil = DateTime.MinValue;
    private bool pasteWasDown;

    private List<(string Name, string Help)> suggestions = new();
    private int selected;
    private Rectangle suggestionArea;

    public ChatMenu(AgentStore store, AgentRecord agent, bool docked = false)
    {
        this.store = store;
        Agent = agent;
        Docked = docked;
        Agent.Unread = false;
        store.MarkDirty();

        var manager = ModEntry.Instance.Manager;
        send = new Button(I18n.Get("chat.button.send"), Submit);
        stop = new Button(I18n.Get("chat.button.stop"), () => manager.Stop(store, Agent));
        mode = new Button("", () => ChatCommands.TryHandle(this, store, Agent, "/mode"));
        move = new Button(I18n.Get("chat.button.move"), () => ChatCommands.TryHandle(this, store, Agent, "/move"));
        folder = new Button(I18n.Get("chat.button.folder"), () => Shell.OpenFolder(Agent.WorkingDirectory));
        terminal = new Button(I18n.Get("chat.button.terminal"), () => Shell.OpenTerminal(Agent));
        reset = new Button(I18n.Get("chat.button.reset"), ResetSession);
        delete = new Button(I18n.Get("chat.button.delete"), Delete);
        dock = new Button(I18n.Get(docked ? "chat.button.expand" : "chat.button.dock"), () => Reopen(Agent, !Docked));
        move.Visible = folder.Visible = terminal.Visible = reset.Visible = delete.Visible = !docked;
        buttons = new[] { send, stop, mode, move, folder, terminal, reset, delete, dock };

        input.Text = ModEntry.Instance.Drafts.TryGetValue(agent.Id, out var draft) ? draft : "";
        input.OnSubmit = Submit;
        input.OnTab = AcceptSuggestion;
        Layout();
        input.Focus();
    }

    // ---------------------------------------------------------------- layout

    private void Layout()
    {
        var vw = Game1.uiViewport.Width;
        var vh = Game1.uiViewport.Height;
        if (Docked)
        {
            width = Math.Clamp((int)(vw * 0.42f), Math.Min(560, vw - 32), 820);
            height = vh - 32;
            xPositionOnScreen = 16;
            yPositionOnScreen = 16;
        }
        else
        {
            width = Math.Min(1280, vw - 64);
            height = Math.Min(920, vh - 64);
            xPositionOnScreen = (vw - width) / 2;
            yPositionOnScreen = (vh - height) / 2;
        }
        initializeUpperRightCloseButton();

        var lineH = Game1.smallFont.LineSpacing;
        var inner = width - Pad * 2;
        tabsArea = new Rectangle(xPositionOnScreen + Pad, yPositionOnScreen + Pad, inner - 60, TabSize);
        var buttonsY = yPositionOnScreen + height - Pad - 60;
        inputArea = new Rectangle(xPositionOnScreen + Pad, buttonsY - 14 - (lineH * 3 + 24), inner, lineH * 3 + 24);
        var top = tabsArea.Bottom + 96;
        transcriptArea = new Rectangle(xPositionOnScreen + Pad, top, inner, inputArea.Y - 14 - top);
        Widgets.LayoutRow(buttons, xPositionOnScreen + Pad, buttonsY, inner, 60, 8);
    }

    public override void gameWindowSizeChanged(Rectangle oldBounds, Rectangle newBounds)
    {
        base.gameWindowSizeChanged(oldBounds, newBounds);
        Layout();
        builtWidth = -1;
    }

    // ---------------------------------------------------------------- actions

    private void Reopen(AgentRecord agent, bool docked)
    {
        SaveDraft();
        input.Blur();
        Game1.activeClickableMenu = new ChatMenu(store, agent, docked);
    }

    private void SaveDraft()
    {
        if (input.Text.Trim().Length > 0)
            ModEntry.Instance.Drafts[Agent.Id] = input.Text;
        else
            ModEntry.Instance.Drafts.Remove(Agent.Id);
    }

    private void Submit()
    {
        var text = input.Text.Trim();
        if (text.Length == 0)
            return;

        // "/com" + Enter: complete with the selected suggestion first.
        if (suggestions.Count > 0 && !suggestions.Any(s => string.Equals("/" + s.Name, text, StringComparison.OrdinalIgnoreCase)))
        {
            AcceptSuggestion();
            return;
        }

        input.Text = "";
        ModEntry.Instance.Drafts.Remove(Agent.Id);
        scroll = 0;
        suggestions.Clear();

        var manager = ModEntry.Instance.Manager;
        if (text.StartsWith("!"))
            manager.RunShell(store, Agent, text[1..]);
        else if (text.Length > 1 && text.StartsWith("/") && ChatCommands.TryHandle(this, store, Agent, text))
            return;
        else
            manager.Send(store, Agent, text);
    }

    private void AcceptSuggestion()
    {
        if (suggestions.Count == 0)
            return;
        input.Text = "/" + suggestions[Math.Clamp(selected, 0, suggestions.Count - 1)].Name + " ";
        suggestions.Clear();
    }

    private void ResetSession()
    {
        if (DateTime.Now > resetArmedUntil)
        {
            resetArmedUntil = DateTime.Now.AddSeconds(3);
            return;
        }
        ChatCommands.TryHandle(this, store, Agent, "/new");
        resetArmedUntil = DateTime.MinValue;
    }

    private void Delete()
    {
        if (DateTime.Now > deleteArmedUntil)
        {
            deleteArmedUntil = DateTime.Now.AddSeconds(3);
            return;
        }
        ChatCommands.TryHandle(this, store, Agent, "/delete confirm");
    }

    // ---------------------------------------------------------------- input

    public override void update(GameTime time)
    {
        base.update(time);
        var manager = ModEntry.Instance.Manager;
        stop.Enabled = Agent.IsBusy || Agent.PendingMessages.Count > 0 || manager.IsShellRunning(Agent);
        terminal.Enabled = !Agent.IsBusy;
        send.Label = Agent.IsBusy && !input.Text.TrimStart().StartsWith("!") && !input.Text.TrimStart().StartsWith("/") ? I18n.Get("chat.button.queue") : I18n.Get("chat.button.send");
        mode.Label = Docked ? Widgets.PermissionLabel(Agent.Permission) : I18n.Get("chat.button.mode", new { mode = Widgets.PermissionLabel(Agent.Permission) });
        delete.Label = I18n.Get(DateTime.Now < deleteArmedUntil ? "chat.button.confirm" : "chat.button.delete");
        reset.Label = I18n.Get(DateTime.Now < resetArmedUntil ? "chat.button.confirm" : "chat.button.reset");

        UpdateSuggestions();

        // Ctrl+V (Windows) / Cmd+V (macOS)
        var pasteDown = TextInput.IsCtrlDown() && Keyboard.GetState().IsKeyDown(Keys.V);
        if (pasteDown && !pasteWasDown)
            input.Paste();
        pasteWasDown = pasteDown;

        if (Game1.keyboardDispatcher.Subscriber != input)
            input.Focus();
    }

    private void UpdateSuggestions()
    {
        var text = input.Text;
        if (!text.StartsWith("/") || text.Contains(' ') || text.Contains('\n'))
        {
            suggestions.Clear();
            return;
        }
        var prefix = text[1..];
        var found = ChatCommands.Suggest(store, Agent, prefix).Take(MaxSuggestions).ToList();
        if (found.Count != suggestions.Count || found.Where((s, i) => s.Name != suggestions[i].Name).Any())
            selected = 0;
        suggestions = found;
    }

    public override void receiveKeyPress(Keys key)
    {
        // The keyboard is for typing: only Escape closes the window.
        switch (key)
        {
            case Keys.Escape:
                if (suggestions.Count > 0)
                    input.Text = "";
                else
                    exitThisMenu();
                break;
            case Keys.Up when suggestions.Count > 0:
                selected = (selected - 1 + suggestions.Count) % suggestions.Count;
                break;
            case Keys.Down when suggestions.Count > 0:
                selected = (selected + 1) % suggestions.Count;
                break;
            case Keys.PageUp:
                receiveScrollWheelAction(1);
                receiveScrollWheelAction(1);
                break;
            case Keys.PageDown:
                receiveScrollWheelAction(-1);
                receiveScrollWheelAction(-1);
                break;
        }
    }

    public override void receiveLeftClick(int x, int y, bool playSound = true)
    {
        if (upperRightCloseButton != null && upperRightCloseButton.containsPoint(x, y))
        {
            exitThisMenu();
            return;
        }
        if (suggestions.Count > 0 && suggestionArea.Contains(x, y))
        {
            selected = Math.Clamp((y - suggestionArea.Y - 8) / SuggestionH, 0, suggestions.Count - 1);
            AcceptSuggestion();
            return;
        }
        foreach (var (agent, bounds) in tabs)
            if (bounds.Contains(x, y))
            {
                if (agent != Agent)
                {
                    Game1.playSound("smallSelect");
                    Reopen(agent, Docked);
                }
                return;
            }
        foreach (var btn in buttons)
            if (btn.TryClick(x, y))
                return;
        if (inputArea.Contains(x, y))
            input.Focus();
    }

    public override void receiveScrollWheelAction(int direction)
    {
        var visible = Math.Max(1, transcriptArea.Height / Game1.smallFont.LineSpacing);
        scroll = Math.Clamp(scroll + (direction > 0 ? 3 : -3), 0, Math.Max(0, lines.Count - visible));
    }

    protected override void cleanupBeforeExit()
    {
        SaveDraft();
        input.Blur();
        base.cleanupBeforeExit();
    }

    // ---------------------------------------------------------------- transcript

    private void Rebuild()
    {
        var font = Game1.smallFont;
        var w = transcriptArea.Width - 24;
        if (builtRevision == Agent.Revision && builtWidth == w)
            return;

        var oldCount = lines.Count;
        lines.Clear();
        EntryRole? previous = null;
        foreach (var entry in Agent.Transcript)
        {
            var compact = (previous == EntryRole.Tool && entry.Role == EntryRole.Tool) || (previous == EntryRole.Shell && entry.Role == EntryRole.Output);
            if (previous != null && !compact)
                lines.Add(("", Color.Transparent));

            var (prefix, color) = entry.Role switch
            {
                EntryRole.User => (I18n.Get("chat.prefix.you") + " ", Widgets.UserColor),
                EntryRole.Agent => ("", Widgets.TextColor),
                EntryRole.Tool => ("  > ", Widgets.ToolColor),
                EntryRole.Error => (I18n.Get("chat.prefix.error") + " ", Widgets.ErrorColor),
                EntryRole.Shell => ("", Widgets.ShellColor),
                EntryRole.Output => ("", Widgets.ToolColor),
                _ => ("  ", Widgets.Muted)
            };
            var body = entry.Role == EntryRole.Agent ? TextUtil.StripMarkdown(entry.Text) : entry.Text;
            if (entry.Role == EntryRole.Output)
                body = body.Length == 0 ? "..." : "    " + body.Replace("\n", "\n    ");
            if (entry.Role == EntryRole.Agent && !compact)
                lines.Add((Agent.Name + ":", Color.Lerp(AgentVisuals.Tint(Agent), Color.Black, 0.45f)));
            foreach (var line in TextUtil.Wrap(TextUtil.Sanitize(prefix + body, font), font, w))
                lines.Add((line, color));
            previous = entry.Role;
        }

        // If you were reading further up, keep the same position despite the new lines.
        if (scroll > 0 && builtWidth == w)
            scroll += lines.Count - oldCount;
        builtRevision = Agent.Revision;
        builtWidth = w;
    }

    // ---------------------------------------------------------------- drawing

    private void DrawTabs(SpriteBatch b)
    {
        tabs.Clear();
        var x = tabsArea.X;
        var ordered = store.Agents.OrderByDescending(a => a.IsBusy || a.Unread).ThenBy(a => a.Name).ToList();
        foreach (var a in ordered)
        {
            if (x + TabSize > tabsArea.Right)
            {
                Widgets.Text(b, $"+{ordered.Count - tabs.Count}", new Vector2(x + 4, tabsArea.Y + 16), Widgets.Muted);
                break;
            }
            var r = new Rectangle(x, tabsArea.Y, TabSize, TabSize);
            var current = a == Agent;
            var hover = r.Contains(Game1.getMouseX(), Game1.getMouseY());
            IClickableMenu.drawTextureBox(b, Game1.mouseCursors, new Rectangle(432, 439, 9, 9), r.X, r.Y, r.Width, r.Height,
                current ? new Color(255, 225, 160) : hover ? Color.Wheat : Color.White, 4f, false);
            AgentVisuals.DrawIcon(b, a, new Vector2(r.X + 6, r.Y + 6), 3f);
            if (a.IsBusy)
                b.Draw(Game1.staminaRect, new Rectangle(r.X + 8, r.Bottom - 10, (int)((r.Width - 16) * (0.3 + 0.7 * Math.Abs(Math.Sin(Game1.currentGameTime.TotalGameTime.TotalSeconds * 2)))), 4), new Color(190, 120, 0));
            if (a.Unread)
                Widgets.Badge(b, new Vector2(r.Right - 14, r.Y - 4), a.Status == AgentStatus.Error ? Widgets.ErrorColor : Widgets.OkColor);
            tabs.Add((a, r));
            x += TabSize + 6;
        }
    }

    public override void draw(SpriteBatch b)
    {
        if (!Docked)
            b.Draw(Game1.fadeToBlackRect, Game1.graphics.GraphicsDevice.Viewport.Bounds, Color.Black * 0.55f);
        Widgets.Panel(b, new Rectangle(xPositionOnScreen, yPositionOnScreen, width, height));

        var font = Game1.smallFont;
        var x = xPositionOnScreen + Pad;
        var manager = ModEntry.Instance.Manager;

        DrawTabs(b);

        // Header
        var y = tabsArea.Bottom + 14;
        AgentVisuals.DrawIcon(b, Agent, new Vector2(x, y + 4), 4f);
        var status = Widgets.StatusLabel(Agent);
        var statusSize = font.MeasureString(TextUtil.Sanitize(status, font));
        var nameMax = width - Pad * 2 - 90 - statusSize.X - 16;
        Widgets.Text(b, TextUtil.Ellipsize(TextUtil.Sanitize(Agent.Name, Game1.dialogueFont), Game1.dialogueFont, nameMax), new Vector2(x + 80, y - 4), Widgets.TextColor, Game1.dialogueFont);
        Widgets.Text(b, status, new Vector2(xPositionOnScreen + width - Pad - statusSize.X, y + 8), Widgets.StatusColor(Agent));

        var location = Game1.getLocationFromName(Agent.LocationName)?.DisplayName ?? Agent.LocationName;
        var model = string.IsNullOrWhiteSpace(Agent.Model) ? "" : $" ({Agent.Model})";
        var sub = $"{Widgets.KindLabel(Agent.Kind)}{model}  -  {location}  -  {Agent.WorkingDirectory}";
        Widgets.Text(b, TextUtil.Ellipsize(TextUtil.Sanitize(sub, font), font, width - Pad * 2 - 90), new Vector2(x + 80, y + 46), Widgets.Muted);

        // Transcript
        Widgets.Inset(b, transcriptArea);
        Rebuild();
        var lineH = font.LineSpacing;
        var visible = Math.Max(1, (transcriptArea.Height - 16) / lineH);
        var shellRunning = manager.IsShellRunning(Agent);
        var footer = Agent.IsBusy || shellRunning ? 1 : 0;
        var available = visible - footer;
        scroll = Math.Clamp(scroll, 0, Math.Max(0, lines.Count - available));
        var start = Math.Max(0, lines.Count - available - scroll);
        var ty = transcriptArea.Y + 8;
        if (lines.Count == 0)
        {
            var hello = TextUtil.Wrap(TextUtil.Sanitize(I18n.Get("chat.empty", new { name = Agent.Name, path = Agent.WorkingDirectory }), font), font, transcriptArea.Width - 24);
            foreach (var l in hello)
            {
                b.DrawString(font, l, new Vector2(transcriptArea.X + 12, ty), Widgets.Muted);
                ty += lineH;
            }
        }
        for (var i = start; i < Math.Min(lines.Count, start + available); i++)
        {
            if (lines[i].Text.Length > 0)
                b.DrawString(font, lines[i].Text, new Vector2(transcriptArea.X + 12, ty), lines[i].Color);
            ty += lineH;
        }
        if (footer > 0)
        {
            var dots = new string('.', 1 + (int)(Game1.currentGameTime.TotalGameTime.TotalMilliseconds / 400) % 3);
            var queued = Agent.PendingMessages.Count > 0 ? "  " + I18n.Get("chat.queued-count", new { count = Agent.PendingMessages.Count }) : "";
            var what = Agent.IsBusy ? I18n.Get("chat.working", new { name = Agent.Name }) + dots + queued : I18n.Get("chat.shell-running") + dots;
            Widgets.Text(b, what, new Vector2(transcriptArea.X + 12, transcriptArea.Bottom - 8 - lineH), Widgets.Muted);
        }
        if (scroll > 0)
        {
            var hint = TextUtil.Sanitize(I18n.Get("chat.scroll-hint"), font);
            var hs = font.MeasureString(hint);
            b.DrawString(font, hint, new Vector2(transcriptArea.Right - hs.X - 12, transcriptArea.Bottom - hs.Y - 4), Widgets.Muted);
        }

        // Input
        var isShell = input.Text.StartsWith("!");
        IClickableMenu.drawTextureBox(b, Game1.mouseCursors, new Rectangle(384, 373, 18, 18), inputArea.X, inputArea.Y, inputArea.Width, inputArea.Height,
            isShell ? new Color(210, 255, 210) : Color.White, 4f, false);
        var inputText = TextUtil.Sanitize(input.Text, font);
        var caret = input.Selected && (int)(Game1.currentGameTime.TotalGameTime.TotalMilliseconds / 500) % 2 == 0 ? "|" : "";
        if (inputText.Length == 0)
        {
            var placeholder = I18n.Get(Docked ? "chat.placeholder.docked" : "chat.placeholder", new { name = Agent.Name });
            Widgets.Text(b, TextUtil.Ellipsize(TextUtil.Sanitize(placeholder, font), font, inputArea.Width - 40), new Vector2(inputArea.X + 16, inputArea.Y + 12), Widgets.Muted);
            b.DrawString(font, caret, new Vector2(inputArea.X + 12, inputArea.Y + 12), Widgets.TextColor);
        }
        else
        {
            var wrapped = TextUtil.Wrap(inputText + caret, font, inputArea.Width - 32);
            var shown = wrapped.Skip(Math.Max(0, wrapped.Count - 3)).ToList();
            for (var i = 0; i < shown.Count; i++)
                b.DrawString(font, shown[i], new Vector2(inputArea.X + 16, inputArea.Y + 12 + i * lineH), isShell ? Widgets.ShellColor : Widgets.TextColor);
        }

        foreach (var btn in buttons)
            btn.Draw(b);

        DrawSuggestions(b);

        base.draw(b);
        drawMouse(b);
    }

    private void DrawSuggestions(SpriteBatch b)
    {
        if (suggestions.Count == 0)
            return;
        var font = Game1.smallFont;
        var w = Math.Min(inputArea.Width, 720);
        var h = suggestions.Count * SuggestionH + 16;
        suggestionArea = new Rectangle(inputArea.X, inputArea.Y - h - 4, w, h);
        IClickableMenu.drawTextureBox(b, Game1.menuTexture, new Rectangle(0, 256, 60, 60), suggestionArea.X, suggestionArea.Y, suggestionArea.Width, suggestionArea.Height, Color.White, 1f, false);
        for (var i = 0; i < suggestions.Count; i++)
        {
            var row = new Rectangle(suggestionArea.X + 12, suggestionArea.Y + 8 + i * SuggestionH, w - 24, SuggestionH);
            if (i == selected)
                b.Draw(Game1.staminaRect, row, new Color(255, 210, 140) * 0.6f);
            var name = TextUtil.Sanitize("/" + suggestions[i].Name, font);
            var nameW = font.MeasureString(name).X;
            b.DrawString(font, name, new Vector2(row.X + 8, row.Y + 4), Widgets.TextColor);
            var help = TextUtil.Ellipsize(TextUtil.Sanitize(suggestions[i].Help, font), font, row.Width - nameW - 40);
            b.DrawString(font, help, new Vector2(row.X + nameW + 28, row.Y + 4), Widgets.Muted);
        }
        var hint = TextUtil.Sanitize(I18n.Get("chat.suggestions-hint"), font);
        var hs = font.MeasureString(hint) * 0.8f;
        b.DrawString(font, hint, new Vector2(suggestionArea.Right - hs.X - 12, suggestionArea.Y - hs.Y - 2), Color.White, 0f, Vector2.Zero, 0.8f, SpriteEffects.None, 1f);
    }
}
