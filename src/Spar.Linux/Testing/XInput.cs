using System.Collections.Frozen;
using Spar.Hosting;
using static Spar.Linux.Testing.TestNative;

namespace Spar.Linux.Testing;

/// <summary>
/// Trusted input for Linux test windows (decision "Trusted input in Linux desktop tests: XTEST"): key and pointer events
/// through the XTEST extension on the test process's own connection to its private display, so they reach GTK and the
/// page as a person's would (<c>isTrusted</c>, with user activation). One display has one pointer, one keyboard focus
/// and one set of held keys, so every event of every window of the process goes through one lock: before it, a key's
/// window gets the input focus and a pointer's window is raised, so parallel tests never type or click into each
/// other's windows. A character the keymap has no key for is bound to a spare keycode first, and GTK is given two turns
/// of its loop to read the new mapping before the key is pressed (<c>docs/evidence/window-automation-linux</c>: binding and
/// typing at once typed the wrong character).
/// </summary>
internal sealed class XInput
{
    private const uint ShiftKeysym = 0xffe1;

    private static readonly FrozenDictionary<string, uint> NamedKeysyms = new Dictionary<string, uint>(StringComparer.Ordinal)
    {
        ["Enter"] = 0xff0d, ["Tab"] = 0xff09, ["Escape"] = 0xff1b, ["Backspace"] = 0xff08, ["Delete"] = 0xffff,
        ["Insert"] = 0xff63, ["Home"] = 0xff50, ["End"] = 0xff57, ["PageUp"] = 0xff55, ["PageDown"] = 0xff56,
        ["ArrowLeft"] = 0xff51, ["ArrowUp"] = 0xff52, ["ArrowRight"] = 0xff53, ["ArrowDown"] = 0xff54,
        ["Shift"] = ShiftKeysym, ["Control"] = 0xffe3, ["Alt"] = 0xffe9, ["Meta"] = 0xffeb,
    }.ToFrozenDictionary(StringComparer.Ordinal);

    private static readonly FrozenDictionary<string, char> PunctuationCodes = new Dictionary<string, char>(StringComparer.Ordinal)
    {
        ["Backquote"] = '`', ["Minus"] = '-', ["Equal"] = '=', ["BracketLeft"] = '[', ["BracketRight"] = ']',
        ["Backslash"] = '\\', ["Semicolon"] = ';', ["Quote"] = '\'', ["Comma"] = ',', ["Period"] = '.', ["Slash"] = '/',
        ["Space"] = ' ',
    }.ToFrozenDictionary(StringComparer.Ordinal);

    private readonly nint _display;
    private readonly Func<Task> _gtkTurn;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly List<byte> _spare = [];
    private readonly List<byte> _otherSpare = [];
    private readonly SemaphoreSlim _spareStroke = new(1, 1);
    private readonly HashSet<string> _spareKeys = new(StringComparer.Ordinal);
    private readonly Dictionary<uint, byte> _bound = [];
    private readonly HashSet<uint> _held = [];
    private readonly Dictionary<string, byte> _pressed = new(StringComparer.Ordinal);

    /// <summary>
    /// The display's input, held by one window from its first key or button down to its last key or button up; other
    /// windows' keys, pointer events and focus changes wait meanwhile. One display has one keyboard state and one pointer:
    /// another window's Control+End pressed meanwhile made this window's Control a press of a key already down, which
    /// reached no page, and its release ended this window's Control before its End (measured in the CI container: End
    /// alone moved the caret; pitfalls). A press and its release are held the same way, and a click between a stroke's
    /// keys would have taken its modifiers.
    /// </summary>
    private readonly SemaphoreSlim _input = new(1, 1);
    private readonly Lock _inputGate = new();
    private nuint _inputOwner;
    private int _inputDown;
    private int _nextSpare;
    private (int X, int Y) _pointer = (-1, -1);
    private nuint _pointerWindow;

    private XInput(nint display, Func<Task> gtkTurn)
    {
        _display = display;
        _gtkTurn = gtkTurn;
    }

    /// <summary>
    /// A connection of the test process's own to <paramref name="displayName"/>, with XTEST; a problem when either is
    /// missing. <paramref name="gtkTurn"/> completes after one turn of GTK's loop.
    /// </summary>
    public static unsafe XInput Open(string displayName, Func<Task> gtkTurn)
    {
        var display = XOpenDisplay(displayName);
        if (display == 0)
        {
            throw new SparTestWindowUnavailableException($"Window tests cannot run here: the test's display {displayName} could not be opened for input.");
        }

        int eventBase, errorBase, major, minor;
        if (XTestQueryExtension(display, &eventBase, &errorBase, &major, &minor) == 0)
        {
            XCloseDisplay(display);
            throw new SparTestWindowUnavailableException($"Window tests cannot run here: the display {displayName} has no XTEST extension, which trusted input needs.");
        }

        var input = new XInput(display, gtkTurn);
        input.FindSpareKeycodes();
        return input;
    }

    /// <summary>The keysym a key event presses: its named key, its US-layout key by code, else its character's.</summary>
    internal static uint Keysym(SparTestKey key)
    {
        if (NamedKeysyms.TryGetValue(key.Key, out var named))
        {
            return named;
        }

        if (key.Key.StartsWith('F') && key.Key.Length is 2 or 3 && int.TryParse(key.Key.AsSpan(1), out var function) && function is >= 1 and <= 12)
        {
            return (uint)(0xffbe + function - 1);
        }

        if (key.Code.Length == 4 && key.Code.StartsWith("Key", StringComparison.Ordinal))
        {
            return char.ToLowerInvariant(key.Code[3]);
        }

        if (key.Code.Length == 6 && key.Code.StartsWith("Digit", StringComparison.Ordinal))
        {
            return key.Code[5];
        }

        if (PunctuationCodes.TryGetValue(key.Code, out var punctuation))
        {
            return punctuation;
        }

        // A character the US layout has no key for: Latin-1 keysyms are the code point, others 0x01000000 + it.
        var rune = System.Text.Rune.GetRuneAt(key.Key, 0);
        return rune.Value < 0x100 ? (uint)rune.Value : 0x01000000u + (uint)rune.Value;
    }

    /// <summary>Gives <paramref name="window"/> the keyboard focus, under the input lock and a server grab, and waits for the server.</summary>
    public async Task FocusAsync(nuint window)
    {
        await HoldInputAsync(window);
        await _lock.WaitAsync();
        try
        {
            XGrabServer(_display);
            try
            {
                XSetInputFocus(_display, window, 2, 0);
                XSync(_display, 0);
            }
            finally
            {
                XUngrabServer(_display);
            }

            XSync(_display, 0);
        }
        finally
        {
            _lock.Release();
            InputDone(window, 0);
        }
    }

    /// <summary>One key event into <paramref name="window"/>, which gets the input focus first.</summary>
    public async Task KeyAsync(nuint window, SparTestKey key)
    {
        await HoldInputAsync(window);
        var change = 0;
        try
        {
            await SendKeyAsync(window, key);
            change = key.Down ? 1 : -1;
        }
        finally
        {
            InputDone(window, change);
        }
    }

    private async Task SendKeyAsync(nuint window, SparTestKey key)
    {
        // A key on a spare keycode is pressed and released before another test may bind one: GTK translates a key by the
        // keymap of the moment it reads it, so a binding made between another key's press and its delivery typed the
        // wrong character (1 in about 6 suite runs before this).
        var spare = key.Down && NeedsSpare(key);
        if (spare)
        {
            // A stroke left half-done by a failed test does not hold the others for ever.
            spare = await _spareStroke.WaitAsync(TimeSpan.FromSeconds(5));
        }

        var releasesStroke = !key.Down && _spareKeys.Contains(key.Key);
        await _lock.WaitAsync();
        try
        {
            byte keycode;
            if (!key.Down && _pressed.Remove(key.Key, out var down))
            {
                keycode = down;
            }
            else
            {
                keycode = await KeycodeForAsync(key);
            }

            // The focus and the key under one server grab, so no other client's focus change (GTK sets the focus itself
            // when it presents a window and no window manager runs) comes between them. A guard: the measured cause of
            // lost keys was the shared keyboard state (_keyboard above), and the grab alone did not stop them.
            XGrabServer(_display);
            try
            {
                XSetInputFocus(_display, window, 2, 0);
                XTestFakeKeyEvent(_display, keycode, key.Down ? 1 : 0, 0);
                XSync(_display, 0);
            }
            finally
            {
                XUngrabServer(_display);
            }

            if (key.Down)
            {
                _pressed[key.Key] = keycode;
                _held.Add(Keysym(key));
            }
            else
            {
                _held.Remove(Keysym(key));
            }

            XSync(_display, 0);
            if (spare)
            {
                _spareKeys.Add(key.Key);
            }
            else if (releasesStroke)
            {
                _spareKeys.Remove(key.Key);
            }
        }
        catch
        {
            if (spare)
            {
                _spareStroke.Release();
            }

            throw;
        }
        finally
        {
            _lock.Release();
        }

        if (releasesStroke)
        {
            _spareStroke.Release();
        }
    }

    /// <summary>
    /// Waits until <paramref name="window"/> may send input: at once when it holds the input, else until no window does.
    /// A hold left by a failed test (keys or the button left down) passes on after 5 s, its count started again.
    /// </summary>
    private async Task HoldInputAsync(nuint window)
    {
        lock (_inputGate)
        {
            if (_inputOwner == window)
            {
                return;
            }
        }

        await _input.WaitAsync(TimeSpan.FromSeconds(5));
        lock (_inputGate)
        {
            _inputOwner = window;
            _inputDown = 0;
        }
    }

    /// <summary>
    /// <paramref name="window"/>'s event was sent, with <paramref name="change"/> keys or buttons down (+1), up (-1) or
    /// neither (0): the input is free again once none is down.
    /// </summary>
    private void InputDone(nuint window, int change)
    {
        lock (_inputGate)
        {
            if (_inputOwner != window)
            {
                // The hold passed on meanwhile.
                return;
            }

            _inputDown = Math.Max(0, _inputDown + change);
            if (_inputDown == 0)
            {
                _inputOwner = 0;
                _input.Release();
            }
        }
    }

    private bool NeedsSpare(SparTestKey key) =>
        (key.Code.Length == 0 && !NamedKeysyms.ContainsKey(key.Key))
        || ((key.Modifiers & 8) != 0 && !_held.Contains(ShiftKeysym) && key.Key != "Shift" && key.Text is { Length: > 0 });

    /// <summary>
    /// The keycode a key event presses. A character the US layout has no key for always gets a spare keycode of its own
    /// (the keymap may hold it on another level or group of a key that types something else, and the page must see no
    /// physical code), and so does a shifted character typed without Shift held (a typed capital, <c>"!"</c>): pressing
    /// Shift for it would give the page a Shift key event that a DevTools key event on Windows does not.
    /// </summary>
    private Task<byte> KeycodeForAsync(SparTestKey key)
    {
        var unmapped = key.Code.Length == 0 && !NamedKeysyms.ContainsKey(key.Key);
        var shiftedAlone = (key.Modifiers & 8) != 0 && !_held.Contains(ShiftKeysym) && key.Key != "Shift";
        if (shiftedAlone && key.Text is { Length: > 0 } text)
        {
            var rune = System.Text.Rune.GetRuneAt(text, 0);
            return BindAsync(rune.Value < 0x100 ? (uint)rune.Value : 0x01000000u + (uint)rune.Value);
        }

        return unmapped ? BindAsync(Keysym(key)) : KeycodeAsync(Keysym(key));
    }

    /// <summary>
    /// One pointer event at the screen point (<paramref name="x"/>, <paramref name="y"/>) over <paramref name="window"/>,
    /// raised first. False when it was a move to where the pointer already is, which sends the page nothing.
    /// </summary>
    public async Task<bool> PointerAsync(nuint window, int x, int y, SparTestPointer pointer)
    {
        await HoldInputAsync(window);
        var change = 0;
        try
        {
            var sent = await SendPointerAsync(window, x, y, pointer);
            change = pointer.Kind switch
            {
                SparTestPointerKind.Press => 1,
                SparTestPointerKind.Release => -1,
                _ => 0,
            };
            return sent;
        }
        finally
        {
            InputDone(window, change);
        }
    }

    private async Task<bool> SendPointerAsync(nuint window, int x, int y, SparTestPointer pointer)
    {
        await _lock.WaitAsync();
        try
        {
            XRaiseWindow(_display, window);
            var moved = _pointer != (x, y) || _pointerWindow != window;
            if (_pointerWindow != window)
            {
                // Another window (or a new one mapped where the pointer already was): a move into it first, so GTK sees the
                // pointer enter before a press; a press with no motion in the window was lost.
                XTestFakeMotionEvent(_display, -1, x + 1, y + 1, 0);
                _pointerWindow = window;
            }

            if (moved)
            {
                XTestFakeMotionEvent(_display, -1, x, y, 0);
                _pointer = (x, y);
            }

            switch (pointer.Kind)
            {
                case SparTestPointerKind.Press:
                    XTestFakeButtonEvent(_display, 1, 1, 0);
                    break;
                case SparTestPointerKind.Release:
                    XTestFakeButtonEvent(_display, 1, 0, 0);
                    break;
                case SparTestPointerKind.Wheel:
                    Wheel(pointer.DeltaY, down: 5, up: 4);
                    Wheel(pointer.DeltaX, down: 7, up: 6);
                    break;
            }

            XSync(_display, 0);
            return moved || pointer.Kind != SparTestPointerKind.Move;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>A wheel delta as notches of buttons 4–7, one per 100 CSS pixels and at least one when it is not zero.</summary>
    private void Wheel(float delta, uint down, uint up)
    {
        var notches = delta == 0 ? 0 : Math.Max(1, (int)MathF.Round(MathF.Abs(delta) / 100));
        for (var notch = 0; notch < notches; notch++)
        {
            var button = delta > 0 ? down : up;
            XTestFakeButtonEvent(_display, button, 1, 0);
            XTestFakeButtonEvent(_display, button, 0, 0);
        }
    }

    /// <summary>The keycode of <paramref name="keysym"/>, binding a spare one (and letting GTK read the new map) when none has it.</summary>
    private async Task<byte> KeycodeAsync(uint keysym)
    {
        var keycode = XKeysymToKeycode(_display, keysym);
        if (keycode != 0)
        {
            return keycode;
        }

        return await BindAsync(keysym);
    }

    /// <summary>A spare keycode bound to <paramref name="keysym"/> alone (reused while it stays bound).</summary>
    private async Task<byte> BindAsync(uint keysym)
    {
        if (_bound.TryGetValue(keysym, out var bound))
        {
            return bound;
        }

        if (_spare.Count == 0)
        {
            throw new InvalidOperationException($"The test display's keymap has no spare keycode for the keysym 0x{keysym:X}.");
        }

        // Never a keycode that is down now: its release must reach the same key (parallel tests type at once). The
        // keycodes WebKit names no code for come first; when each is down, another spare one is taken.
        var keycode = _spare[_nextSpare];
        for (var tries = 0; tries < _spare.Count && _pressed.ContainsValue(keycode); tries++)
        {
            _nextSpare = (_nextSpare + 1) % _spare.Count;
            keycode = _spare[_nextSpare];
        }

        _nextSpare = (_nextSpare + 1) % _spare.Count;
        if (_pressed.ContainsValue(keycode) && _otherSpare.FirstOrDefault(code => !_pressed.ContainsValue(code)) is var other and not 0)
        {
            keycode = other;
        }
        foreach (var (old, code) in _bound.Where(entry => entry.Value == keycode).ToList())
        {
            _bound.Remove(old);
        }

        Bind(keycode, keysym);
        _bound[keysym] = keycode;
        await _gtkTurn();
        await _gtkTurn();
        return keycode;
    }

    private unsafe void Bind(byte keycode, uint keysym)
    {
        var symbols = stackalloc nuint[2] { keysym, keysym };
        XChangeKeyboardMapping(_display, keycode, 2, symbols, 1);
        XSync(_display, 0);
    }

    /// <summary>The keycodes the display's keymap leaves empty, for characters it has no key for.</summary>
    private unsafe void FindSpareKeycodes()
    {
        int minimum, maximum, perKeycode;
        XDisplayKeycodes(_display, &minimum, &maximum);
        var count = maximum - minimum + 1;
        var map = XGetKeyboardMapping(_display, (byte)minimum, count, &perKeycode);
        if (map == null)
        {
            return;
        }

        try
        {
            for (var index = 0; index < count; index++)
            {
                var empty = true;
                for (var level = 0; level < perKeycode; level++)
                {
                    empty &= map[(index * perKeycode) + level] == 0;
                }

                if (empty)
                {
                    _spare.Add((byte)(minimum + index));
                }
            }
        }
        finally
        {
            XFree(map);
        }


        // Keycodes with no evdev key first (evdev 240, KEY_UNKNOWN, and 195-199, unassigned): WebKit names no physical
        // code for them ("Unidentified"); another spare keycode can be a key it knows (F19 was one under Xvfb).
        var unknown = _spare.Where(code => code is 248 or (>= 203 and <= 207)).ToList();
        if (unknown.Count > 0)
        {
            _otherSpare.AddRange(_spare.Where(code => !unknown.Contains(code)));
            _spare.RemoveAll(code => !unknown.Contains(code));
        }
    }
}
