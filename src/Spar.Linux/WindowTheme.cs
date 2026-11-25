using System.Runtime.InteropServices;
using System.Text;
using Spar.Hosting;
using Spar.Linux.Interop;

namespace Spar.Linux;

/// <summary>
/// The window's theme without flicker on Linux (plan section 3d): which scheme applies, then the host's layers for it.
/// The scheme is the app's saved choice (<see cref="SavedColorScheme"/>), else the system's from the XDG Settings
/// portal's <c>color-scheme</c>, read once before GTK starts and followed through its change signal, else, where no
/// portal answers (WSLg, a bare X session), GTK's own settings as GTK read them, before the host sets any. The layers: a
/// GTK CSS provider that paints the window in the colour of <c>spar.Window(w => w.Background(light, dark))</c>, GTK's
/// dark preference (<c>gtk-application-prefer-dark-theme</c>, which <c>prefers-color-scheme</c> follows), and an opaque
/// WebKit background in the same colour (a transparent one shows black). No theme name is set.
/// </summary>
internal sealed class WindowTheme
{
    /// <summary>The CSS class of the app's windows, which the provider's rule paints.</summary>
    public const string WindowClass = "spar-window";

    private const string PortalService = "org.freedesktop.portal.Desktop";
    private const string PortalPath = "/org/freedesktop/portal/desktop";
    private const string PortalSettings = "org.freedesktop.portal.Settings";
    private const string Appearance = "org.freedesktop.appearance";
    private const string ColorSchemeKey = "color-scheme";

    /// <summary>How long the start waits for a portal that is running but slow; one that is not running fails at once.</summary>
    private const int PortalMilliseconds = 250;

    private readonly WindowOptions _options;
    private bool _portalAnswered;
    private bool? _systemDark;
    private nint _bus;
    private uint _subscription;
    private nint _provider;
    private nint _display;
    private string _class = WindowClass;
    private bool _fixed;
    private static int s_fixed;
    private nint _window;
    private bool _x11;
    private GCHandle _self;
    private Action? _changed;

    private WindowTheme(WindowOptions options, bool? appChoosesDark, bool? portalDark, nint bus, string source)
    {
        _options = options;
        AppChoosesDark = appChoosesDark;
        IsDark = appChoosesDark ?? portalDark ?? false;
        _portalAnswered = portalDark is not null;
        _systemDark = portalDark;
        _bus = bus;
        Source = source;
    }

    /// <summary>The app's own choice (true dark, false light), or null when the window follows the system.</summary>
    public bool? AppChoosesDark { get; private set; }

    /// <summary>Whether the Settings portal answered, so the system's scheme is followed through its change signal.</summary>
    public bool HasPortal => _portalAnswered;

    /// <summary>Whether the window is dark now.</summary>
    public bool IsDark { get; private set; }

    /// <summary>Where the scheme came from, for the log.</summary>
    public string Source { get; private set; }

    /// <summary>The window colour now.</summary>
    public SparColor Background => IsDark ? _options.DarkBackground : _options.LightBackground;

    /// <summary>
    /// Reads the scheme before GTK starts (main thread, after the environment is set: the session bus's first use
    /// starts GDBus's thread): the saved choice from <paramref name="dataRoot"/>, else the portal's, which costs about
    /// 1–2 ms when it answers and fails at once when it is not running (it is never started for this).
    /// </summary>
    public static WindowTheme Read(WindowOptions options, string dataRoot)
    {
        if (SavedColorScheme.Read(dataRoot) is { } saved)
        {
            return new WindowTheme(options, saved, null, 0, "the app's saved choice");
        }

        var (bus, dark, note) = ReadPortal();
        return new WindowTheme(options, null, dark, bus, dark is null ? note : "the Settings portal");
    }

    /// <summary>
    /// A window test's fixed scheme (decision "Window tests: closing, session-end budget, test theme and any-character
    /// keys": light unless the test asks for dark), whatever the system's. Its CSS rule names a class of this window's
    /// own, and its provider leaves the display with the window, since a test process's windows share their displays.
    /// </summary>
    public static WindowTheme Fixed(WindowOptions options, bool dark)
    {
        var theme = new WindowTheme(options, dark, null, 0, "the test's colour scheme");
        theme._class = $"{WindowClass}-test-{Interlocked.Increment(ref s_fixed)}";
        theme._fixed = true;
        return theme;
    }

    /// <summary>
    /// The layers, on the loop thread after <c>gtk_init</c> and before <paramref name="window"/> is presented: GTK's
    /// settings read first when nothing else decided, then the dark preference and the window's colour.
    /// </summary>
    public void Apply(nint window)
    {
        if (!_portalAnswered)
        {
            // Read before the host sets any, also when the app chose: a later setColorScheme("system") needs them.
            var (dark, setting) = ReadGtkSettings();
            _systemDark = dark;
            if (AppChoosesDark is null)
            {
                IsDark = dark;
                Source = $"{Source}; GTK's settings ({setting})";
            }
        }

        _window = window;
        _display = Gtk.gtk_widget_get_display(window);
        Gtk.gtk_widget_add_css_class(window, _class);
        _provider = Gtk.gtk_css_provider_new();
        Gtk.gtk_style_context_add_provider_for_display(_display, _provider, Gtk.StyleProviderPriorityApplication);
        Paint();
    }

    /// <summary>
    /// Realizes the window just before it is presented (loop thread) and, on X11, gives its X window the theme's
    /// colour as its background, so the X server fills it from the moment it maps, before GTK's first paint. Without
    /// it, the window showed what was on the screen under it for about 6 ms under Xvfb (a white frame in dark mode).
    /// </summary>
    public void Realize()
    {
        X11.gtk_widget_realize(_window);
        _x11 = true;
        PaintX11();
    }

    /// <summary>
    /// Follows the portal's changes while the window follows the system (loop thread: the signal arrives on the
    /// thread-default context of the subscriber). <paramref name="changed"/> repaints what the window holds.
    /// </summary>
    public void Follow(Action changed)
    {
        _changed = changed;
        if (AppChoosesDark is null)
        {
            Subscribe();
        }
    }

    /// <summary>
    /// The system's scheme from a portal read off the loop (<see cref="ReadPortalScheme"/>), before
    /// <see cref="Choose"/>(null) when no portal answered at start: the bus is kept, so its changes are followed from now
    /// on (loop thread). Takes the bus's reference.
    /// </summary>
    public void AdoptPortal(nint bus, bool? dark)
    {
        if (bus == 0 || dark is null || _bus != 0)
        {
            if (bus != 0)
            {
                GLib.g_object_unref(bus);
            }

            return;
        }

        _bus = bus;
        _portalAnswered = true;
        _systemDark = dark;
    }

    /// <summary>
    /// The page's <c>setColorScheme</c> while the app runs (loop thread): the app's choice from now on, or the system's
    /// again for null, followed through the portal when one answers. The window's layers follow at once; true when its
    /// scheme changed, so the caller repaints what the window holds (the view's background).
    /// </summary>
    public bool Choose(bool? dark)
    {
        AppChoosesDark = dark;
        if (dark is null)
        {
            Subscribe();
        }

        var now = dark ?? _systemDark ?? false;
        if (now == IsDark)
        {
            return false;
        }

        IsDark = now;
        Paint();
        return true;
    }

    /// <summary>The bus and the portal's scheme, read off the loop: the portal call may wait up to 250 ms.</summary>
    public static (nint Bus, bool? Dark) ReadPortalScheme()
    {
        var (bus, dark, _) = ReadPortal();
        return (bus, dark);
    }

    /// <summary>Stops following the system (loop thread, at the teardown).</summary>
    public void Close()
    {
        if (_subscription != 0)
        {
            GLib.g_dbus_connection_signal_unsubscribe(_bus, _subscription);
            _subscription = 0;
        }

        if (SparTestSession.IsSupported && _fixed && _provider != 0 && _display != 0)
        {
            Gtk.gtk_style_context_remove_provider_for_display(_display, _provider);
        }
    }

    /// <summary>Frees what native code found this theme by, once the main loop has ended.</summary>
    public void Release()
    {
        Close();
        if (_self.IsAllocated)
        {
            _self.Free();
        }

        if (_bus != 0)
        {
            GLib.g_object_unref(_bus);
            _bus = 0;
        }

        if (_provider != 0)
        {
            GLib.g_object_unref(_provider);
            _provider = 0;
        }
    }

    /// <summary>The portal's value: 1 dark; 0 (no preference) and 2 light.</summary>
    internal static bool? DarkFromPortal(uint value) => value switch
    {
        0 or 2 => false,
        1 => true,
        _ => null,
    };

    /// <summary>A GTK theme name that names a dark variant.</summary>
    internal static bool IsDarkThemeName(string? name) =>
        name is not null && (name.EndsWith("-dark", StringComparison.OrdinalIgnoreCase) || name.EndsWith(":dark", StringComparison.OrdinalIgnoreCase));

    /// <summary>The CSS rule that paints the app's windows.</summary>
    internal static string Css(SparColor colour) => Css(WindowClass, colour);

    /// <summary>The CSS rule that paints the windows of one CSS class.</summary>
    internal static string Css(string cssClass, SparColor colour) => $"window.{cssClass} {{ background-color: {colour}; }}";

    /// <summary>Subscribes to the portal's change signal once, when a bus and a repaint callback exist (loop thread).</summary>
    private unsafe void Subscribe()
    {
        if (_bus == 0 || _subscription != 0 || _changed is null)
        {
            return;
        }

        if (!_self.IsAllocated)
        {
            _self = GCHandle.Alloc(this);
        }

        _subscription = GLib.g_dbus_connection_signal_subscribe(
            _bus,
            PortalService,
            PortalSettings,
            "SettingChanged",
            PortalPath,
            Appearance,
            0,
            &OnSettingChanged,
            GCHandle.ToIntPtr(_self),
            0);
    }

    private unsafe void Paint()
    {
        var settings = Gtk.gtk_settings_get_for_display(_display);
        if (settings != 0)
        {
            GValue value = default;
            GLib.g_value_init(&value, GLib.TypeBoolean);
            GLib.g_value_set_boolean(&value, IsDark ? 1 : 0);
            GLib.g_object_set_property(settings, "gtk-application-prefer-dark-theme", &value);
            GLib.g_value_unset(&value);
        }

        var css = Encoding.UTF8.GetBytes(Css(_class, Background));
        fixed (byte* data = css)
        {
            Gtk.gtk_css_provider_load_from_data(_provider, data, css.Length);
        }

        PaintX11();
    }

    /// <summary>The X window's background pixel in the current colour, when the window is realized on X11.</summary>
    private unsafe void PaintX11()
    {
        if (!_x11)
        {
            return;
        }

        try
        {
            var surface = X11.gtk_native_get_surface(_window);
            if (surface == 0 || X11.g_type_check_instance_is_a(surface, X11.gdk_x11_surface_get_type()) == 0)
            {
                _x11 = false;
                return;
            }

            var display = X11.gdk_x11_display_get_xdisplay(X11.gdk_surface_get_display(surface));
            var window = X11.gdk_x11_surface_get_xid(surface);
            var attributes = stackalloc byte[256];
            var depth = X11.XGetWindowAttributes(display, window, attributes) != 0 ? *(int*)(attributes + 20) : 24;

            // TrueColor, as every visual GTK 4 picks: 0xRRGGBB, with an opaque alpha byte on a 32-bit (ARGB) visual.
            var colour = Background;
            var pixel = ((uint)colour.Red << 16) | ((uint)colour.Green << 8) | colour.Blue | (depth == 32 ? 0xFF000000 : 0);
            X11.XSetWindowBackground(display, window, pixel);
        }
        catch (EntryPointNotFoundException)
        {
            // A GTK built without its X11 backend: nothing to fill.
            _x11 = false;
        }
    }

    private unsafe void SettingChanged(nint parameters)
    {
        // (namespace, key, value): only the appearance namespace was subscribed.
        if (Unwrap(GLib.g_variant_get_child_value(parameters, 2)) is not { } value || DarkFromPortal(value) is not { } dark)
        {
            return;
        }

        var key = GLib.g_variant_get_child_value(parameters, 1);
        var name = Marshal.PtrToStringUTF8(GLib.g_variant_get_string(key, null));
        GLib.g_variant_unref(key);
        if (name != ColorSchemeKey)
        {
            return;
        }

        // Kept while the app's own choice holds, for a later setColorScheme("system").
        _systemDark = dark;
        if (AppChoosesDark is not null || dark == IsDark)
        {
            return;
        }

        IsDark = dark;
        Paint();
        _changed?.Invoke();
    }

    /// <summary>GTK's dark preference or a dark theme name, as GTK read them from the system (never values the host set).</summary>
    private static unsafe (bool Dark, string Setting) ReadGtkSettings()
    {
        var settings = Gtk.gtk_settings_get_default();
        if (settings == 0)
        {
            return (false, "none");
        }

        GValue prefer = default;
        GLib.g_value_init(&prefer, GLib.TypeBoolean);
        GLib.g_object_get_property(settings, "gtk-application-prefer-dark-theme", &prefer);
        var prefersDark = GLib.g_value_get_boolean(&prefer) != 0;
        GLib.g_value_unset(&prefer);

        GValue theme = default;
        GLib.g_value_init(&theme, GLib.TypeString);
        GLib.g_object_get_property(settings, "gtk-theme-name", &theme);
        var name = Marshal.PtrToStringUTF8(GLib.g_value_get_string(&theme));
        GLib.g_value_unset(&theme);

        return (prefersDark || IsDarkThemeName(name), $"prefer-dark {prefersDark}, theme {name ?? "none"}");
    }

    /// <summary>The session bus and the portal's scheme, or no bus and why not.</summary>
    private static unsafe (nint Bus, bool? Dark, string Note) ReadPortal()
    {
        nint error = 0;
        var bus = GLib.g_bus_get_sync(GLib.BusSession, 0, &error);
        if (bus == 0)
        {
            var message = GLib.ErrorMessage(error);
            GLib.g_error_free(error);
            return (0, null, $"no session bus ({message})");
        }

        // ReadOne since portal 1.15; Read, whose value is wrapped once more, before it.
        var (value, failure) = CallPortal(bus, "ReadOne");
        if (value is null)
        {
            (value, var older) = CallPortal(bus, "Read");
            failure = value is null ? $"{failure}; {older}" : failure;
        }

        if (value is { } scheme && DarkFromPortal(scheme) is { } dark)
        {
            return (bus, dark, "");
        }

        GLib.g_object_unref(bus);
        return (0, null, $"no Settings portal ({failure})");
    }

    private static unsafe (uint? Value, string Failure) CallPortal(nint bus, string method)
    {
        var children = stackalloc nint[2];
        children[0] = GLib.g_variant_new_string(Appearance);
        children[1] = GLib.g_variant_new_string(ColorSchemeKey);
        var parameters = GLib.g_variant_new_tuple(children, 2);
        nint error = 0;
        var result = GLib.g_dbus_connection_call_sync(
            bus, PortalService, PortalPath, PortalSettings, method, parameters, 0, GLib.CallNoAutoStart, PortalMilliseconds, 0, &error);
        if (result == 0)
        {
            var message = GLib.ErrorMessage(error);
            GLib.g_error_free(error);
            return (null, message);
        }

        var value = Unwrap(GLib.g_variant_get_child_value(result, 0));
        GLib.g_variant_unref(result);
        return value is null ? (null, "not a number") : (value, "");
    }

    /// <summary>Takes an owned variant, unwraps nested <c>v</c> values, and returns its <c>u</c>, or null.</summary>
    private static unsafe uint? Unwrap(nint variant)
    {
        var variantType = stackalloc byte[] { (byte)'v', 0 };
        var unsignedType = stackalloc byte[] { (byte)'u', 0 };
        while (variant != 0 && GLib.g_variant_is_of_type(variant, variantType) != 0)
        {
            var inner = GLib.g_variant_get_variant(variant);
            GLib.g_variant_unref(variant);
            variant = inner;
        }

        if (variant == 0)
        {
            return null;
        }

        try
        {
            return GLib.g_variant_is_of_type(variant, unsignedType) != 0 ? GLib.g_variant_get_uint32(variant) : null;
        }
        finally
        {
            GLib.g_variant_unref(variant);
        }
    }

    [UnmanagedCallersOnly]
    private static void OnSettingChanged(nint connection, nint sender, nint path, nint interfaceName, nint signal, nint parameters, nint data)
    {
        try
        {
            if (GCHandle.FromIntPtr(data).Target is WindowTheme theme)
            {
                theme.SettingChanged(parameters);
            }
        }
        catch (Exception exception)
        {
            // Nothing may unwind into GLib.
            Console.Error.WriteLine("spar fail: a theme change failed: " + exception.Message);
        }
    }
}
