using System.Runtime.InteropServices;
using Spar.WindowAutomation.Linux.Native;

namespace Spar.WindowAutomation.Linux;

/// <summary>
/// Trusted input through the X server's XTEST extension, on the harness's own display connection. The pointer and the
/// keyboard focus are global to the display, so one display serves one test at a time (the lock serialises input).
/// </summary>
internal sealed unsafe class XInput : IDisposable
{
    private static readonly Dictionary<string, nuint> s_named = new()
    {
        ["Enter"] = 0xff0d,
        ["Escape"] = 0xff1b,
        ["Tab"] = 0xff09,
        ["Backspace"] = 0xff08,
        ["Delete"] = 0xffff,
        ["ArrowDown"] = 0xff54,
        ["ArrowUp"] = 0xff52,
        ["ArrowLeft"] = 0xff51,
        ["ArrowRight"] = 0xff53,
        ["Home"] = 0xff50,
        ["End"] = 0xff57,
        ["PageUp"] = 0xff55,
        ["PageDown"] = 0xff56,
        ["Shift"] = 0xffe1,
        ["Control"] = 0xffe3,
        ["Alt"] = 0xffe9,
        [" "] = 0x20,
    };

    private const uint ShiftKeysym = 0xffe1;

    private readonly Lock _lock = new();
    private readonly nint _display;
    private readonly Dictionary<nuint, (byte Code, bool Shift)> _codes = [];
    private readonly List<int> _scratch = [];
    private readonly Dictionary<int, nuint> _scratchHolds = [];
    private readonly int _minimum;
    private readonly int _maximum;
    private int _nextScratch;

    public XInput()
    {
        _display = X11.XOpenDisplay(0);
        if (_display == 0)
        {
            throw new InvalidOperationException("XOpenDisplay failed: DISPLAY must name an X server (GDK_BACKEND=x11)");
        }

        if (X11.XTestQueryExtension(_display, out _, out _, out var major, out var minor) == 0)
        {
            throw new InvalidOperationException("the X server has no XTEST extension");
        }

        Version = $"{major}.{minor}";
        X11.XDisplayKeycodes(_display, out _minimum, out _maximum);
        LoadKeymap();
    }

    public string Version { get; }

    /// <summary>Characters the keymap lacked, bound to a spare keycode before typing them.</summary>
    public int Remapped { get; private set; }

    public int SpareKeycodes => _scratch.Count;

    /// <summary>The window's origin in root coordinates.</summary>
    public (int X, int Y) RootOrigin(nuint xid)
    {
        lock (_lock)
        {
            X11.XTranslateCoordinates(_display, xid, X11.XDefaultRootWindow(_display), 0, 0, out var x, out var y, out _);
            return (x, y);
        }
    }

    /// <summary>Who has the keyboard focus (0 none, 1 PointerRoot, else a window).</summary>
    public nuint Focus()
    {
        lock (_lock)
        {
            X11.XGetInputFocus(_display, out var window, out _);
            return window;
        }
    }

    /// <summary>The root's children as "0xID mapped|unmapped 'name'" (a new dialog from any process adds one).</summary>
    public List<string> TopLevels()
    {
        lock (_lock)
        {
            var list = new List<string>();
            X11.XQueryTree(_display, X11.XDefaultRootWindow(_display), out _, out _, out var children, out var count);
            var attributes = stackalloc byte[256];
            for (var i = 0; i < count; i++)
            {
                var window = ((nuint*)children)[i];
                X11.XGetWindowAttributes(_display, window, attributes);
                var mapState = *(int*)(attributes + 92);
                X11.XFetchName(_display, window, out var name);
                list.Add($"0x{window:x} {(mapState == 2 ? "viewable" : mapState == 1 ? "unviewable" : "unmapped")} {*(int*)(attributes + 8)}x{*(int*)(attributes + 12)}{(*(int*)(attributes + 120) != 0 ? " override-redirect" : "")} '{GLib.Utf8(name)}'");
                if (name != 0)
                {
                    X11.XFree(name);
                }
            }

            if (children != 0)
            {
                X11.XFree(children);
            }

            return list;
        }
    }

    public void SetFocus(nuint xid)
    {
        lock (_lock)
        {
            X11.XSetInputFocus(_display, xid, 2, 0);
            X11.XSync(_display, 0);
        }
    }

    public void Move(int x, int y)
    {
        lock (_lock)
        {
            X11.XTestFakeMotionEvent(_display, -1, x, y, 0);
            X11.XSync(_display, 0);
        }
    }

    public void Click(int x, int y, uint button = 1)
    {
        lock (_lock)
        {
            X11.XTestFakeMotionEvent(_display, -1, x, y, 0);
            X11.XTestFakeButtonEvent(_display, button, 1, 0);
            X11.XTestFakeButtonEvent(_display, button, 0, 0);
            X11.XSync(_display, 0);
        }
    }

    /// <summary>A named key ("Enter", "Escape", …) or one character. Call <see cref="Prepare"/> first for characters.</summary>
    public void Press(string key)
    {
        lock (_lock)
        {
            Stroke(KeysymOf(key));
            X11.XSync(_display, 0);
        }
    }

    /// <summary>
    /// Binds every character of <paramref name="text"/> that the keymap lacks to its own spare keycode and returns
    /// how many characters, from the start, can be typed now (spare keycodes are few). When it bound any, the caller
    /// must let the app process the MappingNotify before typing, or the app types with its old keymap.
    /// </summary>
    public (int Length, bool Remapped) Prepare(string text)
    {
        lock (_lock)
        {
            var bound = new HashSet<nuint>();
            var remapped = false;
            var i = 0;
            while (i < text.Length)
            {
                var step = char.IsSurrogatePair(text, i) ? 2 : 1;
                var keysym = Keysym(char.ConvertToUtf32(text, i));
                if (!_codes.ContainsKey(keysym))
                {
                    if (_scratch.Count == 0 || bound.Count >= _scratch.Count)
                    {
                        break;
                    }

                    Bind(keysym, bound);
                    bound.Add(keysym);
                    remapped = true;
                }
                else if (_scratchHolds.ContainsValue(keysym))
                {
                    bound.Add(keysym);
                }

                i += step;
            }

            X11.XSync(_display, 0);
            return (Math.Max(i, Math.Min(1, text.Length)), remapped);
        }
    }

    /// <summary>Types text, one key stroke per character (Shift where the keymap needs it).</summary>
    public void Type(string text)
    {
        lock (_lock)
        {
            for (var i = 0; i < text.Length; i += char.IsSurrogatePair(text, i) ? 2 : 1)
            {
                Stroke(Keysym(char.ConvertToUtf32(text, i)));
            }

            X11.XSync(_display, 0);
        }
    }

    public void Dispose() => X11.XCloseDisplay(_display);

    // Latin-1 characters are their own keysyms; everything else is 0x01000000 + the code point.
    private static nuint Keysym(int codePoint) => codePoint is >= 0x20 and <= 0x7e or >= 0xa0 and <= 0xff ? (nuint)codePoint : (nuint)(0x01000000 + codePoint);

    private static nuint KeysymOf(string key) =>
        s_named.TryGetValue(key, out var named) ? named
        : key.Length == 1 || key.Length == 2 && char.IsSurrogatePair(key, 0) ? Keysym(char.ConvertToUtf32(key, 0))
        : throw new ArgumentException("Unknown key: " + key, nameof(key));

    private void Stroke(nuint keysym)
    {
        if (!_codes.TryGetValue(keysym, out var code))
        {
            throw new InvalidOperationException($"keysym 0x{keysym:x} is not in the keymap: call Prepare first");
        }

        var shift = _codes[ShiftKeysym].Code;
        if (code.Shift)
        {
            X11.XTestFakeKeyEvent(_display, shift, 1, 0);
        }

        X11.XTestFakeKeyEvent(_display, code.Code, 1, 0);
        X11.XTestFakeKeyEvent(_display, code.Code, 0, 0);
        if (code.Shift)
        {
            X11.XTestFakeKeyEvent(_display, shift, 0, 0);
        }
    }

    private void LoadKeymap()
    {
        var count = _maximum - _minimum + 1;
        var map = X11.XGetKeyboardMapping(_display, (byte)_minimum, count, out var perCode);
        var syms = (nuint*)map;
        for (var i = 0; i < count; i++)
        {
            for (var level = 0; level < Math.Min(perCode, 2); level++)
            {
                var keysym = syms[i * perCode + level];
                if (keysym != 0 && !_codes.ContainsKey(keysym))
                {
                    _codes[keysym] = ((byte)(_minimum + i), level == 1);
                }
            }

            if (Enumerable.Range(0, perCode).All(l => syms[i * perCode + l] == 0))
            {
                _scratch.Add(_minimum + i);
            }
        }

        X11.XFree(map);
    }

    // A character the keymap lacks gets a spare keycode (round robin), as xdotool does.
    private void Bind(nuint keysym, HashSet<nuint> keep)
    {
        var code = _scratch[_nextScratch++ % _scratch.Count];
        while (_scratchHolds.TryGetValue(code, out var held) && keep.Contains(held))
        {
            code = _scratch[_nextScratch++ % _scratch.Count];
        }

        if (_scratchHolds.TryGetValue(code, out var previous))
        {
            _codes.Remove(previous);
        }

        var syms = stackalloc nuint[2] { keysym, keysym };
        X11.XChangeKeyboardMapping(_display, code, 2, syms, 1);
        _scratchHolds[code] = keysym;
        _codes[keysym] = ((byte)code, false);
        Remapped++;
    }
}
