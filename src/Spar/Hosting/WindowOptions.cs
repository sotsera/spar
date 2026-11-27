using System.Globalization;
using Spar.AppWindows;

namespace Spar;

/// <summary>
/// A window's options, from <c>spar.Window(w => …)</c> for the main window and <c>spar.Window("kind", w => …)</c> for a
/// window kind the page opens by name (<c>docs/app-model.md</c> sections 2 and 6.7). The main window's colours are
/// among the values the build reads, so they are constants there (plan section 2, "Program.cs"); every kind takes them.
/// </summary>
public sealed class WindowOptions
{
    /// <summary>The size a window gets when the app sets none, in device-independent pixels.</summary>
    internal static (int Width, int Height) DefaultSize => (1024, 768);

    /// <summary>The window colour before the page paints, in the light theme.</summary>
    internal SparColor LightBackground { get; private set; } = SparColor.DefaultLight;

    /// <summary>The window colour before the page paints, in the dark theme.</summary>
    internal SparColor DarkBackground { get; private set; } = SparColor.DefaultDark;

    /// <summary>The client area's size at the first start, in device-independent pixels.</summary>
    internal (int Width, int Height) InitialSize { get; private set; } = DefaultSize;

    /// <summary>The smallest client area the user can resize the window to; zero for none.</summary>
    internal (int Width, int Height) Minimum { get; private set; }

    /// <summary>The kind's name; null for the main window.</summary>
    internal string? Kind { get; }

    /// <summary>The path of the frontend the window shows: <c>/</c> for the main window, the kind's route otherwise.</summary>
    internal string PageRoute { get; private set; } = "/";

    /// <summary>Whether a kind has at most one window open, which opening it again focuses.</summary>
    internal bool IsSingleInstance { get; private set; }

    /// <summary>Whether the kind is a modal, which the page shows with <c>windows.showModal</c> and ends with <c>closeWith</c>.</summary>
    internal bool IsModal { get; private set; }

    /// <summary>Whether the window's placement and zoom are kept in <c>windows.json</c> between runs.</summary>
    internal bool RemembersPlacement { get; private set; } = true;

    /// <summary>Whether the page zooms, and between which factors.</summary>
    internal (bool Enabled, double Min, double Max) ZoomLimits { get; private set; } = (true, 0.25, 5);

    /// <summary>The contracts and commands the window may call; null when it may call every one.</summary>
    internal CommandAllowList? AllowList { get; private set; }

    /// <summary>Whether the kind named its route (a kind needs one).</summary>
    internal bool HasRoute { get; private set; }

    /// <summary>The allow-list resolved against the app's contracts when the app starts; null when the window may call every one.</summary>
    internal WindowAccess? Access { get; set; }

    internal WindowOptions(string? kind = null)
    {
        Kind = kind;
    }

    /// <summary>
    /// The window colour before the page paints, for each theme (plan section 3d), as <c>#rrggbb</c> or <c>#rgb</c>.
    /// The host paints it from the window's first frame, and the WebView stays transparent over it until the page
    /// draws, so neither theme flashes white. Use the page's own background colours. The main window's only: every
    /// window kind takes them.
    /// </summary>
    public WindowOptions Background(string light, string dark)
    {
        if (Kind is not null)
        {
            throw new InvalidOperationException($"Window kind '{Kind}': the colours are the main window's, which every kind takes; set them in spar.Window(w => w.Background(…)).");
        }

        LightBackground = SparColor.Parse(light, nameof(light));
        DarkBackground = SparColor.Parse(dark, nameof(dark));
        return this;
    }

    /// <summary>
    /// The size of the window's content (the page), in device-independent pixels, at the first start; the window is
    /// centred on the primary screen.
    /// </summary>
    public WindowOptions Size(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);
        InitialSize = (width, height);
        return this;
    }

    /// <summary>The smallest size of the window's content the user can resize it to, in device-independent pixels.</summary>
    public WindowOptions MinSize(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(width);
        ArgumentOutOfRangeException.ThrowIfNegative(height);
        Minimum = (width, height);
        return this;
    }

    /// <summary>
    /// The path of the app's own frontend a window of this kind shows, such as <c>/settings</c>: the host serves the
    /// frontend's page there, and the page's router picks its view. A window kind needs one; the main window shows
    /// <c>/</c>.
    /// </summary>
    public WindowOptions Route(string route)
    {
        if (Kind is null)
        {
            throw new InvalidOperationException("The main window shows the frontend's root, '/'; a route is for a window kind.");
        }

        PageRoute = WindowRoutes.Check(route, Kind);
        HasRoute = true;
        return this;
    }

    /// <summary>At most one window of this kind is open: opening it again focuses the open one.</summary>
    public WindowOptions SingleInstance()
    {
        if (Kind is null)
        {
            throw new InvalidOperationException("The main window is always one; SingleInstance is for a window kind.");
        }

        IsSingleInstance = true;
        return this;
    }

    /// <summary>
    /// The kind is a modal (decision "M3d's decide-first questions", item 4): the page shows it with
    /// <c>windows.showModal(kind, params)</c>, which waits until it closes, and the modal's own page ends it with
    /// <c>windows.closeWith(value)</c>, which hands the value to the opener; closed any other way, the opener gets
    /// <c>dismissed</c>. While it is open, the window that opened it takes no input and the modal stays above it; the
    /// app's other windows stay usable. A modal kind is never opened with <c>windows.open</c>.
    /// </summary>
    public WindowOptions Modal()
    {
        if (Kind is null)
        {
            throw new InvalidOperationException("The main window is never a modal; Modal is for a window kind.");
        }

        IsModal = true;
        return this;
    }

    /// <summary>
    /// Whether the window's placement is remembered between runs, in <c>windows.json</c> in the app's data folder (on by
    /// default; decision "M3d's decide-first questions", item 5). The main window and a single-instance kind keep their
    /// size, position, maximized state and zoom; a kind with several windows its last size and zoom, each new window
    /// opening offset from the last; a modal its size and zoom, always centred on its opener. A place on no visible
    /// screen opens centred instead. On Wayland only the size and maximized state are kept.
    /// </summary>
    public WindowOptions RememberPlacement(bool remember = true)
    {
        RemembersPlacement = remember;
        return this;
    }

    /// <summary>
    /// The page's zoom (<c>docs/app-model.md</c> 6.8): on by default, with Ctrl+wheel and Ctrl+plus, minus and 0, because
    /// people with low vision rely on them, between <paramref name="min"/> and <paramref name="max"/> (0.25 to 5, with 1
    /// between them); <c>Zoom(false)</c> turns it off. The factor is remembered with the window's placement, and the page
    /// reads and sets it with <c>zoom</c> from <c>@spar/app/sdk</c>.
    /// </summary>
    public WindowOptions Zoom(bool enabled = true, double min = 0.25, double max = 5)
    {
        if (!double.IsFinite(min) || !double.IsFinite(max) || min < 0.25 || max > 5 || min > 1 || max < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(min), $"A window's zoom goes from at least 0.25 to at most 5, with 1 between: Zoom(min: {min}, max: {max}).");
        }

        ZoomLimits = (enabled, min, max);
        return this;
    }

    /// <summary>
    /// Limits the app's contracts the window may call (decision "Per-window command allow-lists"); a call to any other
    /// is refused before it runs (<c>rejected</c> / <c>forbidden-command</c>). Every window may call every command by
    /// default. Recommended for a window that shows content the app does not fully control. Spar's own page commands
    /// (dialogs, grants, the colour scheme, windows) stay allowed.
    /// </summary>
    public WindowOptions Commands(Action<CommandAllowList> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        AllowList ??= new CommandAllowList();
        configure(AllowList);
        return this;
    }
}

/// <summary>An opaque colour of the window, from <c>#rrggbb</c> or <c>#rgb</c>.</summary>
internal readonly record struct SparColor(byte Red, byte Green, byte Blue)
{
    /// <summary>The light theme's colour when the app sets none: white, as a page without styles is.</summary>
    public static SparColor DefaultLight => new(0xFF, 0xFF, 0xFF);

    /// <summary>The dark theme's colour when the app sets none: the dark background of Windows' own apps.</summary>
    public static SparColor DefaultDark => new(0x1F, 0x1F, 0x1F);

    /// <summary>The colour as a Win32 <c>COLORREF</c>, <c>0x00BBGGRR</c>.</summary>
    public uint ColorRef => (uint)(Red | (Green << 8) | (Blue << 16));

    public static SparColor Parse(string text, string parameter)
    {
        ArgumentException.ThrowIfNullOrEmpty(text, parameter);
        if (TryParse(text, out var color))
        {
            return color;
        }

        throw new ArgumentException($"'{text}' is not a colour of the form #rrggbb or #rgb.", parameter);
    }

    public static bool TryParse(string text, out SparColor color)
    {
        color = default;
        if (text.Length is not (4 or 7) || text[0] != '#')
        {
            return false;
        }

        var digits = text.AsSpan(1);
        foreach (var digit in digits)
        {
            if (!char.IsAsciiHexDigit(digit))
            {
                return false;
            }
        }

        if (digits.Length == 3)
        {
            color = new SparColor(Twice(digits[0]), Twice(digits[1]), Twice(digits[2]));
            return true;
        }

        color = new SparColor(Byte(digits[..2]), Byte(digits[2..4]), Byte(digits[4..]));
        return true;
    }

    public override string ToString() => $"#{Red:x2}{Green:x2}{Blue:x2}";

    private static byte Byte(ReadOnlySpan<char> pair) => byte.Parse(pair, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);

    private static byte Twice(char digit) => Convert.FromHexString([digit, digit])[0];
}
