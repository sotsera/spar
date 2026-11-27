using Microsoft.Extensions.DependencyInjection;
using Spar.Hosting;
using Spar.Testing;
using static Spar.Testing.SparAssertions;

namespace Spar.WindowTests;

/// <summary>
/// The app's windows for its C# code and a page's own window, in real windows (decision "M3d's decide-first questions",
/// item 8): <see cref="IWindows"/> opens a kind, titles, sizes and closes windows through their guards, and hears them
/// open and close; a command knows its caller's window (<see cref="ISparCall.Window"/>); a page retitles, resizes,
/// minimizes and closes its own. Maximizing is not checked: a Windows test window stays offscreen and does not
/// maximize, and the Linux test display has no window manager to ask.
/// </summary>
[Trait("Surface", "Window")]
public sealed class WindowControlTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_app_opens_titles_sizes_and_closes_its_windows_and_hears_them_open_and_close()
    {
        await using var host = await HostPage.OpenAsync();
        var windows = host.App.Services.GetRequiredService<IWindows>();
        var events = new List<string>();
        windows.Opened += (_, window) => { lock (events) { events.Add("opened " + window.Id); } };
        windows.Closed += (_, window) => { lock (events) { events.Add("closed " + window.Id); } };

        windows.Main.SetTitle("Orders — main");
        windows.Main.SetSize(820, 610);
        var opened = await windows.OpenAsync("note", """{"text":"from C#","n":7}""", Token);
        var note = opened switch
        {
            IAppWindow window => window,
            WindowOpenError error => throw new InvalidOperationException(error.ToString()),
        };
        var page = await host.App.WindowAsync("note", cancellationToken: Token);
        await Expect(page.Page.GetByText("This window: /note")).ToHaveTextAsync("""This window: /note {"text":"from C#","n":7}""");
        var all = windows.All.Select(w => w.Id).ToList();
        var main = await StateAsync(host.App.Window, state => state.Title == "Orders — main" && state.Width == 820);

        await page.Page.GetByLabel("Close guard").SelectOptionAsync("keep");
        var kept = await note.CloseAsync();
        await page.Page.GetByLabel("Close guard").SelectOptionAsync("none");
        var closed = await note.CloseAsync();
        await page.Window.Closed.WaitAsync(TimeSpan.FromSeconds(10), Token);
        await WaitForAsync(() => { lock (events) { return events.Count == 2; } });

        Assert.Equal(["main", "note-1"], all);
        Assert.Equal(("Orders — main", 820, 610), (main.Title, main.Width, main.Height));
        Assert.False(kept);
        Assert.True(closed);
        Assert.False(note.IsOpen);
        Assert.Equal(["opened note-1", "closed note-1"], events);
        Assert.Equal(["main"], windows.All.Select(w => w.Id));
    }

    [Fact]
    public async Task A_command_knows_its_callers_window_and_a_page_changes_its_own()
    {
        await using var host = await HostPage.OpenAsync();
        await host.ClickAsync("Open a note");
        var note = await host.App.WindowAsync("note", cancellationToken: Token);
        await Expect(note.Page.GetByText("This window: /note")).ToHaveTextAsync("""This window: /note {"text":"hello","n":1}""");

        await host.ClickAsync("Which window");
        await Expect(host.Page.GetByText("Window: main")).ToBeVisibleAsync();
        await Button(note, "Which window").ClickAsync();
        await Expect(note.Page.GetByText("Window: note-1")).ToBeVisibleAsync();
        await Button(note, "Retitle").ClickAsync();
        await Button(note, "Resize").ClickAsync();
        var changed = await StateAsync(note.Window, state => state.Title == "Retitled" && state.Width == 640 && state.Height == 480);
        if (OperatingSystem.IsWindows())
        {
            // Minimizing is a request to the window manager on Linux, and the test display has none.
            await Button(note, "Minimize").ClickAsync();
            await StateAsync(note.Window, state => state.Minimized);
            host.App.Services.GetRequiredService<IWindows>().All.Single(w => w.Id == "note-1").Restore();
            await StateAsync(note.Window, state => !state.Minimized);
        }

        await Button(note, "Close this window").ClickAsync();
        await note.Window.Closed.WaitAsync(TimeSpan.FromSeconds(10), Token);

        Assert.Equal(("Retitled", 640, 480), (changed.Title, changed.Width, changed.Height));
        await host.ClickAsync("Echo");
        await Expect(host.Status).ToHaveTextAsync("Echo: hello");
    }

    private static SparLocator Button(SparAppWindow window, string name) =>
        window.Page.GetByRole(SparRole.Button, new() { Name = name, Exact = true });

    /// <summary>The window's state once <paramref name="done"/> holds; the host changes it on its own thread, a little later.</summary>
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

    private static async Task WaitForAsync(Func<bool> done)
    {
        var limit = DateTime.UtcNow.AddSeconds(5);
        while (!done() && DateTime.UtcNow < limit)
        {
            await Task.Delay(20, Token);
        }
    }
}
