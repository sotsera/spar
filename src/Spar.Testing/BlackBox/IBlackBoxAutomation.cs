namespace Spar.Testing;

/// <summary>
/// The accessibility client the fixture drives a launched app through, as platform-neutral operations: on Windows UI
/// Automation (<see cref="AutomationHost"/>), on Linux AT-SPI with its host (M1c). Each operation reads the app as it
/// is now and keeps nothing of its tree.
/// </summary>
internal interface IBlackBoxAutomation
{
    /// <summary>Where the app's window goes: a desktop name for process creation, or null for the test process's own.</summary>
    string? Desktop { get; }

    /// <summary>The main window's title; null when the app has no window.</summary>
    Task<string?> TitleAsync(int processId, CancellationToken cancellationToken = default);

    /// <summary>The elements <paramref name="target"/> finds in the main window; empty when the app has no window.</summary>
    Task<IReadOnlyList<FoundElement>> FindAsync(int processId, ElementTarget target, CancellationToken cancellationToken = default);

    /// <summary>Invokes the one element <paramref name="target"/> finds, or says why it could not.</summary>
    Task<ElementOutcome> InvokeAsync(int processId, ElementTarget target, CancellationToken cancellationToken = default);

    /// <summary>Reads the value of the one element <paramref name="target"/> finds, or says why it could not.</summary>
    Task<ElementOutcome> ValueAsync(int processId, ElementTarget target, CancellationToken cancellationToken = default);

    /// <summary>Asks the main window to close, as its close button does; false when the app has no window.</summary>
    Task<bool> CloseWindowAsync(int processId, CancellationToken cancellationToken = default);

    /// <summary>The app's first native dialog, read whole; null while it shows none.</summary>
    Task<DialogContent?> FindDialogAsync(int processId, CancellationToken cancellationToken = default);

    /// <summary>Presses the dialog's button named <paramref name="button"/>; false when the dialog or the button is gone.</summary>
    Task<bool> PressAsync(DialogContent dialog, string button, CancellationToken cancellationToken = default);

    /// <summary>The screen rectangle of the one element <paramref name="target"/> finds (<see cref="ElementOutcome.Bounds"/>).</summary>
    Task<ElementOutcome> BoundsAsync(int processId, ElementTarget target, CancellationToken cancellationToken = default);

    /// <summary>Gives the one element <paramref name="target"/> finds the keyboard focus, through the accessibility API (no input).</summary>
    Task<ElementOutcome> FocusAsync(int processId, ElementTarget target, CancellationToken cancellationToken = default);

    /// <summary>The main window's rectangle, its title bar's, and whether it has the foreground; null when the app has no window.</summary>
    Task<WindowParts?> WindowPartsAsync(int processId, CancellationToken cancellationToken = default);

    /// <summary>Every visible top-level window of <paramref name="processIds"/> (the app and the browser it started), read whole.</summary>
    Task<IReadOnlyList<TopLevelWindow>> TopLevelWindowsAsync(IReadOnlyList<int> processIds, CancellationToken cancellationToken = default);

    /// <summary>The rectangle of the dialog's button named <paramref name="button"/>; null when the dialog or the button is gone.</summary>
    Task<ScreenRect?> DialogButtonBoundsAsync(DialogContent dialog, string button, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ends the app's session as Windows does (<c>WM_QUERYENDSESSION</c>, then <c>WM_ENDSESSION</c> with <c>TRUE</c>, for a
    /// log-off), to its main window; returns once the window procedure returned <c>WM_ENDSESSION</c>, false when the app
    /// has no window. For Spar's own tests of the session end on a Production executable.
    /// </summary>
    Task<bool> EndSessionAsync(int processId, CancellationToken cancellationToken = default);

    /// <summary>A text dump of every visible top-level window of <paramref name="processIds"/> and the elements under it, for the evidence.</summary>
    Task<string> DumpAsync(IReadOnlyList<int> processIds, CancellationToken cancellationToken = default);
}

/// <summary>A rectangle on the screen, in physical pixels.</summary>
internal readonly record struct ScreenRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;

    public int Height => Bottom - Top;

    public bool IsEmpty => Width <= 0 || Height <= 0;

    public (int X, int Y) Center => (Left + (Width / 2), Top + (Height / 2));

    public override string ToString() => $"[{Left},{Top},{Right},{Bottom}]";
}

/// <summary>A main window as real input needs it: its handle, its rectangle, its title bar's, and whether it is the foreground window.</summary>
internal sealed record WindowParts(nint Handle, ScreenRect Bounds, ScreenRect? TitleBar, bool IsForeground);

/// <summary>
/// A visible top-level window of the app's process tree: its process, class, title and control type, and the names of
/// the menu items under it (a context menu's).
/// </summary>
internal sealed record TopLevelWindow(int ProcessId, nint Handle, string ClassName, string Title, int ControlType, IReadOnlyList<string> MenuItems);

/// <summary>What a locator looks for, and which of the matches it takes (null: all; -1: the last).</summary>
internal sealed record ElementTarget(BlackBoxQuery Query, int? Index);

/// <summary>One element as an operation found it.</summary>
internal readonly record struct FoundElement(string Name, bool IsEnabled, bool IsOffscreen);

/// <summary>How an operation on one element ended.</summary>
internal enum ElementStatus
{
    Done,
    NotFound,
    Ambiguous,
    Disabled,
    Unsupported,
}

/// <summary>
/// An operation's outcome: its status, the value it read, the names of the elements found when they were several, and
/// the element's rectangle when the operation read it.
/// </summary>
internal sealed record ElementOutcome(ElementStatus Status, string? Value = null, IReadOnlyList<string>? Names = null, ScreenRect? Bounds = null);

/// <summary>A native dialog as it was read: its window, title, text parts and own buttons.</summary>
/// <param name="Native">The accessibility client's own reference to the dialog (Linux: its AT-SPI node), when it has no window handle.</param>
internal sealed record DialogContent(nint Handle, string Title, IReadOnlyList<string> Texts, IReadOnlyList<string> Buttons, object? Native = null);

/// <summary>
/// What a black-box locator matches, independent of the accessibility API: a role (its ARIA name, and the native
/// control type that stands for it) or a text element, an accessible name and the checks applied after the search.
/// </summary>
internal sealed record BlackBoxQuery(
    string Description,
    string? AriaRole,
    int? NativeType,
    bool TextOnly,
    string? Name,
    bool Exact,
    System.Text.RegularExpressions.Regex? NameRegex,
    bool? Disabled)
{
    public static BlackBoxQuery ByRole(SparRole role, SparGetByRoleOptions? options)
    {
        if (options is { Checked: not null } or { Expanded: not null } or { Level: not null }
            or { Pressed: not null } or { Selected: not null } or { IncludeHidden: true })
        {
            throw new NotSupportedException(
                "A black-box GetByRole matches the role, the name and Disabled; Checked, Expanded, Level, Pressed, Selected and " +
                "IncludeHidden are not read through UI Automation.");
        }

        var name = (options?.Name, options?.NameRegex) switch
        {
            ({ } text, _) => $", Name = \"{text}\"",
            (_, { } regex) => $", NameRegex = /{regex}/",
            _ => "",
        };
        return new BlackBoxQuery(
            $"GetByRole({role}{name})",
            role.ToString().ToLowerInvariant(),
            NativeControlTypes.For(role),
            TextOnly: false,
            options?.Name,
            options?.Exact == true,
            options?.NameRegex,
            options?.Disabled);
    }

    public static BlackBoxQuery ByText(string text, SparGetByTextOptions? options)
    {
        ArgumentNullException.ThrowIfNull(text);
        return new BlackBoxQuery($"GetByText(\"{text}\")", null, null, TextOnly: true, text, options?.Exact == true, null, null);
    }
}

/// <summary>
/// The UI Automation control type a native element of a role has (UIAutomationClient.h). The browser gives each page
/// element its ARIA role (<c>heading</c>, <c>button</c>, <c>navigation</c>, measured in <c>docs/evidence/blackbox-desktop</c>); a
/// native element has none, so a role also matches the control type that stands for it (a dialog's buttons).
/// </summary>
internal static class NativeControlTypes
{
    public const int Button = 50000;
    public const int Text = 50020;
    public const int Window = 50032;
    public const int TitleBar = 50037;

    public static int? For(SparRole role) => role switch
    {
        SparRole.Button => Button,
        SparRole.Checkbox => 50002,
        SparRole.Combobox => 50003,
        SparRole.Textbox => 50004,
        SparRole.Link => 50005,
        SparRole.Img => 50006,
        SparRole.Listitem => 50007,
        SparRole.List => 50008,
        SparRole.Menu => 50009,
        SparRole.Menubar => 50010,
        SparRole.Menuitem => 50011,
        SparRole.Progressbar => 50012,
        SparRole.Radio => 50013,
        SparRole.Scrollbar => 50014,
        SparRole.Slider => 50015,
        SparRole.Spinbutton => 50016,
        SparRole.Status => 50017,
        SparRole.Tablist => 50018,
        SparRole.Tab => 50019,
        SparRole.Toolbar => 50021,
        SparRole.Tooltip => 50022,
        SparRole.Tree => 50023,
        SparRole.Treeitem => 50024,
        SparRole.Group => 50026,
        SparRole.Grid => 50028,
        SparRole.Document => 50030,
        SparRole.Table => 50036,
        SparRole.Separator => 50038,
        _ => null,
    };
}
