using System.Diagnostics;
using Spar.Devtools;
using Spar.Hosting;
using Spar.Linux.Testing;

namespace Spar.Linux.Devtools;

/// <summary>
/// The app window's page for the devtools of a Development build on Linux: the window tests' page automation
/// (<see cref="TestPage"/>, the engine in a script world of every document) installed before the first load. On the
/// developer's own desktop there is no trusted input: XTEST would move the developer's pointer and Wayland has none, so
/// clicks and typing are the page's own events from the engine (decision "M2's decide-first questions", item 4), and
/// each answer says so. Script dialogs stay the developer's. Every call runs its WebKit part on the main loop.
/// </summary>
internal sealed class DevtoolsPage : IDevtoolsPage, ISparTestPageEvents
{
    /// <summary>How often a request that found no document yet (a navigation under way) is sent again.</summary>
    private static readonly TimeSpan Again = TimeSpan.FromMilliseconds(20);

    private readonly MainLoop _ui;
    private readonly DevtoolsLog _devtoolsLog;
    private TestPage? _page;
    private long _document;

    private DevtoolsPage(MainLoop ui, DevtoolsLog devtoolsLog)
    {
        _ui = ui;
        _devtoolsLog = devtoolsLog;
    }

    public bool TrustedInput => false;

    public long Document => Interlocked.Read(ref _document);

    /// <summary>On the main loop, before the first load; the page is in <see cref="DevtoolsHost.Pages"/> after it.</summary>
    public static TestPage Install(MainLoop ui, nint view, nint content, DevtoolsHost host, string engine)
    {
        var page = new DevtoolsPage(ui, host.Log);
        page._page = TestPage.Install(
            view, content, new SparTestAutomation(engine, DevtoolsHost.World, page), _ => { }, _ => { }, answerDialogs: false);
        host.Pages.Current = page;
        return page._page;
    }

    public async Task<string?> EngineAsync(string request, TimeSpan wait)
    {
        var page = Page;
        var clock = Stopwatch.StartNew();
        while (true)
        {
            var left = wait - clock.Elapsed;
            if (left <= TimeSpan.Zero)
            {
                return null;
            }

            var call = OnLoopAsync(() => page.EngineAsync(request));
            if (await Task.WhenAny(call, Task.Delay(left)) != call)
            {
                // Sent to a document that went before it answered, or a page too busy to answer in time.
                return null;
            }

            if (await call is { } answer)
            {
                return answer;
            }

            await Task.Delay(Again);
        }
    }

    public Task<byte[]> ScreenshotAsync() => OnLoopAsync(() => Page.ScreenshotAsync(null));

    public Task ClickAsync(double x, double y) =>
        throw new DevtoolsError("unavailable", "Linux has no trusted input on the developer's desktop; clicks are the page's own events.");

    /// <summary>The engine's <c>insertText</c>, an input method's insertion as the page receives it.</summary>
    public async Task InsertTextAsync(string text)
    {
        var request = new System.Buffers.ArrayBufferWriter<byte>();
        using (var writer = new System.Text.Json.Utf8JsonWriter(request))
        {
            writer.WriteStartObject();
            writer.WriteString("op", "insertText");
            writer.WriteString("text", text);
            writer.WriteEndObject();
        }

        await EngineAsync(System.Text.Encoding.UTF8.GetString(request.WrittenSpan), TimeSpan.FromSeconds(5));
    }

    void ISparTestPageEvents.Navigated(string url, bool newDocument)
    {
        if (newDocument)
        {
            Interlocked.Increment(ref _document);
        }
    }

    void ISparTestPageEvents.Console(string type, string text) => _devtoolsLog.PageConsole(type, text);

    void ISparTestPageEvents.PageError(string text) => _devtoolsLog.PageError(text);

    /// <summary>Not connected (<c>answerDialogs: false</c>): WebKit shows the dialog as it would without the devtools.</summary>
    bool ISparTestPageEvents.Dialog(string type, string message) => true;

    private TestPage Page => _page ?? throw new DevtoolsError("no_page", "The window's page automation is not installed yet; try again.");

    private Task<T> OnLoopAsync<T>(Func<Task<T>> work)
    {
        var result = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _ui.Post(() => _ = RelayAsync(work, result), "devtools call");
        return result.Task;
    }

    private static async Task RelayAsync<T>(Func<Task<T>> work, TaskCompletionSource<T> result)
    {
        try
        {
            result.TrySetResult(await work());
        }
        catch (Exception exception)
        {
            result.TrySetException(exception);
        }
    }
}
