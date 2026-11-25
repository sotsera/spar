using Microsoft.Win32;
using Spar.Hosting;
using Spar.Windows.Interop;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;

namespace Spar.Windows;

/// <summary>
/// The window's theme without flicker (plan section 3d): which scheme applies, the system's (<c>AppsUseLightTheme</c>)
/// or the app's own saved choice, and the host's layers for it: the window colour from <c>spar.Window(w =>
/// w.Background(light, dark))</c> as the class brush and in <c>WM_ERASEBKGND</c>, the title bar through
/// <c>DWMWA_USE_IMMERSIVE_DARK_MODE</c> before the window is shown, and the scheme the profile hands the page
/// (<c>PreferredColorScheme</c>). The WebView itself is transparent over the window (<see cref="BrowserView"/>).
/// </summary>
internal sealed class WindowTheme
{
    private const string PersonalizeKey = @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    private readonly WindowOptions _options;

    private WindowTheme(WindowOptions options, bool? appChoosesDark, bool systemDark)
    {
        _options = options;
        AppChoosesDark = appChoosesDark;
        IsDark = appChoosesDark ?? systemDark;
    }

    /// <summary>The app's own choice (true dark, false light), or null when the window follows the system.</summary>
    public bool? AppChoosesDark { get; private set; }

    /// <summary>Whether the window is dark now.</summary>
    public bool IsDark { get; private set; }

    /// <summary>The window colour now.</summary>
    public SparColor Background => IsDark ? _options.DarkBackground : _options.LightBackground;

    /// <summary>The profile's <c>PreferredColorScheme</c>: the app's choice, or Auto, which follows the system by itself.</summary>
    public int PageScheme => AppChoosesDark switch
    {
        true => WebView2Values.ColorSchemeDark,
        false => WebView2Values.ColorSchemeLight,
        null => WebView2Values.ColorSchemeAuto,
    };

    /// <summary>
    /// Reads the theme before the window exists: the saved choice from <paramref name="dataRoot"/>
    /// (<see cref="SavedColorScheme"/>), else the system's. Both are small local reads on the main thread, before any
    /// window.
    /// </summary>
    public static WindowTheme Read(WindowOptions options, string dataRoot) =>
        new(options, SavedColorScheme.Read(dataRoot), SystemIsDark());

    /// <summary>
    /// A fixed scheme, whatever the system and the saved choice say: a window test's light or dark, so its screenshots
    /// match on every machine. The window, its title bar and its page all take it, and a change of the system's theme
    /// changes nothing.
    /// </summary>
    public static WindowTheme Fixed(WindowOptions options, bool dark) => new(options, dark, systemDark: dark);

    /// <summary>Sets the title bar's theme; called before the window is first shown, and on a change.</summary>
    public unsafe void ApplyTitleBar(HWND window)
    {
        BOOL dark = IsDark;
        _ = PInvoke.DwmSetWindowAttribute(window, DWMWINDOWATTRIBUTE.DWMWA_USE_IMMERSIVE_DARK_MODE, &dark, (uint)sizeof(BOOL));
    }

    /// <summary>
    /// After <c>WM_SETTINGCHANGE</c> with <c>ImmersiveColorSet</c>: true when the window follows the system and its
    /// theme changed. The read is one registry value of the user's hive, already in memory.
    /// </summary>
    public bool SystemChanged()
    {
        if (AppChoosesDark is not null)
        {
            return false;
        }

        var dark = SystemIsDark();
        if (dark == IsDark)
        {
            return false;
        }

        IsDark = dark;
        return true;
    }

    /// <summary>
    /// The page's <c>setColorScheme</c> while the app runs (UI thread): the app's choice from now on, or the system's
    /// again for null. True when the window's scheme changed; the profile's <see cref="PageScheme"/> changes either way.
    /// </summary>
    public bool Choose(bool? dark)
    {
        AppChoosesDark = dark;
        var now = dark ?? SystemIsDark();
        if (now == IsDark)
        {
            return false;
        }

        IsDark = now;
        return true;
    }

    private static bool SystemIsDark() => Registry.GetValue(PersonalizeKey, "AppsUseLightTheme", 1) is 0;
}
