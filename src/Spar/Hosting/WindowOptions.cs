using System.Globalization;

namespace Spar;

/// <summary>
/// The main window's options, from <c>spar.Window(w => …)</c> in <c>Program.cs</c> (<c>docs/app-model.md</c> section
/// 2). The colours are among the values the build reads, so they are constants there (plan section 2, "Program.cs").
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

    internal WindowOptions()
    {
    }

    /// <summary>
    /// The window colour before the page paints, for each theme (plan section 3d), as <c>#rrggbb</c> or <c>#rgb</c>.
    /// The host paints it from the window's first frame, and the WebView stays transparent over it until the page
    /// draws, so neither theme flashes white. Use the page's own background colours.
    /// </summary>
    public WindowOptions Background(string light, string dark)
    {
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
