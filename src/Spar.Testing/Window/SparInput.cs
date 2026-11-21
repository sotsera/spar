using Spar.Hosting;

namespace Spar.Testing;

/// <summary>
/// Trusted keyboard input into the page (decision "Window-test profiles, trusted input, the page's pickers and the
/// Sandbox runner": the DevTools Protocol's <c>Input</c> domain on Windows; decision "Trusted input in Linux desktop
/// tests: XTEST" on Linux, under the test process's own Xvfb). The events reach the page as a person's
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
    /// The modifiers go down first and up last. Any one character is a key, also one the US keyboard has no key for
    /// (<c>"è"</c>, <c>"€"</c>, an emoji): the page gets its <c>keydown</c> and <c>keyup</c> with <c>event.key</c> the
    /// character and an empty <c>event.code</c> (<c>"Unidentified"</c> on Linux, as WebKit names a key with no physical code).
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
    /// Types <paramref name="text"/> one character at a time, each with its key events, as a person typing. The text is
    /// split into Unicode scalar values, never UTF-16 units, so an emoji or 𝄞 is one key, and a combining sequence one
    /// key per code point (the value the page ends with is the text). A character the US keyboard has no key for gets
    /// <c>keydown</c> and <c>keyup</c> with <c>event.key</c> the character and an empty <c>event.code</c> (<c>"Unidentified"</c> on Linux).
    /// </summary>
    public async Task TypeAsync(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        await _session.BeforeInputAsync(TimeSpan.FromSeconds(5));
        foreach (var rune in text.EnumerateRunes())
        {
            if (KeyboardLayout.ForCharacter(rune) is not { } typed)
            {
                var key = KeyboardLayout.Unmapped(rune);
                await DownAsync(key, KeyboardLayout.Modifiers.None);
                await UpAsync(key, KeyboardLayout.Modifiers.None);
                continue;
            }

            var shift = typed.Shift ? KeyboardLayout.Modifiers.Shift : KeyboardLayout.Modifiers.None;
            await DownAsync(typed.Key, shift);
            await UpAsync(typed.Key, shift);
        }
    }


    /// <summary>Inserts text where the focus is, without key events, as an input method does (<c>input</c> events only).</summary>
    public Task InsertTextAsync(string text) => _session.Window.InsertTextAsync(text);

    private Task DownAsync(KeyboardLayout.Key key, KeyboardLayout.Modifiers modifiers)
    {
        // A combination with Control, Alt or Meta types nothing; the page sees the key and acts on the shortcut.
        var text = (modifiers & ~KeyboardLayout.Modifiers.Shift) == 0 ? key.Text : null;
        return _session.Window.KeyAsync(new SparTestKey(Down: true, key.Name, key.Code, key.VirtualKey, (int)modifiers, text));
    }

    private Task UpAsync(KeyboardLayout.Key key, KeyboardLayout.Modifiers modifiers) =>
        _session.Window.KeyAsync(new SparTestKey(Down: false, key.Name, key.Code, key.VirtualKey, (int)modifiers, null));
}

/// <summary>Trusted mouse input into the page, in CSS pixels of its viewport (see <see cref="SparKeyboard"/>).</summary>
public sealed class SparMouse
{
    private readonly PageSession _session;

    internal SparMouse(PageSession session)
    {
        _session = session;
    }

    public Task MoveAsync(float x, float y) => _session.Window.PointerAsync(new SparTestPointer(SparTestPointerKind.Move, x, y));

    /// <summary>Moves to the point, then presses and releases the left button <paramref name="clickCount"/> times.</summary>
    public async Task ClickAsync(float x, float y, int clickCount = 1)
    {
        await _session.BeforeInputAsync(TimeSpan.FromSeconds(5));
        await MoveAsync(x, y);
        for (var click = 1; click <= clickCount; click++)
        {
            await _session.Window.PointerAsync(new SparTestPointer(SparTestPointerKind.Press, x, y, click));
            await _session.Window.PointerAsync(new SparTestPointer(SparTestPointerKind.Release, x, y, click));
        }
    }

    /// <summary>Scrolls by the wheel at the point, in CSS pixels.</summary>
    public Task WheelAsync(float x, float y, float deltaX, float deltaY) =>
        _session.Window.PointerAsync(new SparTestPointer(SparTestPointerKind.Wheel, x, y, DeltaX: deltaX, DeltaY: deltaY));
}
