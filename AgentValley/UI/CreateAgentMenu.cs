using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewValley;
using StardewValley.Menus;

namespace AgentValley.UI;

/// <summary>Agent creation: type, name, folder, permissions, model. Then it gets placed at the cursor.</summary>
public sealed class CreateAgentMenu : IClickableMenu
{
    private const int Pad = 40;
    private const int FieldH = 64;

    private readonly AgentStore store;
    private AgentKind kind;
    private PermissionLevel permission;

    private readonly TextInput name = new() { MaxLength = 32 };
    private readonly TextInput folder = new() { MaxLength = 400 };
    private readonly TextInput model = new() { MaxLength = 60 };
    private readonly TextInput[] fields;
    private TextInput focused;

    private readonly Button claude, codex, perm, browse, create, cancel;
    private readonly List<Button> recent = new();
    private Rectangle nameBox, folderBox, modelBox;
    private string error = "";
    private bool picking;
    private bool pasteWasDown;
    private string autoName = "";

    public CreateAgentMenu(AgentStore store, ModConfig config)
    {
        this.store = store;
        permission = config.DefaultPermission;

        claude = new Button("Claude Code", () => SetKind(AgentKind.Claude));
        codex = new Button("Codex", () => SetKind(AgentKind.Codex));
        perm = new Button("", () => permission = Widgets.Next(permission));
        browse = new Button(I18n.Get("create.browse"), Browse);
        create = new Button(I18n.Get("create.submit"), Create);
        cancel = new Button(I18n.Get("create.cancel"), () => exitThisMenu());

        folder.Text = store.Data.RecentFolders.FirstOrDefault(Directory.Exists)
            ?? (string.IsNullOrWhiteSpace(config.DefaultWorkingDirectory) ? "" : CliLocator.ExpandHome(config.DefaultWorkingDirectory));
        foreach (var dir in store.Data.RecentFolders.Where(Directory.Exists).Take(4))
        {
            var d = dir;
            recent.Add(new Button(Path.GetFileName(d.TrimEnd('/', '\\')) is { Length: > 0 } n ? n : d, () => folder.Text = d));
        }

        fields = new[] { name, folder, model };
        foreach (var f in fields)
        {
            f.OnSubmit = Create;
            f.OnTab = FocusNext;
        }

        SetKind(AgentKind.Claude);
        Layout();
        focused = name;
        name.Focus();
    }

    private void SetKind(AgentKind k)
    {
        kind = k;
        claude.Active = k == AgentKind.Claude;
        codex.Active = k == AgentKind.Codex;
        if (name.Text.Trim().Length == 0 || name.Text == autoName)
        {
            autoName = $"{(k == AgentKind.Claude ? "Claude" : "Codex")} {store.Agents.Count(a => a.Kind == k) + 1}";
            name.Text = autoName;
        }
    }

    private void Layout()
    {
        width = Math.Min(980, Game1.uiViewport.Width - 64);
        height = Math.Min(760, Game1.uiViewport.Height - 64);
        xPositionOnScreen = (Game1.uiViewport.Width - width) / 2;
        yPositionOnScreen = (Game1.uiViewport.Height - height) / 2;
        initializeUpperRightCloseButton();

        var x = xPositionOnScreen + Pad;
        var labelW = 190;
        var fx = x + labelW;
        var fw = width - Pad * 2 - labelW;
        var y = yPositionOnScreen + Pad + 80;

        claude.Bounds = new Rectangle(fx, y, (fw - 12) / 2, FieldH);
        codex.Bounds = new Rectangle(fx + (fw - 12) / 2 + 12, y, (fw - 12) / 2, FieldH);
        y += FieldH + 24;
        nameBox = new Rectangle(fx, y, fw, FieldH);
        y += FieldH + 24;
        folderBox = new Rectangle(fx, y, fw - 220, FieldH);
        browse.Bounds = new Rectangle(fx + fw - 208, y, 208, FieldH);
        y += FieldH + 12;
        var rx = fx;
        foreach (var r in recent)
        {
            r.Bounds = new Rectangle(rx, y, Math.Min(200, (fw - 36) / 4), 48);
            rx += r.Bounds.Width + 12;
        }
        if (recent.Count > 0)
            y += 48 + 24;
        else
            y += 12;
        perm.Bounds = new Rectangle(fx, y, fw, FieldH);
        y += FieldH + 24;
        modelBox = new Rectangle(fx, y, fw, FieldH);

        var by = yPositionOnScreen + height - Pad - FieldH;
        cancel.Bounds = new Rectangle(x, by, 220, FieldH);
        create.Bounds = new Rectangle(xPositionOnScreen + width - Pad - 320, by, 320, FieldH);
    }

    public override void gameWindowSizeChanged(Rectangle oldBounds, Rectangle newBounds)
    {
        base.gameWindowSizeChanged(oldBounds, newBounds);
        Layout();
    }

    private void FocusNext()
    {
        var i = Array.IndexOf(fields, focused);
        Focus(fields[(i + 1) % fields.Length]);
    }

    private void Focus(TextInput f)
    {
        focused.Blur();
        focused = f;
        f.Focus();
    }

    private void Browse()
    {
        if (picking)
            return;
        picking = true;
        Shell.PickFolderAsync().ContinueWith(t =>
        {
            picking = false;
            if (!string.IsNullOrWhiteSpace(t.Result))
                folder.Text = t.Result!;
        });
    }

    private void Create()
    {
        var n = name.Text.Trim();
        var dir = CliLocator.ExpandHome(folder.Text.Trim().Trim('"'));
        if (n.Length == 0)
        {
            error = I18n.Get("create.error.name");
            return;
        }
        if (dir.Length == 0 || !Directory.Exists(dir))
        {
            error = I18n.Get("create.error.folder");
            return;
        }

        var agent = new AgentRecord
        {
            Name = n,
            Kind = kind,
            Permission = permission,
            Model = model.Text.Trim(),
            WorkingDirectory = Path.GetFullPath(dir),
            ColorIndex = store.Agents.Count(a => a.Kind == kind)
        };
        store.RememberFolder(agent.WorkingDirectory);
        exitThisMenu();
        ModEntry.Instance.Placement.Begin(store, agent, isNew: true);
    }

    public override void update(GameTime time)
    {
        base.update(time);
        perm.Label = I18n.Get("create.permissions", new { mode = Widgets.PermissionLabel(permission) });
        browse.Label = picking ? "..." : I18n.Get("create.browse");
        var pasteDown = TextInput.IsCtrlDown() && Keyboard.GetState().IsKeyDown(Keys.V);
        if (pasteDown && !pasteWasDown)
            focused.Paste();
        pasteWasDown = pasteDown;

        if (Game1.keyboardDispatcher.Subscriber != focused)
            focused.Focus();
    }

    public override void receiveKeyPress(Keys key)
    {
        if (key == Keys.Escape)
            exitThisMenu();
    }

    public override void receiveLeftClick(int x, int y, bool playSound = true)
    {
        if (upperRightCloseButton != null && upperRightCloseButton.containsPoint(x, y))
        {
            exitThisMenu();
            return;
        }
        foreach (var btn in new[] { claude, codex, perm, browse, create, cancel }.Concat(recent))
            if (btn.TryClick(x, y))
                return;
        if (nameBox.Contains(x, y)) Focus(name);
        else if (folderBox.Contains(x, y)) Focus(folder);
        else if (modelBox.Contains(x, y)) Focus(model);
    }

    protected override void cleanupBeforeExit()
    {
        focused.Blur();
        base.cleanupBeforeExit();
    }

    private void DrawField(SpriteBatch b, Rectangle box, TextInput input, string placeholder)
    {
        IClickableMenu.drawTextureBox(b, Game1.mouseCursors, new Rectangle(384, 373, 18, 18), box.X, box.Y, box.Width, box.Height, input == focused ? Color.White : Color.White * 0.85f, 4f, false);
        var font = Game1.smallFont;
        var text = TextUtil.Sanitize(input.Text, font);
        var caret = input == focused && (int)(Game1.currentGameTime.TotalGameTime.TotalMilliseconds / 500) % 2 == 0 ? "|" : "";
        var pos = new Vector2(box.X + 16, box.Y + (box.Height - font.LineSpacing) / 2 + 2);
        if (text.Length == 0)
        {
            Widgets.Text(b, placeholder, pos + new Vector2(6, 0), Widgets.Muted);
            b.DrawString(font, caret, pos, Widgets.TextColor);
            return;
        }
        // Field too short: show the end of the text.
        var shown = text + caret;
        while (shown.Length > 1 && font.MeasureString(shown).X > box.Width - 32)
            shown = shown[1..];
        b.DrawString(font, shown, pos, Widgets.TextColor);
    }

    public override void draw(SpriteBatch b)
    {
        b.Draw(Game1.fadeToBlackRect, Game1.graphics.GraphicsDevice.Viewport.Bounds, Color.Black * 0.55f);
        Widgets.Panel(b, new Rectangle(xPositionOnScreen, yPositionOnScreen, width, height));
        var x = xPositionOnScreen + Pad;
        Widgets.Text(b, I18n.Get("create.title"), new Vector2(x, yPositionOnScreen + Pad), Widgets.TextColor, Game1.dialogueFont);

        void Label(string text, Rectangle row) => Widgets.Text(b, text, new Vector2(x, row.Y + 18), Widgets.TextColor);
        Label(I18n.Get("create.label.type"), claude.Bounds);
        Label(I18n.Get("create.label.name"), nameBox);
        Label(I18n.Get("create.label.folder"), folderBox);
        Label(I18n.Get("create.label.mode"), perm.Bounds);
        Label(I18n.Get("create.label.model"), modelBox);

        claude.Draw(b);
        codex.Draw(b);
        DrawField(b, nameBox, name, I18n.Get("create.placeholder.name"));
        DrawField(b, folderBox, folder, CliLocator.IsWindows ? @"C:\path\to\project" : "~/path/to/project");
        browse.Draw(b);
        foreach (var r in recent)
            r.Draw(b);
        perm.Draw(b);
        DrawField(b, modelBox, model, I18n.Get("create.placeholder.model"));

        if (permission == PermissionLevel.Yolo)
            Widgets.Text(b, I18n.Get("create.yolo-warning"), new Vector2(perm.Bounds.X, perm.Bounds.Bottom + 2), Widgets.Muted, scale: 0.8f);
        if (error.Length > 0)
            Widgets.Text(b, error, new Vector2(cancel.Bounds.Right + 24, cancel.Bounds.Y + 18), Widgets.ErrorColor);

        cancel.Draw(b);
        create.Draw(b);
        base.draw(b);
        drawMouse(b);
    }
}
