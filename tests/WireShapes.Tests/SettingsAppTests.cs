using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Spar;
using Spar.Testing;
using WireShapes;

namespace WireShapes.Tests;

/// <summary>
/// Settings records declared in the app's Program.cs, read and written with the serializer the build generated, each in
/// its own file under the test's data root (decision "M3b's decide-first questions", item 4).
/// </summary>
public sealed class SettingsAppTests : IAsyncLifetime
{
    private SparTestApp _app = null!;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _app = await SparTestApp.StartAsync<Program>(cancellationToken: Token);

    public ValueTask DisposeAsync() => _app.DisposeAsync();

    [Fact]
    public async Task The_declared_records_start_on_their_defaults_and_each_update_writes_its_own_file()
    {
        var settings = _app.Services.GetRequiredService<ISparSettings<WireSettings>>();
        var layout = _app.Services.GetRequiredService<ISparSettings<WireLayout>>();
        Assert.Equal((50_000, SparSettingsSource.Defaults), (settings.Current.BatchSize, settings.Source));

        await settings.UpdateAsync(s => s with { Recent = ["orders"], Window = new WireLayout(800, 600) }, Token);
        await layout.UpdateAsync(l => l with { Width = 1200 }, Token);

        using var file = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(_app.Folders.Data, "settings.json"), Token));
        var written = file.RootElement.GetProperty("settings");
        Assert.Equal("orders", written.GetProperty("recent")[0].GetString());
        Assert.Equal(800, written.GetProperty("window").GetProperty("width").GetInt32());
        Assert.Contains("\"width\": 1200", await File.ReadAllTextAsync(Path.Combine(_app.Folders.Data, "layout.json"), Token), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_watch_sees_the_settings_then_each_update()
    {
        var settings = _app.Services.GetRequiredService<ISparSettings<WireSettings>>();
        await using var watch = settings.WatchAsync(Token).GetAsyncEnumerator(Token);
        Assert.True(await watch.MoveNextAsync());

        await settings.UpdateAsync(s => s with { Theme = "Dark" }, Token);

        Assert.True(await watch.MoveNextAsync());
        Assert.Equal("Dark", watch.Current.Theme);
    }
}
