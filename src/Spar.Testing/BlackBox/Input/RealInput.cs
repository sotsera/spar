using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Windows.Win32;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Spar.Testing;

/// <summary>A mouse button real input presses.</summary>
internal enum RealMouseButton
{
    Left,
    Right,
    Middle,

    /// <summary>The first extra button: "back" in browsers.</summary>
    Back,

    /// <summary>The second extra button: "forward" in browsers.</summary>
    Forward,
}

/// <summary>
/// Real input through <c>SendInput</c>: the mouse at a screen point and keys as a keyboard sends them, into whatever
/// window has the foreground. Only inside a Windows Sandbox guest Spar's runner started, in its user session
/// (<see cref="SandboxGuest.EnsureUserSession"/>): every call checks that first and sends nothing anywhere else, so it
/// can never reach the developer's desktop. Key names are the page API's (<see cref="KeyboardLayout.Parse"/>): a US
/// keyboard's virtual keys, and a character it has no key for sent as Unicode input.
/// </summary>
internal static unsafe class RealInput
{
    /// <summary>Clicks <paramref name="button"/> at the screen point (<paramref name="x"/>, <paramref name="y"/>), in physical pixels.</summary>
    [SupportedOSPlatform("windows10.0.17763")]
    public static void Click(int x, int y, RealMouseButton button = RealMouseButton.Left)
    {
        SandboxGuest.EnsureUserSession("send real input");
        var (down, up, data) = Flags(button);
        var (dx, dy) = Absolute(x, y);
        const MOUSE_EVENT_FLAGS At = MOUSE_EVENT_FLAGS.MOUSEEVENTF_ABSOLUTE | MOUSE_EVENT_FLAGS.MOUSEEVENTF_VIRTUALDESK;
        Send(
        [
            Mouse(dx, dy, 0, MOUSE_EVENT_FLAGS.MOUSEEVENTF_MOVE | At),
            Mouse(dx, dy, data, down | At),
            Mouse(dx, dy, data, up | At),
        ]);
    }

    /// <summary>Moves the mouse to the screen point (<paramref name="x"/>, <paramref name="y"/>), in physical pixels, buttons as they are.</summary>
    [SupportedOSPlatform("windows10.0.17763")]
    public static void Move(int x, int y)
    {
        SandboxGuest.EnsureUserSession("send real input");
        var (dx, dy) = Absolute(x, y);
        Send([Mouse(dx, dy, 0, MOUSE_EVENT_FLAGS.MOUSEEVENTF_MOVE | MOUSE_EVENT_FLAGS.MOUSEEVENTF_ABSOLUTE | MOUSE_EVENT_FLAGS.MOUSEEVENTF_VIRTUALDESK)]);
    }

    /// <summary>
    /// Drags with the left button from one screen point to another, in physical pixels: the press, a move in
    /// <paramref name="steps"/> steps with <paramref name="pause"/> between them (a window manager's move follows motion
    /// it sees), and the release. It blocks the calling test thread for the pauses, never a window's.
    /// </summary>
    [SupportedOSPlatform("windows10.0.17763")]
    public static void Drag(int fromX, int fromY, int toX, int toY, int steps = 8, int pause = 15)
    {
        SandboxGuest.EnsureUserSession("send real input");
        const MOUSE_EVENT_FLAGS At = MOUSE_EVENT_FLAGS.MOUSEEVENTF_ABSOLUTE | MOUSE_EVENT_FLAGS.MOUSEEVENTF_VIRTUALDESK;
        var (startX, startY) = Absolute(fromX, fromY);
        Send([Mouse(startX, startY, 0, MOUSE_EVENT_FLAGS.MOUSEEVENTF_MOVE | At), Mouse(startX, startY, 0, MOUSE_EVENT_FLAGS.MOUSEEVENTF_LEFTDOWN | At)]);
        for (var step = 1; step <= steps; step++)
        {
            Thread.Sleep(pause);
            var (dx, dy) = Absolute(fromX + ((toX - fromX) * step / steps), fromY + ((toY - fromY) * step / steps));
            Send([Mouse(dx, dy, 0, MOUSE_EVENT_FLAGS.MOUSEEVENTF_MOVE | At)]);
        }

        Thread.Sleep(pause);
        var (endX, endY) = Absolute(toX, toY);
        Send([Mouse(endX, endY, 0, MOUSE_EVENT_FLAGS.MOUSEEVENTF_LEFTUP | At)]);
    }

    /// <summary>Double-clicks the left button at the screen point, in physical pixels.</summary>
    [SupportedOSPlatform("windows10.0.17763")]
    public static void DoubleClick(int x, int y)
    {
        SandboxGuest.EnsureUserSession("send real input");
        var (dx, dy) = Absolute(x, y);
        const MOUSE_EVENT_FLAGS At = MOUSE_EVENT_FLAGS.MOUSEEVENTF_ABSOLUTE | MOUSE_EVENT_FLAGS.MOUSEEVENTF_VIRTUALDESK;
        Send(
        [
            Mouse(dx, dy, 0, MOUSE_EVENT_FLAGS.MOUSEEVENTF_MOVE | At),
            Mouse(dx, dy, 0, MOUSE_EVENT_FLAGS.MOUSEEVENTF_LEFTDOWN | At),
            Mouse(dx, dy, 0, MOUSE_EVENT_FLAGS.MOUSEEVENTF_LEFTUP | At),
            Mouse(dx, dy, 0, MOUSE_EVENT_FLAGS.MOUSEEVENTF_LEFTDOWN | At),
            Mouse(dx, dy, 0, MOUSE_EVENT_FLAGS.MOUSEEVENTF_LEFTUP | At),
        ]);
    }

    /// <summary>
    /// Presses a key or a combination, such as <c>"F12"</c>, <c>"Control+Shift+I"</c> or <c>"Alt+ArrowLeft"</c>: the
    /// modifiers down in order, the key down and up, the modifiers up in reverse.
    /// </summary>
    [SupportedOSPlatform("windows10.0.17763")]
    public static void Press(string combination)
    {
        SandboxGuest.EnsureUserSession("send real input");
        var (modifiers, key) = KeyboardLayout.Parse(combination);
        var held = Held(modifiers);
        var inputs = new List<INPUT>();
        foreach (var modifier in held)
        {
            inputs.Add(Key(modifier, up: false));
        }

        if (key.VirtualKey == 0)
        {
            foreach (var unit in key.Text ?? key.Name)
            {
                inputs.Add(Unicode(unit, up: false));
                inputs.Add(Unicode(unit, up: true));
            }
        }
        else
        {
            inputs.Add(Key((ushort)key.VirtualKey, up: false));
            inputs.Add(Key((ushort)key.VirtualKey, up: true));
        }

        for (var index = held.Count - 1; index >= 0; index--)
        {
            inputs.Add(Key(held[index], up: true));
        }

        Send(CollectionsMarshal.AsSpan(inputs));
    }

    /// <summary>Types <paramref name="text"/> as Unicode input, one key press per UTF-16 unit, whatever the keyboard layout.</summary>
    [SupportedOSPlatform("windows10.0.17763")]
    public static void Type(string text)
    {
        SandboxGuest.EnsureUserSession("send real input");
        ArgumentNullException.ThrowIfNull(text);
        var inputs = new List<INPUT>(text.Length * 2);
        foreach (var unit in text)
        {
            inputs.Add(Unicode(unit, up: false));
            inputs.Add(Unicode(unit, up: true));
        }

        Send(CollectionsMarshal.AsSpan(inputs));
    }

    /// <summary>The modifiers' virtual keys in the order they go down: Control, Shift, Alt, the Windows key.</summary>
    internal static List<ushort> Held(KeyboardLayout.Modifiers modifiers)
    {
        var held = new List<ushort>(4);
        if (modifiers.HasFlag(KeyboardLayout.Modifiers.Control))
        {
            held.Add(0x11);
        }

        if (modifiers.HasFlag(KeyboardLayout.Modifiers.Shift))
        {
            held.Add(0x10);
        }

        if (modifiers.HasFlag(KeyboardLayout.Modifiers.Alt))
        {
            held.Add(0x12);
        }

        if (modifiers.HasFlag(KeyboardLayout.Modifiers.Meta))
        {
            held.Add(0x5B);
        }

        return held;
    }

    /// <summary>Keys a keyboard sends with the extended flag: the navigation block, the arrows and the right-hand keys.</summary>
    internal static bool IsExtended(ushort virtualKey) => virtualKey is
        >= 0x21 and <= 0x28 // PageUp, PageDown, End, Home, the arrows
        or 0x2D or 0x2E // Insert, Delete
        or 0x5B or 0x5C; // the Windows keys

    private static (MOUSE_EVENT_FLAGS Down, MOUSE_EVENT_FLAGS Up, int Data) Flags(RealMouseButton button) => button switch
    {
        RealMouseButton.Left => (MOUSE_EVENT_FLAGS.MOUSEEVENTF_LEFTDOWN, MOUSE_EVENT_FLAGS.MOUSEEVENTF_LEFTUP, 0),
        RealMouseButton.Right => (MOUSE_EVENT_FLAGS.MOUSEEVENTF_RIGHTDOWN, MOUSE_EVENT_FLAGS.MOUSEEVENTF_RIGHTUP, 0),
        RealMouseButton.Middle => (MOUSE_EVENT_FLAGS.MOUSEEVENTF_MIDDLEDOWN, MOUSE_EVENT_FLAGS.MOUSEEVENTF_MIDDLEUP, 0),
        RealMouseButton.Back => (MOUSE_EVENT_FLAGS.MOUSEEVENTF_XDOWN, MOUSE_EVENT_FLAGS.MOUSEEVENTF_XUP, 1), // XBUTTON1
        RealMouseButton.Forward => (MOUSE_EVENT_FLAGS.MOUSEEVENTF_XDOWN, MOUSE_EVENT_FLAGS.MOUSEEVENTF_XUP, 2), // XBUTTON2
        _ => throw new ArgumentOutOfRangeException(nameof(button)),
    };

    /// <summary>A screen point in the 0–65535 range <c>MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK</c> takes.</summary>
    [SupportedOSPlatform("windows10.0.17763")]
    private static (int X, int Y) Absolute(int x, int y)
    {
        var left = PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_XVIRTUALSCREEN);
        var top = PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_YVIRTUALSCREEN);
        var width = Math.Max(2, PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_CXVIRTUALSCREEN));
        var height = Math.Max(2, PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_CYVIRTUALSCREEN));
        return ((int)Math.Round((x - left) * 65535.0 / (width - 1)), (int)Math.Round((y - top) * 65535.0 / (height - 1)));
    }

    private static INPUT Mouse(int x, int y, int data, MOUSE_EVENT_FLAGS flags)
    {
        var input = new INPUT { type = INPUT_TYPE.INPUT_MOUSE };
        input.Anonymous.mi = new MOUSEINPUT { dx = x, dy = y, mouseData = unchecked((uint)data), dwFlags = flags };
        return input;
    }

    [SupportedOSPlatform("windows10.0.17763")]
    private static INPUT Key(ushort virtualKey, bool up)
    {
        var flags = up ? KEYBD_EVENT_FLAGS.KEYEVENTF_KEYUP : 0;
        if (IsExtended(virtualKey))
        {
            flags |= KEYBD_EVENT_FLAGS.KEYEVENTF_EXTENDEDKEY;
        }

        var scan = (ushort)PInvoke.MapVirtualKey(virtualKey, MAP_VIRTUAL_KEY_TYPE.MAPVK_VK_TO_VSC);
        var input = new INPUT { type = INPUT_TYPE.INPUT_KEYBOARD };
        input.Anonymous.ki = new KEYBDINPUT { wVk = (VIRTUAL_KEY)virtualKey, wScan = scan, dwFlags = flags };
        return input;
    }

    private static INPUT Unicode(char unit, bool up)
    {
        var flags = KEYBD_EVENT_FLAGS.KEYEVENTF_UNICODE | (up ? KEYBD_EVENT_FLAGS.KEYEVENTF_KEYUP : 0);
        var input = new INPUT { type = INPUT_TYPE.INPUT_KEYBOARD };
        input.Anonymous.ki = new KEYBDINPUT { wScan = unit, dwFlags = flags };
        return input;
    }

    [SupportedOSPlatform("windows10.0.17763")]
    private static void Send(ReadOnlySpan<INPUT> inputs)
    {
        var sent = PInvoke.SendInput(inputs, sizeof(INPUT));
        if (sent != inputs.Length)
        {
            throw new InvalidOperationException(
                $"SendInput sent {sent} of {inputs.Length} events ({Marshal.GetLastPInvokeError()}): another desktop or a " +
                "higher-integrity window has the input.");
        }
    }
}

