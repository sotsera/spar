using Spar.Testing;
using static Spar.Testing.SparAssertions;

namespace Spar.WindowTests;

/// <summary>
/// Files in a real window (decisions "M3c's decide-first questions", items 2, 3 and 7, and "The drop design, from the
/// file-drop spike"): files dropped from the OS on a drop area become grants the backend resolves; a drop elsewhere
/// stays refused; an open dialog's file is read by the page. The drop goes through each engine's own drag and drop
/// (the DevTools Protocol's drag events on WebView2, an XDND drag on the test display on Linux); the dialogs are the
/// test host's.
/// </summary>
[Trait("Surface", "Window")]
public sealed class FileWindowTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Files_dropped_on_an_area_become_grants_the_backend_reads()
    {
        await using var host = await HostPage.OpenAsync();
        var folder = Folder(host);
        var notes = Write(folder, "notes.md", "# notes");
        var todo = Write(folder, "todo.txt", "buy milk");

        await host.Page.GetByRole(SparRole.Region, new() { Name = "Drop Markdown here" }).DropFilesAsync([notes, todo]);

        await Expect(Dropped(host)).ToHaveTextAsync("Dropped notes.md 7, todo.txt 8");
        host.AssertClean();
    }

    [Fact]
    public async Task A_file_the_area_does_not_take_says_why()
    {
        await using var host = await HostPage.OpenAsync();
        var csv = Write(Folder(host), "data.csv", "1,2");

        await host.Page.GetByRole(SparRole.Region, new() { Name = "Drop Markdown here" }).DropFilesAsync([csv]);

        await Expect(Dropped(host)).ToHaveTextAsync("Not taken: data.csv");
        host.AssertClean();
    }

    [Fact]
    public async Task A_drop_outside_every_area_changes_nothing()
    {
        await using var host = await HostPage.OpenAsync();
        var notes = Write(Folder(host), "notes.md", "# notes");
        var before = await host.Page.EvaluateAsync<string>("location.href");

        await host.Page.GetByRole(SparRole.Heading, new() { Name = "Files" }).DropFilesAsync([notes]).ContinueWith(_ => { }, Token);
        await Task.Delay(500, Token);

        Assert.Equal(before, await host.Page.EvaluateAsync<string>("location.href"));
        await Expect(Dropped(host)).ToHaveTextAsync("No drop yet");
        await Expect(host.Status).ToHaveTextAsync("Ready");
    }

    [Fact]
    public async Task An_open_dialogs_file_is_read_by_the_page()
    {
        await using var host = await HostPage.OpenAsync();
        var notes = Write(Folder(host), "notes.md", "# from the dialog");
        host.App.FileDialogs.Choose(notes);

        await host.Page.GetByRole(SparRole.Button, new() { Name = "Open a file" }).ClickAsync();

        await Expect(Dropped(host)).ToHaveTextAsync("Opened notes.md: # from the dialog");
        Assert.Equal("Open notes", Assert.Single(host.App.FileDialogs.Shown).Title);
        host.AssertClean();
    }

    private static SparLocator Dropped(HostPage host) => host.Page.Locator("#dropped");

    private static string Folder(HostPage host) => Directory.CreateDirectory(Path.Combine(host.App.Folders.DataRoot, "files")).FullName;

    private static string Write(string folder, string name, string text)
    {
        var path = Path.Combine(folder, name);
        File.WriteAllText(path, text);
        return path;
    }
}
