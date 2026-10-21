using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Menus;
using StardewValley.Mods;
using AgentValley.UI;
using AgentValley.World;

namespace AgentValley;

public sealed class ModEntry : Mod
{
    public static ModEntry Instance { get; private set; } = null!;

    public ModConfig Config { get; private set; } = null!;
    public AgentManager Manager { get; private set; } = null!;
    public Placement Placement { get; private set; } = null!;
    public CallOverlay Calls { get; } = new();
    public SidePanel Sidebar { get; } = new();

    /// <summary>Unsent messages, per agent (restored when you come back to its chat).</summary>
    public Dictionary<string, string> Drafts { get; } = new();

    /// <summary>Agents of the loaded save. Other saves stay in memory so their running turns can finish.</summary>
    public AgentStore? Store { get; private set; }

    private readonly Dictionary<string, AgentStore> stores = new();

    public override void Entry(IModHelper helper)
    {
        Instance = this;
        I18n.Init(helper.Translation);
        Config = helper.ReadConfig<ModConfig>();
        Manager = new AgentManager(Monitor, () => Config, helper.DirectoryPath);
        Manager.TurnFinished += OnTurnFinished;
        Manager.TurnFinishedBackground += OnTurnFinishedBackground;
        Placement = new Placement(helper);

        helper.Events.GameLoop.GameLaunched += (_, _) => Integrations.ConfigMenu.Register(this);
        helper.Events.GameLoop.SaveLoaded += OnSaveLoaded;
        helper.Events.GameLoop.ReturnedToTitle += OnReturnedToTitle;
        helper.Events.GameLoop.Saving += (_, _) => Store?.Flush(force: true);
        helper.Events.GameLoop.UpdateTicked += OnUpdateTicked;
        helper.Events.Input.ButtonsChanged += OnButtonsChanged;
        helper.Events.Input.ButtonPressed += OnButtonPressed;
        helper.Events.Display.RenderedStep += OnRenderedStep;
        helper.Events.Display.RenderedHud += OnRenderedHud;

        AppDomain.CurrentDomain.ProcessExit += (_, _) => Shutdown();

        helper.ConsoleCommands.Add("agents", "Lists the Agent Valley agents of the loaded save.", (_, _) => ListAgents());
        helper.ConsoleCommands.Add("agents_cli", "Detects the claude / codex CLIs again.", (_, _) => DetectClis());

        // On macOS, reading the shell PATH takes a second or two: do it right away, in the background.
        Task.Run(DetectClis);
    }

    private void DetectClis()
    {
        CliLocator.ClearCache();
        var claude = CliLocator.Find("claude", Config.ClaudePath);
        var codex = CliLocator.Find("codex", Config.CodexPath);
        Monitor.Log($"Claude Code: {claude ?? "not found"}", claude == null ? LogLevel.Warn : LogLevel.Info);
        Monitor.Log($"Codex: {codex ?? "not found"}", codex == null ? LogLevel.Warn : LogLevel.Info);
    }

    /// <summary>Restores the default settings (Generic Mod Config Menu's "Reset" button).</summary>
    public void ResetConfig() => Config = new ModConfig();

    /// <summary>Saves config.json, then looks for the CLIs again in case their paths changed.</summary>
    public void SaveConfig()
    {
        Helper.WriteConfig(Config);
        Task.Run(DetectClis);
    }

    private void Shutdown()
    {
        Manager.KillAll();
        foreach (var s in stores.Values)
        {
            try { s.Flush(force: true); }
            catch { }
        }
    }

    // ---------------------------------------------------------------- lifecycle

    private void OnSaveLoaded(object? sender, SaveLoadedEventArgs e)
    {
        var key = Constants.SaveFolderName ?? "default";
        if (!stores.TryGetValue(key, out var store))
            stores[key] = store = AgentStore.Load(Helper, key);
        Store = store;
        if (store.Agents.Count > 0)
            Monitor.Log($"Loaded {store.Agents.Count} agent(s).", LogLevel.Info);
    }

    private void OnReturnedToTitle(object? sender, ReturnedToTitleEventArgs e)
    {
        Placement.Cancel();
        Store?.Flush(force: true);
        Store = null;
    }

    private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
    {
        Manager.Update();
        foreach (var s in stores.Values)
            s.Flush();
        Calls.Update();
    }

    private void OnTurnFinished(AgentStore store, AgentRecord agent, bool error)
    {
        if (store != Store || !Context.IsWorldReady)
            return;

        if (Game1.activeClickableMenu is ChatMenu chat && chat.Agent == agent)
        {
            agent.Unread = false;
            if (Config.PlaySounds)
                Game1.playSound(error ? "cancel" : "newArtifact");
            return;
        }

        agent.Unread = true;
        store.MarkDirty();
        var away = Game1.currentLocation?.NameOrUniqueName != agent.LocationName;
        Calls.Push(agent, error, away);
        if (Config.PlaySounds)
            Game1.playSound(error ? "cancel" : away ? "phone" : "newArtifact");
    }

    private void OnTurnFinishedBackground(string name, string text, bool error)
    {
        if (!Config.DesktopNotifications || Game1.game1 == null || Game1.game1.IsActive)
            return;
        Shell.Notify(I18n.Get(error ? "call.problem" : "call.done", new { name }), Backends.OneLine(text, 180));
    }

    // ---------------------------------------------------------------- input

    private static bool IsTyping => Game1.activeClickableMenu is ChatMenu or CreateAgentMenu;

    private void OnButtonsChanged(object? sender, ButtonsChangedEventArgs e)
    {
        if (Store == null || !Context.IsWorldReady || IsTyping || Placement.Active)
            return;

        if (Config.OpenAgentList.JustPressed())
        {
            Helper.Input.SuppressActiveKeybinds(Config.OpenAgentList);
            if (Game1.activeClickableMenu is AgentListMenu list)
                list.exitThisMenu();
            else if (Context.IsPlayerFree)
                OpenList();
        }
        else if (Config.NewAgent.JustPressed() && Context.IsPlayerFree)
        {
            Helper.Input.SuppressActiveKeybinds(Config.NewAgent);
            OpenCreateMenu();
        }
        else if (Config.ToggleSidebar.JustPressed() && Context.IsPlayerFree)
        {
            Helper.Input.SuppressActiveKeybinds(Config.ToggleSidebar);
            Sidebar.Toggle(Store);
        }
        else if (Config.AnswerLastCall.JustPressed() && Context.IsPlayerFree)
        {
            Helper.Input.SuppressActiveKeybinds(Config.AnswerLastCall);
            var caller = Calls.LastCaller ?? Store.Agents.Where(a => a.Unread).OrderByDescending(a => a.LastActivity).FirstOrDefault();
            if (caller != null && Store.Agents.Contains(caller))
                OpenChat(caller);
            else
                Game1.addHUDMessage(new HUDMessage(I18n.Get("hud.no-caller")));
        }
    }

    private void OnButtonPressed(object? sender, ButtonPressedEventArgs e)
    {
        if (Store == null || !Context.IsWorldReady)
            return;

        if (Placement.Active)
        {
            if (Context.IsPlayerFree)
                Placement.HandleButton(e);
            return;
        }

        if (!Context.IsPlayerFree)
            return;

        if (e.Button == SButton.MouseLeft)
        {
            var (ux, uy) = (Game1.getMouseX(true), Game1.getMouseY(true));
            if (Game1.displayHUD && Sidebar.Click(Store, ux, uy))
            {
                Helper.Input.Suppress(e.Button);
                return;
            }
            var called = Calls.HitTest(ux, uy);
            if (called != null)
            {
                Helper.Input.Suppress(e.Button);
                OpenChat(called);
                return;
            }
        }

        if (e.Button.IsActionButton() && !Sidebar.Contains(Store, Game1.getMouseX(true), Game1.getMouseY(true)))
        {
            var agent = AgentAt(e.Cursor.Tile) ?? AgentAt(e.Cursor.GrabTile);
            if (agent != null)
            {
                Helper.Input.Suppress(e.Button);
                Game1.playSound("junimoMeep1");
                OpenChat(agent);
            }
        }
    }

    // ---------------------------------------------------------------- rendering

    private AgentRecord? AgentAt(Vector2 tile)
    {
        var loc = Game1.currentLocation?.NameOrUniqueName;
        return Store?.Agents.FirstOrDefault(a => a.LocationName == loc && a.TileX == (int)tile.X && a.TileY == (int)tile.Y);
    }

    private void OnRenderedStep(object? sender, RenderedStepEventArgs e)
    {
        if (e.Step != RenderSteps.World_Sorted || Store == null || Game1.currentLocation == null)
            return;

        var loc = Game1.currentLocation.NameOrUniqueName;
        var hovered = Game1.activeClickableMenu == null ? AgentAt(Helper.Input.GetCursorPosition().Tile) : null;
        foreach (var agent in Store.Agents)
        {
            if (agent.LocationName != loc)
                continue;
            var world = new Vector2(agent.TileX * 64, agent.TileY * 64);
            var local = Game1.GlobalToLocal(Game1.viewport, world);
            var depth = Math.Clamp((world.Y + 64) / 10000f + world.X / 10000000f, 0f, 1f);
            AgentVisuals.DrawInWorld(e.SpriteBatch, agent, local, depth, 1f, Config.AlwaysShowNames || agent == hovered);
        }
        Placement.DrawWorld(e.SpriteBatch);
    }

    private void OnRenderedHud(object? sender, RenderedHudEventArgs e)
    {
        if (Store == null)
            return;
        if (!Game1.eventUp)
            Sidebar.Draw(e.SpriteBatch, Store);
        Calls.Draw(e.SpriteBatch);
        Placement.DrawHud(e.SpriteBatch);

        if (Placement.Active || !Context.IsPlayerFree || Sidebar.Contains(Store, Game1.getMouseX(true), Game1.getMouseY(true)))
            return;
        var agent = AgentAt(Helper.Input.GetCursorPosition().Tile);
        if (agent == null)
            return;
        var font = Game1.smallFont;
        var body = $"{Widgets.KindLabel(agent.Kind)} - {Widgets.StatusLabel(agent)}\n{I18n.Get("hover.talk")}";
        IClickableMenu.drawHoverText(e.SpriteBatch, TextUtil.Sanitize(body, font), font, boldTitleText: TextUtil.Sanitize(agent.Name, font));
    }

    // ---------------------------------------------------------------- actions

    public void OpenChat(AgentRecord agent, AgentStore? store = null, bool docked = false)
    {
        store ??= Store;
        if (store == null)
            return;
        Calls.Dismiss(agent);
        Game1.activeClickableMenu = new ChatMenu(store, agent, docked);
    }

    public void OpenList()
    {
        if (Store != null)
            Game1.activeClickableMenu = new AgentListMenu(Store);
    }

    public void OpenCreateMenu()
    {
        if (Store != null)
            Game1.activeClickableMenu = new CreateAgentMenu(Store, Config);
    }

    public void WarpTo(AgentRecord agent)
    {
        var loc = Game1.getLocationFromName(agent.LocationName);
        if (loc == null)
        {
            Game1.addHUDMessage(new HUDMessage(I18n.Get("hud.location-not-found", new { location = agent.LocationName }), HUDMessage.error_type));
            return;
        }
        if (Game1.eventUp || Game1.isFestival())
        {
            Game1.addHUDMessage(new HUDMessage(I18n.Get("hud.not-now"), HUDMessage.error_type));
            return;
        }

        // A free tile next to the agent, preferably below it (so you arrive facing it).
        var target = new Point(agent.TileX, agent.TileY + 1);
        var facing = 0;
        foreach (var (dx, dy, face) in new[] { (0, 1, 0), (1, 0, 3), (-1, 0, 1), (0, -1, 2) })
        {
            var t = new Vector2(agent.TileX + dx, agent.TileY + dy);
            if (loc.isTileOnMap(t) && loc.isTilePassable(t))
            {
                target = new Point((int)t.X, (int)t.Y);
                facing = face;
                break;
            }
        }
        Game1.warpFarmer(Game1.getLocationRequest(loc.NameOrUniqueName, loc.isStructure.Value), target.X, target.Y, facing);
    }

    private void ListAgents()
    {
        if (Store == null)
        {
            Monitor.Log("No save loaded.", LogLevel.Info);
            return;
        }
        foreach (var a in Store.Agents)
            Monitor.Log($"{a.Name} [{a.Kind}] {a.Status} @ {a.LocationName} ({a.TileX},{a.TileY}) - {a.WorkingDirectory} - session {a.SessionId}", LogLevel.Info);
    }
}
