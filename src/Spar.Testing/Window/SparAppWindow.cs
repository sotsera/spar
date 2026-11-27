namespace Spar.Testing;

/// <summary>
/// A window the app's page opened, of one of the kinds <c>Program.cs</c> declares (<c>windows.open(kind, params)</c>,
/// <c>docs/app-model.md</c> 6.7), for the Window surface: its page to drive, as <see cref="SparTestApp.Page"/> is the
/// main window's, and the window to close. Every window of the test app shares its browser profile, as an app's do.
/// </summary>
public sealed class SparAppWindow
{
    internal SparAppWindow(string kind, string id, SparPage page, SparWindow window)
    {
        Kind = kind;
        Id = id;
        Page = page;
        Window = window;
    }

    /// <summary>The window's kind, as <c>Program.cs</c> names it.</summary>
    public string Kind { get; }

    /// <summary>The window's id in the app: its kind's name with a number, <c>settings-1</c> for the first.</summary>
    public string Id { get; }

    /// <summary>The page in the window: locators, trusted input, screenshots and assertions.</summary>
    public SparPage Page { get; }

    /// <summary>The window: <c>CloseAsync</c> closes it through its close request, as its close button does.</summary>
    public SparWindow Window { get; }

    public override string ToString() => Id;
}
