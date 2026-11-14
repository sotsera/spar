using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

[assembly: SupportedOSPlatform("windows10.0.17763")]

namespace Spike;

/// <summary>
/// Shared host for both bindings: one STA thread owns an offscreen window, the message loop and the browser. The
/// binding-specific code lives in <see cref="Backend"/> (WinRT projection or hand-written COM). Each run navigates to a
/// page served by WebResourceRequested, lets the page measure fetch and postMessage round trips, measures in-process
/// CDP calls, then closes and waits for the browser process to exit.
/// </summary>
internal sealed class Host
{
    public const string Origin = "https://app.spar.local";

    private static Host? s_current;
    private readonly Stopwatch _clock;
    private readonly TaskCompletionSource<string> _done = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Dictionary<string, double> _marks = [];
    private HWND _window;
    private UiContext? _ui;
    private long _closeRequestedAt;

    private Host(Stopwatch clock) => _clock = clock;

    public HWND Window => _window;

    public unsafe nint WindowHandle => (nint)_window.Value;

    public Task<string> PageDone => _done.Task;

    public int ExitCode { get; private set; }

    public uint BrowserProcessId { get; set; }

    [STAThread]
    private static int Main()
    {
        var clock = Stopwatch.StartNew();
        using (var self = Process.GetCurrentProcess())
        {
            Console.WriteLine($"[process start -> main: {(DateTime.Now - self.StartTime).TotalMilliseconds:F1} ms] binding={Backend.Name}");
        }

        var host = new Host(clock);
        s_current = host;
        host.CreateWindow();
        SynchronizationContext.SetSynchronizationContext(host._ui);
        host._ui!.Post(static async state => await ((Host)state!).RunAsync(), host);

        while (PInvoke.GetMessage(out var message, HWND.Null, 0, 0) > 0)
        {
            PInvoke.TranslateMessage(message);
            PInvoke.DispatchMessage(message);
        }

        host.Mark("message-loop-ended", sinceClose: true);
        host.WaitForBrowserExit();
        Console.WriteLine("SPIKE_MARKS " + string.Join(" ", host._marks.Select(pair => $"{pair.Key}={pair.Value:F1}")));
        return host.ExitCode;
    }

    private async Task RunAsync()
    {
        try
        {
            await Backend.StartAsync(this);
            var result = await PageDone.WaitAsync(TimeSpan.FromSeconds(30));
            Mark("page-benchmarks-done");
            Console.WriteLine("PAGE " + result);
            if (!result.Contains("\"first\":\"echo:ping\"", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("The page did not see the echo response.");
            }

            var title = await Backend.CallDevToolsAsync("Runtime.evaluate", """{"expression":"document.title","returnByValue":true}""");
            Console.WriteLine("CDP title " + title);
            var timings = new List<double>();
            for (var i = 0; i < 30; i++)
            {
                var start = Stopwatch.GetTimestamp();
                await Backend.CallDevToolsAsync("Runtime.evaluate", """{"expression":"1","returnByValue":true}""");
                timings.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
            }

            timings.Sort();
            Console.WriteLine($"CDP Runtime.evaluate x30: median {timings[15]:F3} ms, p95 {timings[28]:F3} ms, first {timings.Max():F3} ms max");
            Mark("cdp-done");
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("Host failure: " + exception);
            ExitCode = 2;
        }

        Close();
    }

    // ---- Called by the backends ----

    public void Mark(string name, bool sinceClose = false)
    {
        var milliseconds = sinceClose && _closeRequestedAt != 0
            ? Stopwatch.GetElapsedTime(_closeRequestedAt).TotalMilliseconds
            : _clock.Elapsed.TotalMilliseconds;
        _marks[name] = milliseconds;
        Console.WriteLine($"[{milliseconds,8:F1} ms] {name}");
    }

    /// <summary>Serves the page and the echo endpoint. Returns status, reason, content type and body.</summary>
    public static (int Status, string Reason, string ContentType, byte[] Body) Serve(string method, string uri, byte[] requestBody)
    {
        var path = new Uri(uri).AbsolutePath;
        return (method, path) switch
        {
            ("GET", "/") => (200, "OK", "text/html; charset=utf-8", Encoding.UTF8.GetBytes(Page.Html)),
            ("POST", "/api/echo") => (200, "OK", "text/plain; charset=utf-8", [.. "echo:"u8, .. requestBody]),
            _ => (404, "Not Found", "text/plain", []),
        };
    }

    /// <summary>Handles one page message: answers pings, and completes the run on "done:" or "error:".</summary>
    public string? OnPageMessage(string source, string? message)
    {
        if (!source.StartsWith(Origin + "/", StringComparison.Ordinal) || message is null)
        {
            return null;
        }

        if (message == "ping")
        {
            return "pong";
        }

        if (message.StartsWith("done:", StringComparison.Ordinal))
        {
            _done.TrySetResult(message[5..]);
        }
        else if (message.StartsWith("error:", StringComparison.Ordinal))
        {
            _done.TrySetException(new InvalidOperationException(message));
        }

        return null;
    }

    public string ProfileDirectory()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "global.json")))
            {
                var profile = Path.Combine(directory.FullName, "artifacts", "webview2-profiles", "cswinrt-" + Backend.Name.ToLowerInvariant());
                Directory.CreateDirectory(profile);
                return profile;
            }
        }

        throw new InvalidOperationException("Repository root (global.json) not found; refusing to use a default profile.");
    }

    public (int Width, int Height) ClientSize()
    {
        PInvoke.GetClientRect(_window, out var client);
        return (client.right, client.bottom);
    }

    // ---- Window and lifetime ----

    private void Close()
    {
        _closeRequestedAt = Stopwatch.GetTimestamp();
        PInvoke.PostMessage(_window, PInvoke.WM_CLOSE, default, default);
    }

    private unsafe void CreateWindow()
    {
        var instance = (HINSTANCE)(nint)PInvoke.GetModuleHandle((char*)null).Value;
        fixed (char* className = "SparCsWinRTWebView2Spike")
        fixed (char* title = "CsWinRT WebView2 spike")
        {
            var windowClass = new WNDCLASSEXW
            {
                cbSize = (uint)sizeof(WNDCLASSEXW),
                lpfnWndProc = &WindowProcedure,
                hInstance = instance,
                lpszClassName = className,
            };
            if (PInvoke.RegisterClassEx(windowClass) == 0)
            {
                throw new InvalidOperationException("RegisterClassEx failed: " + Marshal.GetLastPInvokeError());
            }

            // Shown, offscreen and not activated (decision D8: never SW_HIDE for a headless run).
            _window = PInvoke.CreateWindowEx(0, className, title, WINDOW_STYLE.WS_OVERLAPPEDWINDOW, -32000, -32000, 900, 640, HWND.Null, HMENU.Null, instance, null);
            if (_window.IsNull)
            {
                throw new InvalidOperationException("CreateWindowEx failed: " + Marshal.GetLastPInvokeError());
            }
        }

        _ui = new UiContext(_window);
        PInvoke.ShowWindow(_window, SHOW_WINDOW_CMD.SW_SHOWNOACTIVATE);
        Mark("window");
    }

    private void WaitForBrowserExit()
    {
        if (BrowserProcessId == 0)
        {
            return;
        }

        try
        {
            using var browser = Process.GetProcessById((int)BrowserProcessId);
            Mark(browser.WaitForExit(TimeSpan.FromSeconds(15)) ? "browser-exited" : "browser-still-running-after-15s", sinceClose: true);
        }
        catch (ArgumentException)
        {
            Mark("browser-exited", sinceClose: true);
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static LRESULT WindowProcedure(HWND window, uint message, WPARAM wParam, LPARAM lParam)
    {
        try
        {
            var host = s_current;
            switch (message)
            {
                case UiContext.DrainMessage:
                    host?._ui?.Drain();
                    return default;
                case PInvoke.WM_SIZE:
                    Backend.Resize();
                    return default;
                case PInvoke.WM_CLOSE:
                    if (host is not null && host._closeRequestedAt == 0)
                    {
                        host._closeRequestedAt = Stopwatch.GetTimestamp();
                    }

                    Backend.Shutdown();
                    PInvoke.DestroyWindow(window);
                    return default;
                case PInvoke.WM_DESTROY:
                    PInvoke.PostQuitMessage(0);
                    return default;
            }
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("Window procedure failure: " + exception);
        }

        return PInvoke.DefWindowProc(window, message, wParam, lParam);
    }
}

/// <summary>Single-threaded synchronization context drained from the window's message loop (as in spikes/react).</summary>
internal sealed class UiContext(HWND window) : SynchronizationContext
{
    public const uint DrainMessage = PInvoke.WM_APP + 1;

    private readonly ConcurrentQueue<(SendOrPostCallback Callback, object? State)> _queue = new();

    public override void Post(SendOrPostCallback d, object? state)
    {
        _queue.Enqueue((d, state));
        PInvoke.PostMessage(window, DrainMessage, default, default);
    }

    public override void Send(SendOrPostCallback d, object? state) => throw new NotSupportedException();

    public void Drain()
    {
        while (_queue.TryDequeue(out var item))
        {
            item.Callback(item.State);
        }
    }
}

internal static class Page
{
    // Measures 50 fetch round trips (1 KB through WebResourceRequested with a deferral) and 50 postMessage round
    // trips, then reports the medians to the host.
    public const string Html = """
        <!doctype html><meta charset="utf-8"><title>loading</title><body>spike<script>
        const median = a => { a.sort((x, y) => x - y); return a[a.length >> 1]; };
        const waiting = [];
        chrome.webview.addEventListener('message', e => waiting.shift()?.(e.data));
        const roundTrip = m => new Promise(r => { waiting.push(r); chrome.webview.postMessage(m); });
        (async () => {
          const first = await (await fetch('/api/echo', { method: 'POST', body: 'ping' })).text();
          document.title = first;
          const body = 'x'.repeat(1024), fetches = [], messages = [];
          for (let i = 0; i < 50; i++) { const t = performance.now(); await (await fetch('/api/echo', { method: 'POST', body })).text(); fetches.push(performance.now() - t); }
          for (let i = 0; i < 50; i++) { const t = performance.now(); await roundTrip('ping'); messages.push(performance.now() - t); }
          chrome.webview.postMessage('done:' + JSON.stringify({ first, fetchMedian: median(fetches), messageMedian: median(messages) }));
        })().catch(e => chrome.webview.postMessage('error:' + e));
        </script>
        """;
}
