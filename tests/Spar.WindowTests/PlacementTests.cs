using Microsoft.Extensions.DependencyInjection;
using Spar.Hosting;
using Spar.Testing;
using static Spar.Testing.SparAssertions;

namespace Spar.WindowTests;

/// <summary>
/// Remembered placement in real windows (decision "M3d's decide-first questions", item 5): a window of a kind opens at
/// the size its last one had, and <c>windows.json</c> in the app's data folder holds what each kind keeps once the
/// changes settle. A test window starts at its declared size, since each test has a fresh data folder, and keeps no
/// position on Windows, where it stays offscreen. Where a window opens on the screen, a modal centred on its owner on
/// X11 included, is not checked here: the Linux test display places each test window itself.
/// </summary>
[Trait("Surface", "Window")]
public sealed class PlacementTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_window_opens_at_the_size_its_kinds_last_window_had_and_windows_json_keeps_it()
    {
        await using var host = await HostPage.OpenAsync();
        var windows = host.App.Services.GetRequiredService<IWindows>();

        var first = await OpenNoteAsync(host, windows, 0);
        var declared = await first.Window.NativeStateAsync();
        windows.All.Single(w => w.Id == first.Id).SetSize(700, 450);
        await StateAsync(first.Window, state => state.Width == 700 && state.Height == 450);
        await first.Window.CloseAsync();
        var second = await OpenNoteAsync(host, windows, 1);
        var reopened = await StateAsync(second.Window, state => state.Width == 700);
        windows.Main.SetSize(820, 610);
        var file = Path.Combine(host.App.Folders.DataRoot, "windows.json");
        var json = await FileAsync(file, text => text.Contains("\"width\": 820", StringComparison.Ordinal));

        Assert.Equal((600, 400), (declared.Width, declared.Height));
        Assert.Equal((700, 450), (reopened.Width, reopened.Height));
        Assert.Contains("\"note\"", json, StringComparison.Ordinal);
        Assert.Contains("\"height\": 450", json, StringComparison.Ordinal);
        Assert.Contains("\"height\": 610", json, StringComparison.Ordinal);
        if (OperatingSystem.IsWindows())
        {
            // An offscreen test window keeps no position.
            Assert.DoesNotContain("\"left\"", json, StringComparison.Ordinal);
        }
    }

    private static async Task<SparAppWindow> OpenNoteAsync(HostPage host, IWindows windows, int index)
    {
        _ = await windows.OpenAsync("note", """{"text":"placed","n":1}""", Token);
        var note = await host.App.WindowAsync("note", index, Token);
        await Expect(note.Page.GetByText("This window: /note")).ToBeVisibleAsync();
        return note;
    }

    private static async Task<SparTestWindowState> StateAsync(SparWindow window, Func<SparTestWindowState, bool> done)
    {
        var limit = DateTime.UtcNow.AddSeconds(5);
        while (true)
        {
            var state = await window.NativeStateAsync();
            if (done(state) || DateTime.UtcNow > limit)
            {
                return state;
            }

            await Task.Delay(50, Token);
        }
    }

    private static async Task<string> FileAsync(string path, Func<string, bool> done)
    {
        var limit = DateTime.UtcNow.AddSeconds(5);
        var text = "";
        while (DateTime.UtcNow < limit)
        {
            text = File.Exists(path) ? await File.ReadAllTextAsync(path, Token) : "";
            if (done(text))
            {
                break;
            }

            await Task.Delay(100, Token);
        }

        return text;
    }
}
