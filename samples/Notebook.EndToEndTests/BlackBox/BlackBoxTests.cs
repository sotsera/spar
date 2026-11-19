using Spar;
using Spar.Testing;

namespace Notebook.EndToEndTests;

/// <summary>
/// The Notebook's exact Production executable, launched (plan section 5, level 7): nothing replaced, its per-user
/// folders in a disposable data root, read and driven through UI Automation on a desktop of the test process's own, so
/// nothing shows on the monitor. Smoke checks and what spans processes: the clean close, the files, the hand-off to a
/// running instance, and the native messages of a failed relaunch and a failed start, with their exit codes.
/// </summary>
[Trait("Surface", "BlackBox")]
public sealed class BlackBoxTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task It_starts_shows_its_page_and_closes_cleanly()
    {
        SkipOffWindows();
        await using var app = await SparBlackBox.LaunchAsync(Projects.Notebook, cancellationToken: Token);

        Assert.Equal("Notebook", await app.Window.TitleAsync());
        await app.Window.GetByRole(SparRole.Heading, new() { Name = "Start your notebook" }).WaitForAsync();
        Assert.Equal(1, await app.Window.GetByRole(SparRole.Navigation, new() { Name = "Pages", Exact = true }).CountAsync());
        Assert.Equal(["New page", "New page"], await app.Window.GetByRole(SparRole.Button, new() { Name = "New page", Exact = true }).AllNamesAsync());

        Assert.Equal(0, await app.CloseAsync(Token));
        Assert.True(await app.WaitForProcessTreeExitAsync(), $"Processes left: {string.Join(", ", app.ProcessIds)}");
    }

    [Fact]
    public async Task Invoking_a_page_button_opens_its_dialog()
    {
        SkipOffWindows();
        await using var app = await SparBlackBox.LaunchAsync(Projects.Notebook, cancellationToken: Token);

        // The sidebar and the empty page both have the button: an action needs one, so the test says which.
        var newPage = app.Window.GetByRole(SparRole.Button, new() { Name = "New page", Exact = true });
        await Assert.ThrowsAsync<InvalidOperationException>(() => newPage.InvokeAsync());
        await newPage.First.InvokeAsync();

        await app.Window.GetByRole(SparRole.Dialog, new() { Name = "New page" }).WaitForAsync();
        await app.Window.GetByRole(SparRole.Button, new() { Name = "Create page" }).WaitForAsync();
        Assert.Equal(0, await app.CloseAsync(Token));
    }

    [Fact]
    public async Task Every_file_it_writes_is_under_its_data_root()
    {
        SkipOffWindows();
        var real = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Projects.Notebook.AppId);
        var before = Snapshot(real);

        await using var app = await SparBlackBox.LaunchAsync(Projects.Notebook, cancellationToken: Token);
        Assert.Equal(0, await app.CloseAsync(Token));
        Assert.True(await app.WaitForProcessTreeExitAsync());

        Assert.Equal(before, Snapshot(real));
        Assert.True(File.Exists(app.LogFile), "The app's log is under the data root.");
        Assert.True(Directory.Exists(Path.Combine(app.AppFolder, "WebView2", "EBWebView")), "The browser profile is under the data root.");
        Assert.StartsWith(app.DataRoot, app.AppFolder, StringComparison.OrdinalIgnoreCase);
        var log = await File.ReadAllTextAsync(app.LogFile, Token);
        Assert.Contains($"App id {Projects.Notebook.AppId}, folder {app.AppFolder}", log, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_second_launch_hands_over_to_the_running_instance_and_exits_0()
    {
        SkipOffWindows();
        await using var first = await SparBlackBox.LaunchAsync(Projects.Notebook, cancellationToken: Token);

        await using var second = await SparBlackBox.LaunchAsync(Projects.Notebook, o =>
        {
            o.DataRoot = first.DataRoot;
            o.Arguments.Add("--from-test");
            o.WaitForReady = false;
        }, Token);

        Assert.Equal(0, await second.WaitForExitAsync(Token));
        Assert.Contains("handed to the running instance", second.StandardError, StringComparison.Ordinal);
        await WaitForLogAsync(first, "relaunch with 1 arguments: window shown");
        Assert.Equal(0, await first.CloseAsync(Token));
    }

    [Fact]
    public async Task A_relaunch_that_gets_no_answer_shows_its_message_and_exits_8()
    {
        SkipOffWindows();
        string root;
        await using (var first = await SparBlackBox.LaunchAsync(Projects.Notebook, o => o.KeepData = true, Token))
        {
            Assert.Equal(0, await first.CloseAsync(Token));
            root = first.DataRoot;
        }

        try
        {
            // The lock of a running instance that nobody serves: the relaunch waits for it about 5 s, then gives up.
            var lockFile = Path.Combine(root, "LocalAppData", Projects.Notebook.AppId, "instance.lock");
            await using var held = new FileStream(lockFile, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            await using var relaunch = await SparBlackBox.LaunchAsync(Projects.Notebook, o =>
            {
                o.DataRoot = root;
                o.WaitForReady = false;
            }, Token);

            var dialog = await relaunch.WaitForDialogAsync(TimeSpan.FromSeconds(20), Token);
            Assert.Equal("Notebook", dialog.Title);
            Assert.Contains("Notebook can't start", dialog.Text, StringComparison.Ordinal);
            Assert.Contains("did not let another start within 5 seconds", dialog.Text, StringComparison.Ordinal);
            Assert.Contains("Close", dialog.Buttons);

            await dialog.PressAsync("Close");
            Assert.Equal(8, await relaunch.WaitForExitAsync(Token));
            Assert.Contains("Exit code 8.", relaunch.StandardError, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task A_start_whose_profile_folder_cannot_be_created_shows_its_message_and_exits_70()
    {
        SkipOffWindows();
        string root;
        await using (var first = await SparBlackBox.LaunchAsync(Projects.Notebook, o => o.KeepData = true, Token))
        {
            Assert.Equal(0, await first.CloseAsync(Token));
            Assert.True(await first.WaitForProcessTreeExitAsync());
            root = first.DataRoot;
        }

        try
        {
            // The browser profile's folder is a file: the host cannot create it, and says so before the browser starts.
            var profile = Path.Combine(root, "LocalAppData", Projects.Notebook.AppId, "WebView2");
            Directory.Delete(profile, recursive: true);
            await File.WriteAllTextAsync(profile, "not a folder", Token);
            await using var broken = await SparBlackBox.LaunchAsync(Projects.Notebook, o =>
            {
                o.DataRoot = root;
                o.WaitForReady = false;
            }, Token);

            var dialog = await broken.WaitForDialogAsync(cancellationToken: Token);
            Assert.Equal("Notebook", dialog.Title);
            Assert.Contains("Notebook can't start", dialog.Text, StringComparison.Ordinal);
            Assert.Contains($"The browser profile folder '{profile}' cannot be created", dialog.Text, StringComparison.Ordinal);
            await dialog.PressAsync("Close");
            Assert.Equal(70, await broken.WaitForExitAsync(Token));
            Assert.Contains("The app's configuration stops its start", broken.StandardError, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>The black-box fixture runs on Windows until the Linux host arrives (M1c); elsewhere each test is a skip.</summary>
    private static void SkipOffWindows() =>
        Assert.SkipUnless(OperatingSystem.IsWindows(), "The black-box fixture runs on Windows; the Linux host arrives in M1c.");

    private static async Task WaitForLogAsync(SparBlackBoxApp app, string text)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            await using var stream = new FileStream(app.LogFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            if ((await reader.ReadToEndAsync(Token)).Contains(text, StringComparison.Ordinal))
            {
                return;
            }

            await Task.Delay(50, Token);
        }

        Assert.Fail($"The app's log never had '{text}'.");
    }

    /// <summary>Every file under a folder with its size and time, or nothing when the folder does not exist.</summary>
    private static string Snapshot(string folder) => !Directory.Exists(folder)
        ? "(none)"
        : string.Join("\n", Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal)
            .Select(file => $"{file} {new FileInfo(file).Length} {File.GetLastWriteTimeUtc(file):O}"));
}
