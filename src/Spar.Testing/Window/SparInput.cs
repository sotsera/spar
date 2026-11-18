using System.Text.Json;

namespace Spar.Testing;

/// <summary>
/// Trusted keyboard input into the page (decision "Window-test profiles, trusted input, the page's pickers and the
/// Sandbox runner": the DevTools Protocol's <c>Input</c> domain on Windows). The events reach the page as a person's
/// would, <c>isTrusted</c> and with user activation, but never the browser's own shortcuts (F5 does not reload) and
/// never the desktop: the window is never activated.
/// </summary>
public sealed class SparKeyboard
{
    private static readonly (KeyboardLayout.Modifiers Flag, string Name)[] ModifierKeys =
    [
        (KeyboardLayout.Modifiers.Control, "Control"),
        (KeyboardLayout.Modifiers.Alt, "Alt"),
        (KeyboardLayout.Modifiers.Meta, "Meta"),
        (KeyboardLayout.Modifiers.Shift, "Shift"),
    ];

    private readonly PageSession _session;

    internal SparKeyboard(PageSession session)
    {
        _session = session;
    }

    /// <summary>
    /// Presses and releases a key or a combination: <c>"Enter"</c>, <c>"a"</c>, <c>"Control+End"</c>, <c>"Shift+Tab"</c>.
    /// The modifiers go down first and up last.
    /// </summary>
    public async Task PressAsync(string key)
    {
        var (modifiers, pressed) = KeyboardLayout.Parse(key);
        await _session.BeforeInputAsync(TimeSpan.FromSeconds(5));
        var held = KeyboardLayout.Modifiers.None;
        foreach (var (flag, name) in ModifierKeys)
        {
            if (modifiers.HasFlag(flag))
            {
                held |= flag;
                await DownAsync(KeyboardLayout.Parse(name).Key, held);
            }
        }

        await DownAsync(pressed, held);
        await UpAsync(pressed, held);
        foreach (var (flag, name) in ModifierKeys.Reverse())
        {
            if (modifiers.HasFlag(flag))
            {
                held &= ~flag;
                await UpAsync(KeyboardLayout.Parse(name).Key, held);
            }
        }
    }

    /// <summary>
    /// Types <paramref name="text"/> one key at a time, each with its key events, as a person typing; a character the US
    /// keyboard has no key for is inserted as text.
    /// </summary>
    public async Task TypeAsync(string text)
    {
        await _session.BeforeInputAsync(TimeSpan.FromSeconds(5));
        foreach (var character in text)
        {
            if (KeyboardLayout.ForCharacter(character) is not { } typed)
            {
                await InsertTextAsync(character.ToString());
                continue;
            }

            var shift = typed.Shift ? KeyboardLayout.Modifiers.Shift : KeyboardLayout.Modifiers.None;
            await DownAsync(typed.Key, shift);
            await UpAsync(typed.Key, shift);
        }
    }

    /// <summary>Inserts text where the focus is, without key events, as an input method does (<c>input</c> events only).</summary>
    public Task InsertTextAsync(string text) =>
        _session.CallAsync("Input.insertText", writer => writer.WriteString("text", text));

    private Task DownAsync(KeyboardLayout.Key key, KeyboardLayout.Modifiers modifiers)
    {
        // A combination with Control, Alt or Meta types nothing; the page sees the key and acts on the shortcut.
        var text = (modifiers & ~KeyboardLayout.Modifiers.Shift) == 0 ? key.Text : null;
        return Dispatch(text is null ? "rawKeyDown" : "keyDown", key, modifiers, text);
    }

    private Task UpAsync(KeyboardLayout.Key key, KeyboardLayout.Modifiers modifiers) => Dispatch("keyUp", key, modifiers, null);

    private Task Dispatch(string type, KeyboardLayout.Key key, KeyboardLayout.Modifiers modifiers, string? text) =>
        _session.CallAsync("Input.dispatchKeyEvent", writer =>
        {
            writer.WriteString("type", type);
            writer.WriteNumber("modifiers", (int)modifiers);
            writer.WriteString("key", key.Name);
            writer.WriteString("code", key.Code);
            writer.WriteNumber("windowsVirtualKeyCode", key.VirtualKey);
            if (text is not null)
            {
                writer.WriteString("text", text);
                writer.WriteString("unmodifiedText", text);
            }
        });
}

/// <summary>Trusted mouse input into the page, in CSS pixels of its viewport (see <see cref="SparKeyboard"/>).</summary>
public sealed class SparMouse
{
    private readonly PageSession _session;

    internal SparMouse(PageSession session)
    {
        _session = session;
    }

    public Task MoveAsync(float x, float y) => DispatchAsync("mouseMoved", x, y, buttons: 0, clickCount: 0);

    /// <summary>Moves to the point, then presses and releases the left button <paramref name="clickCount"/> times.</summary>
    public async Task ClickAsync(float x, float y, int clickCount = 1)
    {
        await _session.BeforeInputAsync(TimeSpan.FromSeconds(5));
        await MoveAsync(x, y);
        for (var click = 1; click <= clickCount; click++)
        {
            await DispatchAsync("mousePressed", x, y, buttons: 1, click);
            await DispatchAsync("mouseReleased", x, y, buttons: 0, click);
        }
    }

    /// <summary>Scrolls by the wheel at the point, in CSS pixels.</summary>
    public Task WheelAsync(float x, float y, float deltaX, float deltaY) =>
        _session.CallAsync("Input.dispatchMouseEvent", writer =>
        {
            writer.WriteString("type", "mouseWheel");
            writer.WriteNumber("x", x);
            writer.WriteNumber("y", y);
            writer.WriteNumber("deltaX", deltaX);
            writer.WriteNumber("deltaY", deltaY);
        });

    private Task<JsonElement> DispatchAsync(string type, float x, float y, int buttons, int clickCount) =>
        _session.CallAsync("Input.dispatchMouseEvent", writer =>
        {
            writer.WriteString("type", type);
            writer.WriteNumber("x", x);
            writer.WriteNumber("y", y);
            if (clickCount > 0)
            {
                writer.WriteString("button", "left");
                writer.WriteNumber("buttons", buttons);
                writer.WriteNumber("clickCount", clickCount);
            }
        });
}
