using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Locations;
using AgentValley.UI;

namespace AgentValley.World;

/// <summary>Placement mode: a ghost agent follows the cursor, left click to put it down.</summary>
public sealed class Placement
{
    private readonly IModHelper helper;
    private AgentStore? store;
    private AgentRecord? agent;
    private bool isNew;

    public Placement(IModHelper helper) => this.helper = helper;

    public bool Active => agent != null;

    public void Begin(AgentStore store, AgentRecord agent, bool isNew)
    {
        this.store = store;
        this.agent = agent;
        this.isNew = isNew;
        Game1.playSound("dwop");
    }

    public void Cancel()
    {
        if (agent == null)
            return;
        if (isNew)
            Game1.addHUDMessage(new HUDMessage(I18n.Get("place.cancelled")));
        agent = null;
        store = null;
    }

    private Vector2 TargetTile()
    {
        var cursor = helper.Input.GetCursorPosition();
        return Game1.options.gamepadControls ? cursor.GrabTile : cursor.Tile;
    }

    /// <summary>Returns true if the input was consumed.</summary>
    public bool HandleButton(ButtonPressedEventArgs e)
    {
        if (agent == null || store == null)
            return false;

        if (e.Button is SButton.MouseRight or SButton.Escape or SButton.ControllerB)
        {
            helper.Input.Suppress(e.Button);
            Cancel();
            return true;
        }

        if (e.Button is SButton.MouseLeft or SButton.ControllerA || e.Button.IsUseToolButton())
        {
            helper.Input.Suppress(e.Button);
            var location = Game1.currentLocation;
            var tile = TargetTile();
            if (location == null || location is MineShaft or VolcanoDungeon || !location.isTileOnMap(tile))
            {
                Game1.playSound("cancel");
                Game1.addHUDMessage(new HUDMessage(I18n.Get("place.invalid"), HUDMessage.error_type));
                return true;
            }

            agent.LocationName = location.NameOrUniqueName;
            agent.TileX = (int)tile.X;
            agent.TileY = (int)tile.Y;
            if (isNew && !store.Agents.Contains(agent))
            {
                store.Agents.Add(agent);
                agent.Add(EntryRole.System, I18n.Get("place.settled", new { name = agent.Name, path = agent.WorkingDirectory }));
            }
            store.MarkDirty();
            store.Flush(force: true);
            Game1.playSound("axchop");

            var placed = agent;
            var placedStore = store;
            agent = null;
            store = null;
            if (isNew)
                ModEntry.Instance.OpenChat(placed, placedStore);
            return true;
        }
        return false;
    }

    public void DrawWorld(SpriteBatch b)
    {
        if (agent == null)
            return;
        var tile = TargetTile();
        var local = Game1.GlobalToLocal(Game1.viewport, tile * 64f);
        AgentVisuals.DrawInWorld(b, agent, local, 1f, 0.55f, showName: true);
    }

    public void DrawHud(SpriteBatch b)
    {
        if (agent == null)
            return;
        var text = TextUtil.Sanitize(I18n.Get("place.hint", new { name = agent.Name }), Game1.smallFont);
        var size = Game1.smallFont.MeasureString(text);
        var box = new Rectangle((int)(Game1.uiViewport.Width - size.X) / 2 - 24, 16, (int)size.X + 48, (int)size.Y + 32);
        StardewValley.Menus.IClickableMenu.drawTextureBox(b, Game1.menuTexture, new Rectangle(0, 256, 60, 60), box.X, box.Y, box.Width, box.Height, Color.White, 1f, false);
        b.DrawString(Game1.smallFont, text, new Vector2(box.X + 24, box.Y + 18), Widgets.TextColor);
    }
}
