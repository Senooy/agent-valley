using System;
using System.Text;
using Microsoft.Xna.Framework.Input;
using StardewValley;

namespace AgentValley.UI;

/// <summary>Multi-line text field plugged into the game's KeyboardDispatcher (accents and IME included).</summary>
public sealed class TextInput : IKeyboardSubscriber
{
    private readonly StringBuilder text = new();
    private int pasteGuardTick = -1;

    public bool Multiline { get; init; }
    public int MaxLength { get; init; } = 8000;
    public Action? OnSubmit { get; set; }
    public Action? OnTab { get; set; }

    public bool Selected { get; set; }

    public string Text
    {
        get => text.ToString();
        set
        {
            text.Clear().Append(value);
        }
    }

    public void Focus()
    {
        Game1.keyboardDispatcher.Subscriber = this;
        Selected = true;
    }

    public void Blur()
    {
        if (Game1.keyboardDispatcher.Subscriber == this)
            Game1.keyboardDispatcher.Subscriber = null;
        Selected = false;
    }

    public void RecieveTextInput(char inputChar)
    {
        if (!Selected || char.IsControl(inputChar))
            return;
        Append(inputChar.ToString());
    }

    public void RecieveTextInput(string input)
    {
        if (!Selected || string.IsNullOrEmpty(input))
            return;
        foreach (var c in input)
            RecieveTextInput(c);
    }

    public void RecieveCommandInput(char command)
    {
        if (!Selected)
            return;
        switch (command)
        {
            case '\b':
                if (text.Length > 0)
                {
                    // Ctrl+Backspace: delete the whole word.
                    if (IsCtrlDown())
                    {
                        var end = text.Length;
                        var i = end - 1;
                        while (i > 0 && text[i - 1] != ' ' && text[i - 1] != '\n')
                            i--;
                        text.Remove(i, end - i);
                    }
                    else
                        text.Remove(text.Length - 1, 1);
                }
                break;
            case '\r':
                var state = Keyboard.GetState();
                if (Multiline && (state.IsKeyDown(Keys.LeftShift) || state.IsKeyDown(Keys.RightShift)))
                    Append("\n");
                else
                    OnSubmit?.Invoke();
                break;
            case '\t':
                OnTab?.Invoke();
                break;
            case '\u0016':
                Paste();
                break;
        }
    }

    public void RecieveSpecialInput(Keys key) { }

    /// <summary>Call on Ctrl/Cmd+V (the dispatcher doesn't always forward pastes).</summary>
    public void Paste()
    {
        if (!Selected || pasteGuardTick == Game1.ticks)
            return;
        pasteGuardTick = Game1.ticks;
        string clip = "";
        if (DesktopClipboard.GetText(ref clip) && !string.IsNullOrEmpty(clip))
            Append(Multiline ? clip.Replace("\r\n", "\n") : clip.Replace("\r", "").Replace("\n", " "));
    }

    public static bool IsCtrlDown()
    {
        var s = Keyboard.GetState();
        return s.IsKeyDown(Keys.LeftControl) || s.IsKeyDown(Keys.RightControl) || s.IsKeyDown(Keys.LeftWindows) || s.IsKeyDown(Keys.RightWindows);
    }

    private void Append(string s)
    {
        var room = MaxLength - text.Length;
        if (room <= 0)
            return;
        text.Append(s.Length > room ? s[..room] : s);
    }
}
