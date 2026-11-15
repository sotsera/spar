namespace Spar;

public sealed class WindowOptions
{
    internal WindowOptions()
    {
    }

    internal string? LightBackground { get; private set; }

    internal string? DarkBackground { get; private set; }

    internal TitleBar TitleBarKind { get; private set; }

    internal bool HidesToTray { get; private set; }

    internal string? PageRoute { get; private set; }

    internal (bool Enabled, double Min, double Max) ZoomRange { get; private set; } = (true, 0.25, 5);

    internal bool FindBar { get; private set; }

    internal bool CaptureProtected { get; private set; }

    /// <summary>Hides the window from screenshots, recordings and screen sharing where the OS allows it (Windows 10 2004+).
    /// Not a security boundary: it reduces casual leaks only. No effect on Linux.</summary>
    public WindowOptions ProtectFromCapture()
    {
        CaptureProtected = true;
        return this;
    }

    /// <summary>Zoom is on by default (Ctrl+wheel, Ctrl+plus, minus and 0) and saved with the placement.</summary>
    public WindowOptions Zoom(bool enabled = true, double min = 0.25, double max = 5)
    {
        ZoomRange = (enabled, min, max);
        return this;
    }

    /// <summary>Turns on Ctrl+F with the SDK's find bar; off by default so the app's own Ctrl+F can be an action.</summary>
    public WindowOptions FindInPage()
    {
        FindBar = true;
        return this;
    }

    internal bool IsModal { get; private set; }

    internal bool IsSingleInstance { get; private set; }

    internal List<Type> AllowedContracts { get; } = [];

    /// <summary>The page route this window shows, from the same frontend.</summary>
    public WindowOptions Route(string route)
    {
        PageRoute = route;
        return this;
    }

    /// <summary>Disables the owner while open and stays above it.</summary>
    public WindowOptions Modal()
    {
        IsModal = true;
        return this;
    }

    /// <summary>At most one window of this kind; opening it again focuses the existing one.</summary>
    public WindowOptions SingleInstance()
    {
        IsSingleInstance = true;
        return this;
    }

    /// <summary>An allow-list of the contracts this window may call; without one, every command is allowed.</summary>
    public WindowOptions Commands(Action<CommandAllowList> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        configure(new CommandAllowList(AllowedContracts));
        return this;
    }

    internal (int Width, int Height) InitialSize { get; private set; } = (1024, 768);

    internal (int Width, int Height) Minimum { get; private set; }

    internal bool RemembersPlacement { get; private set; } = true;

    /// <summary>The size at the first start, centred on the primary screen.</summary>
    public WindowOptions Size(int width, int height)
    {
        InitialSize = (width, height);
        return this;
    }

    public WindowOptions MinSize(int width, int height)
    {
        Minimum = (width, height);
        return this;
    }

    /// <summary>Saves size, position, maximized state and screen, and restores them on screen at the next start. On by default for the main window; Wayland restores size and maximized state only.</summary>
    public WindowOptions RememberPlacement(bool remember = true)
    {
        RemembersPlacement = remember;
        return this;
    }

    /// <summary>Closing hides the window to the tray instead of quitting; where no tray exists it minimises.</summary>
    public WindowOptions CloseToTray()
    {
        HidesToTray = true;
        return this;
    }

    /// <summary>The window colour before the page paints, for each theme.</summary>
    public WindowOptions Background(string light, string dark)
    {
        LightBackground = light;
        DarkBackground = dark;
        return this;
    }

    /// <summary>The system title bar (the default), or one the page draws.</summary>
    public WindowOptions TitleBar(TitleBar kind)
    {
        TitleBarKind = kind;
        return this;
    }
}

/// <summary>Who draws the window's title bar.</summary>
public enum TitleBar
{
    /// <summary>The operating system's title bar.</summary>
    System,

    /// <summary>The page draws it: <c>data-spar-titlebar</c> marks the drag area, <c>data-spar-caption</c> the buttons.</summary>
    Custom,
}

public sealed class CspBuilder
{
    private readonly List<string> _fontSources = [];

    internal CspBuilder()
    {
    }

    internal IReadOnlyList<string> FontSources => _fontSources;

    /// <summary>Adds sources to <c>font-src</c> on top of the strict default.</summary>
    public CspBuilder FontSrc(params string[] sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        _fontSources.AddRange(sources);
        return this;
    }
}

public sealed class PermissionsBuilder
{
    private readonly List<Permission> _allowed = [];

    internal PermissionsBuilder()
    {
    }

    internal IReadOnlyList<Permission> Allowed => _allowed;

    public PermissionsBuilder Allow(Permission permission)
    {
        _allowed.Add(permission);
        return this;
    }
}

/// <summary>Powerful browser features, denied unless allowed.</summary>
public enum Permission
{
    ClipboardRead,
}

/// <summary>The contracts a window may call.</summary>
public sealed class CommandAllowList
{
    private readonly List<Type> _allowed;

    internal CommandAllowList(List<Type> allowed)
    {
        _allowed = allowed;
    }

    public CommandAllowList Allow<TContract>()
    {
        _allowed.Add(typeof(TContract));
        return this;
    }
}
