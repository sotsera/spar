using SampleApp.Orders;
using Spar;
using Spar.Testing;

namespace SampleApp.Tests;

/// <summary>A test cannot reach a real resource or the user's data by accident. One class: the counters are shared.</summary>
public sealed class GuardTests
{
    [Fact]
    public async Task An_unreplaced_external_dependency_fails_and_names_itself()
    {
        var before = SqlOrderDatabase.Constructed;
        await using var app = await SparTestApp.StartAsync(AppEntry.Run, cancellationToken: TestContext.Current.CancellationToken);

        var reply = await new NotesClient(app).OrderCountAsync();

        Assert.Equal("failure", reply.Status);
        Assert.Equal("external-dependency", reply.FailureCode());
        var logged = Assert.Single(app.Log.Entries, e => e.Exception is SparExternalDependencyException).Exception!;
        Assert.Contains("IOrderDatabase is an external dependency", logged.Message, StringComparison.Ordinal);
        Assert.Equal(before, SqlOrderDatabase.Constructed);
    }

    [Fact]
    public async Task AllowExternal_lets_the_real_one_through()
    {
        var before = SqlOrderDatabase.Constructed;
        await using var app = await SparTestApp.StartAsync(
            AppEntry.Run, o => o.AllowExternal<IOrderDatabase>(), TestContext.Current.CancellationToken);

        var reply = await new NotesClient(app).OrderCountAsync();

        Assert.Equal("unhandled", reply.FailureCode()); // the real one ran and threw "would query production"
        Assert.Equal(before + 1, SqlOrderDatabase.Constructed);
    }

    [Fact]
    public async Task Spar_outward_services_are_fakes_by_default()
    {
        var before = SystemClipboard.Constructed;
        await using var app = await TestApps.StartAsync();
        var notes = new NotesClient(app);
        var note = await notes.AddAsync("secret");

        var reply = await notes.CopyAsync(note.Value!.Id);

        Assert.Equal("ok", reply.Status);
        Assert.Equal(["secret"], app.Services.GetService(typeof(FakeClipboard)) is FakeClipboard fake ? fake.Writes : []);
        Assert.Equal(before, SystemClipboard.Constructed);
    }

    [Fact]
    public async Task Every_folder_sits_under_the_test_data_root()
    {
        await using var app = await TestApps.StartAsync();
        var folders = app.Folders;
        var separator = Path.DirectorySeparatorChar;

        Assert.Contains($"{separator}artifacts{separator}test-host{separator}", folders.DataRoot, StringComparison.Ordinal);
        Assert.StartsWith(folders.DataRoot, folders.Data, StringComparison.Ordinal);
        Assert.StartsWith(folders.DataRoot, folders.Logs, StringComparison.Ordinal); // not the app's BaseDirectory/logs
        Assert.StartsWith(folders.DataRoot, folders.Cache, StringComparison.Ordinal);
        var engineRoot = OperatingSystem.IsWindows() ? "webview2-profiles" : "linux-homes";
        Assert.Contains($"{separator}artifacts{separator}{engineRoot}{separator}", folders.WebView, StringComparison.Ordinal);
    }
}
