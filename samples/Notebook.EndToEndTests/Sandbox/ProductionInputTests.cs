using System.Runtime.Versioning;
using Spar;
using Spar.Testing;

namespace Notebook.EndToEndTests;

/// <summary>
/// The Production executable's input defaults with real input (decision "Security defaults (implementation record;
/// choices for review)", checked here in step 8c): the devtools keys, the browser's own keys and the mouse's back and
/// forward buttons do nothing, and the context menu is cut down to a text field's Cut, Copy, Paste and Select all, with
/// none elsewhere. Each key is pressed with the "New page" dialog open and a title typed: a reload or a navigation would
/// lose both, a zoom would move the dialog, and a devtools window, a print preview or a find bar would add a window or
/// elements the dump did not have. The keyboard is shown to reach the page before and after.
/// </summary>
[Trait(SandboxLane.Trait, "UserSession")]
[SupportedOSPlatform("windows10.0.17763")]
public sealed class ProductionInputTests
{
    /// <summary>Keys a browser acts on; the Production app takes none of them.</summary>
    private static readonly string[] BrowserKeys =
    [
        "F12", "Control+Shift+I", "Control+Shift+J", "Control+Shift+C", // devtools
        "Control+P", "Control+F", "F3", "Control+G", "Control+U", "Control+S", "Control+O", "F7", // print, find, source, save, open, caret
        "F5", "Control+R", "Control+Shift+R", "Control+F5", // reload
        "Control+=", "Control+-", "Control+0", // zoom
        "Control+N", "Control+T", "Control+Shift+N", // new window, tab, private window
        "Alt+ArrowLeft", "Alt+ArrowRight", "Alt+Home", // history
    ];

    /// <summary>Names that appear in a dump only when the browser opened something of its own.</summary>
    private static readonly string[] BrowserUi = ["DevTools", "Print", "Find on page", "Save as", "Zoom", "Caret browsing"];

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact(Explicit = true)]
    public async Task Devtools_and_browser_keys_and_the_mouse_history_buttons_do_nothing()
    {
        SandboxLane.SkipUnlessUserSession();
        await using var app = await SparBlackBox.LaunchAsync(Projects.Notebook, cancellationToken: Token);
        var title = await OpenNewPageDialogAsync(app);
        await title.TypeAsync("Draft");
        await WaitForValueAsync(title, "Draft");
        var heading = app.Window.GetByRole(SparRole.Heading, new() { Name = "New page", Exact = true });
        var baseline = await SnapshotAsync(app, heading, title);
        var baselineDump = await SandboxLane.EvidenceAsync(app, "input-baseline");

        var failures = new List<string>();
        foreach (var key in BrowserKeys)
        {
            await title.FocusAsync();
            await app.Window.PressAsync(key);
            failures.AddRange(await CompareAsync(app, key, baseline, baselineDump, heading, title));
        }

        foreach (var button in new[] { RealMouseButton.Back, RealMouseButton.Forward })
        {
            await heading.ClickAsync(button);
            failures.AddRange(await CompareAsync(app, $"mouse {button}", baseline, baselineDump, heading, title));
        }

        // The keyboard still reaches the page: whatever was pressed above, typing still types.
        await title.TypeAsync("x");
        await WaitForValueAsync(title, "Draftx");
        Assert.True(failures.Count == 0, string.Join("\n", failures));
        SandboxLane.Note($"{BrowserKeys.Length} browser keys and 2 mouse buttons did nothing");
    }

    [Fact(Explicit = true)]
    public async Task A_text_fields_menu_has_only_Cut_Copy_Paste_and_Select_all_and_there_is_none_elsewhere()
    {
        SandboxLane.SkipUnlessUserSession();
        await using var app = await SparBlackBox.LaunchAsync(Projects.Notebook, cancellationToken: Token);
        var search = app.Window.GetByRole(SparRole.Searchbox, new() { Name = "Search pages" });

        await search.ClickAsync(RealMouseButton.Right);
        var menu = await MenuAsync(app, TimeSpan.FromSeconds(5));
        await SandboxLane.EvidenceAsync(app, "menu-text-field");
        Assert.NotNull(menu);
        // An item's name may carry its access key (&) or its shortcut after a tab.
        var items = menu.MenuItems.Select(item => item.Split('\t')[0].Replace("&", "", StringComparison.Ordinal).Trim()).ToList();
        SandboxLane.Note($"text field menu as read: {string.Join(" | ", menu.MenuItems)}");
        Assert.Equal(["Cut", "Copy", "Paste", "Select all"], items);
        await app.Window.PressAsync("Escape");
        Assert.Null(await MenuAsync(app, TimeSpan.FromSeconds(2), present: false));

        await app.Window.GetByRole(SparRole.Heading, new() { Name = "Start your notebook" }).ClickAsync(RealMouseButton.Right);
        var elsewhere = await MenuAsync(app, TimeSpan.FromSeconds(2));
        await SandboxLane.EvidenceAsync(app, "menu-elsewhere");
        Assert.Null(elsewhere);
        SandboxLane.Note($"text field menu: {string.Join(", ", menu.MenuItems)}; elsewhere: none");
    }

    /// <summary>Opens the "New page" dialog with a real click and returns its title field, which has the focus.</summary>
    internal static async Task<SparBlackBoxLocator> OpenNewPageDialogAsync(SparBlackBoxApp app)
    {
        await app.Window.GetByRole(SparRole.Button, new() { Name = "New page", Exact = true }).First.ClickAsync();
        await app.Window.GetByRole(SparRole.Dialog, new() { Name = "New page" }).WaitForAsync();
        return app.Window.GetByRole(SparRole.Textbox, new() { Name = "Title", Exact = true });
    }

    private static async Task WaitForValueAsync(SparBlackBoxLocator field, string value)
    {
        var limit = DateTime.UtcNow.AddSeconds(5);
        string current;
        while ((current = await field.InputValueAsync()) != value)
        {
            Assert.True(DateTime.UtcNow < limit, $"The field reads '{current}', not '{value}'.");
            await Task.Delay(50, Token);
        }
    }

    /// <summary>What a key must leave as it was: the windows of the app's processes, the dialog's place and the field's value.</summary>
    private static async Task<Snapshot> SnapshotAsync(SparBlackBoxApp app, SparBlackBoxLocator heading, SparBlackBoxLocator title)
    {
        var windows = (await app.Window.TopLevelWindowsAsync()).Select(window => $"{window.ClassName} \"{window.Title}\"").Order().ToList();
        var bounds = await heading.CountAsync() == 1 ? (await heading.BoundsAsync()).ToString() : "(no dialog)";
        var value = await title.CountAsync() == 1 ? await title.InputValueAsync() : "(no field)";
        return new Snapshot(string.Join(" | ", windows), bounds, value);
    }

    private static async Task<List<string>> CompareAsync(
        SparBlackBoxApp app,
        string what,
        Snapshot baseline,
        string baselineDump,
        SparBlackBoxLocator heading,
        SparBlackBoxLocator title)
    {
        // Long enough for a devtools window, a print preview or a reload to show.
        await Task.Delay(800, Token);
        var now = await SnapshotAsync(app, heading, title);
        var dump = await app.Window.DumpAsync();
        var problems = new List<string>();
        if (now.Windows != baseline.Windows)
        {
            problems.Add($"{what}: the windows changed from [{baseline.Windows}] to [{now.Windows}]");
        }

        if (now.DialogBounds != baseline.DialogBounds)
        {
            problems.Add($"{what}: the dialog's heading moved from {baseline.DialogBounds} to {now.DialogBounds} (zoom, reload or navigation)");
        }

        if (now.Value != baseline.Value)
        {
            problems.Add($"{what}: the title field reads '{now.Value}', not '{baseline.Value}'");
        }

        foreach (var name in BrowserUi.Where(name => dump.Contains(name, StringComparison.Ordinal) && !baselineDump.Contains(name, StringComparison.Ordinal)))
        {
            problems.Add($"{what}: the browser showed '{name}'");
        }

        if (problems.Count > 0)
        {
            await SandboxLane.EvidenceAsync(app, "input-" + string.Concat(what.Where(char.IsLetterOrDigit)));
        }

        SandboxLane.Note($"{what}: {(problems.Count == 0 ? "nothing happened" : string.Join("; ", problems))}");
        return problems;
    }

    /// <summary>The first window of the app's processes with menu items, waited for (or, with <paramref name="present"/> false, waited away).</summary>
    private static async Task<TopLevelWindow?> MenuAsync(SparBlackBoxApp app, TimeSpan limit, bool present = true)
    {
        var end = DateTime.UtcNow + limit;
        while (true)
        {
            // The browser's windows only: the app's own window has its title bar's system menu.
            var menu = (await app.Window.TopLevelWindowsAsync())
                .FirstOrDefault(window => window.ProcessId != app.ProcessId && window.MenuItems.Count > 0);
            if ((menu is not null) == present || DateTime.UtcNow >= end)
            {
                return menu;
            }

            await Task.Delay(100, Token);
        }
    }

    private sealed record Snapshot(string Windows, string DialogBounds, string Value);
}
