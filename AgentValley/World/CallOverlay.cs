using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;
using StardewValley.Menus;
using AgentValley.UI;

namespace AgentValley.World;

/// <summary>"Calls": clickable cards at the top right when an agent finishes or hits a problem.</summary>
public sealed class CallOverlay
{
    private const int CardW = 460;
    private const int CardH = 112;
    private static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(20);

    private sealed record Call(AgentRecord Agent, string Title, string Text, bool Error, DateTime Since);

    private readonly List<Call> calls = new();

    public AgentRecord? LastCaller { get; private set; }

    public void Push(AgentRecord agent, bool error, bool away)
    {
        calls.RemoveAll(c => c.Agent == agent);
        var title = I18n.Get(error ? "call.problem" : away ? "call.calling" : "call.done", new { name = agent.Name });
        var text = Backends.OneLine(TextUtil.StripMarkdown(agent.LastPreview), 160);
        if (!error && text.Length == 0)
            text = I18n.Get("call.default-text");
        calls.Insert(0, new Call(agent, title, text, error, DateTime.Now));
        if (calls.Count > 4)
            calls.RemoveAt(calls.Count - 1);
        LastCaller = agent;
    }

    public void Dismiss(AgentRecord agent) => calls.RemoveAll(c => c.Agent == agent);

    public void Update() => calls.RemoveAll(c => DateTime.Now - c.Since > Lifetime || !c.Agent.Unread);

    private Rectangle CardBounds(int index)
    {
        // Below the game clock.
        var x = Game1.uiViewport.Width - CardW - 24;
        var y = 300 + index * (CardH + 12);
        return new Rectangle(x, y, CardW, CardH);
    }

    /// <summary>UI coordinates. Returns the clicked agent, or null.</summary>
    public AgentRecord? HitTest(int x, int y)
    {
        for (var i = 0; i < calls.Count; i++)
            if (CardBounds(i).Contains(x, y))
                return calls[i].Agent;
        return null;
    }

    public void Draw(SpriteBatch b)
    {
        var font = Game1.smallFont;
        for (var i = 0; i < calls.Count; i++)
        {
            var c = calls[i];
            var r = CardBounds(i);
            // A little shake during the first second, like a phone.
            var age = (DateTime.Now - c.Since).TotalMilliseconds;
            if (age < 1000)
                r.X += (int)(Math.Sin(age / 25.0) * 4);

            IClickableMenu.drawTextureBox(b, Game1.menuTexture, new Rectangle(0, 256, 60, 60), r.X, r.Y, r.Width, r.Height, Color.White, 1f, true);
            AgentVisuals.DrawIcon(b, c.Agent, new Vector2(r.X + 20, r.Y + 28), 3.5f);
            var tx = r.X + 96;
            var w = r.Width - 96 - 20;
            Widgets.Text(b, TextUtil.Ellipsize(TextUtil.Sanitize(c.Title, font), font, w), new Vector2(tx, r.Y + 18), c.Error ? Widgets.ErrorColor : Widgets.TextColor);
            var lines = TextUtil.Wrap(TextUtil.Sanitize(c.Text, font), font, w).Take(2).ToList();
            for (var l = 0; l < lines.Count; l++)
            {
                var line = l == 1 && TextUtil.Wrap(TextUtil.Sanitize(c.Text, font), font, w).Count > 2 ? TextUtil.Ellipsize(lines[l] + " ...", font, w) : lines[l];
                b.DrawString(font, line, new Vector2(tx, r.Y + 50 + l * 26), Widgets.Muted, 0f, Vector2.Zero, 0.85f, SpriteEffects.None, 1f);
            }
        }
    }
}
