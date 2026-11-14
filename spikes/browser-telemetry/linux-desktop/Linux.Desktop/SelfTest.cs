#if SPAR_SELFTEST
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace Linux.Desktop;

/// <summary>
/// In-process self-test: a journey through JavaScript evaluation, a snapshot, a caching check, the bridge bench
/// (the React spike's bench page and SDK) and memory, then a normal window close. Results go to stdout.
/// </summary>
internal static class SelfTest
{
    public static async Task RunAsync(LinuxHost host)
    {
        try
        {
            await RunCoreAsync(host);
        }
        catch (Exception exception)
        {
            Console.WriteLine($"SPAR_SELFTEST_FAILED {exception}");
            host.ExitCode = 1;
        }

        Console.Out.Flush();
        host.Close();
    }

    private static async Task RunCoreAsync(LinuxHost host)
    {
        var page = Array.IndexOf(Environment.GetCommandLineArgs(), "--page") >= 0 ? "bench" : "app";
        if (page == "app")
        {
            var ready = await Task.WhenAny(host.UiReady, Task.Delay(15_000));
            if (ready != host.UiReady)
            {
                throw new TimeoutException("The React app never called /_spar/ready.");
            }

            await WaitForFramesAsync(host);
            Console.WriteLine("SPAR_PAGE_TIMING " + await EvaluateAsync(host, """
                const nav = performance.getEntriesByType('navigation')[0];
                return { domContentLoaded: Math.round(nav?.domContentLoadedEventEnd ?? -1), load: Math.round(nav?.loadEventEnd ?? -1),
                  fcp: Math.round(performance.getEntriesByName('first-contentful-paint')[0]?.startTime ?? -1),
                  resources: performance.getEntriesByType('resource').map(e => e.name.replace(location.origin, '') + ' ' + Math.round(e.startTime) + '-' + Math.round(e.responseEnd)) };
                """));
            await JourneyAsync(host);
            if (HasFlag("--telemetry"))
            {
                await TelemetryAsync(host);
            }

            if (Option("--snapshot") is { } snapshot)
            {
                await SnapshotAsync(host, snapshot);
            }

            if (HasFlag("--external-click"))
            {
                await ExternalClickAsync(host);
            }

            if (HasFlag("--caching"))
            {
                await CachingAsync(host);
            }

            PrintMemory("after-journey");
        }

        if (Option("--bench") is { } bench)
        {
            await BenchAsync(host, bench);
            PrintMemory("after-bench");
        }

        if (Option("--hold-ms") is { } hold)
        {
            await Task.Delay(int.Parse(hold, CultureInfo.InvariantCulture));
        }
    }

    private static string? Option(string name)
    {
        var args = Environment.GetCommandLineArgs();
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static bool HasFlag(string name) => Environment.GetCommandLineArgs().Contains(name);

    private static Task WaitForFramesAsync(LinuxHost host) =>
        EvaluateAsync(host, "await new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r))); return true;");

    /// <summary>
    /// --telemetry: how page telemetry started, a click on Preview (links.preview: page click, call, backend command,
    /// outbound HttpClient), --telemetry-bench (calls with tracing on and off, both transports), then a flush unless
    /// --no-flush (the close then relies on the host asking the page to flush).
    /// </summary>
    private static async Task TelemetryAsync(LinuxHost host)
    {
        Console.WriteLine("SPAR_TELEMETRY startup " + await EvaluateAsync(host, "return JSON.stringify(window.sparTelemetryStartup);"));
        var timer = Stopwatch.StartNew();
        var preview = await EvaluateAsync(host, """
            [...document.querySelectorAll('button')].find(b => b.textContent === 'Preview').click();
            const started = performance.now();
            while (true) {
              const text = document.querySelector('output')?.textContent ?? '';
              if (text.length > 0 && !text.startsWith('Loading')) return text;
              if (performance.now() - started > 20000) throw new Error('timeout waiting for the preview');
              await new Promise(r => setTimeout(r, 5));
            }
            """);
        Console.WriteLine($"SPAR_TELEMETRY preview {preview} ms={timer.Elapsed.TotalMilliseconds:F0} stats={await EvaluateAsync(host, "return JSON.stringify(window.sparTelemetry?.stats ?? null);")}");

        if (HasFlag("--telemetry-bench"))
        {
            for (var round = 1; round <= 3; round++)
            {
                foreach (var transport in new[] { "fetch", "message" })
                {
                    foreach (var tracing in new[] { true, false })
                    {
                        var result = await EvaluateAsync(host, $$"""
                            if (!window.sparTelemetry && {{(tracing ? "true" : "false")}}) return 'skipped';
                            window.sparTelemetry?.setTracing({{(tracing ? "true" : "false")}});
                            const started = performance.now();
                            const result = await window.sparCallBench({ transport: '{{transport}}', n: 300 });
                            return JSON.stringify({ ...result, wallMs: Math.round(performance.now() - started) });
                            """);
                        Console.WriteLine($"SPAR_TELEMETRY bench round={round} transport={transport} tracing={(tracing ? "on" : "off")} {result}");
                    }
                }
            }

            await EvaluateAsync(host, "window.sparTelemetry?.setTracing(true); return true;");
        }

        if (!HasFlag("--no-flush"))
        {
            timer.Restart();
            await EvaluateAsync(host, "await window.sparTelemetry?.flush(); return true;");
            Console.WriteLine($"SPAR_TELEMETRY flush ms={timer.Elapsed.TotalMilliseconds:F1}");
        }

        Console.WriteLine("SPAR_TELEMETRY assets " + await EvaluateAsync(host,
            "return JSON.stringify(performance.getEntriesByType('resource').map(e => new URL(e.name).pathname).filter(p => p.startsWith('/assets/')));"));
    }

    private static async Task JourneyAsync(LinuxHost host)
    {
        var timer = Stopwatch.StartNew();
        var text = await EvaluateAsync(host, """
            const input = [...document.querySelectorAll('label')].find(l => l.textContent.startsWith('Note')).querySelector('input');
            input.focus();
            // React tracks the value through the native setter; an input event then reaches onChange.
            Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value').set.call(input, 'Buy milk');
            input.dispatchEvent(new Event('input', { bubbles: true }));
            let trusted = null;
            const button = document.querySelector('button[type=submit]');
            button.addEventListener('click', e => { trusted = e.isTrusted; }, { once: true });
            button.click();
            const started = performance.now();
            while (!document.querySelector('ul[aria-label=Notes] li')?.textContent.includes('Buy milk')) {
              if (performance.now() - started > 5000) throw new Error('timeout waiting for the note');
              await new Promise(r => setTimeout(r, 1));
            }
            return { item: document.querySelector('ul[aria-label=Notes] li').textContent,
                     status: document.querySelector('[role=status]').textContent, clickIsTrusted: trusted };
            """);
        Console.WriteLine($"SPAR_JOURNEY {timer.Elapsed.TotalMilliseconds:F1}ms {text}");
    }

    /// <summary>
    /// Prints where the Save button is (view coordinates; the window sits at 0,0 on a bare Xvfb) and waits for a click
    /// from outside the process (xdotool, XTEST), then reports whether the page saw it as trusted.
    /// </summary>
    private static async Task ExternalClickAsync(LinuxHost host)
    {
        var at = await EvaluateAsync(host, """
            const button = document.querySelector('button[type=submit]');
            delete document.body.dataset.trusted;
            button.addEventListener('click', e => { document.body.dataset.trusted = e.isTrusted; }, { once: true });
            const r = button.getBoundingClientRect();
            return Math.round(r.left + r.width / 2) + ' ' + Math.round(r.top + r.height / 2);
            """);
        Console.WriteLine($"SPAR_CLICK_AT {at.Trim('"')}");
        Console.Out.Flush();
        var result = await EvaluateAsync(host, """
            const started = performance.now();
            while (document.body.dataset.trusted === undefined && performance.now() - started < 5000) await new Promise(r => setTimeout(r, 5));
            return document.body.dataset.trusted ?? 'no click seen';
            """);
        Console.WriteLine($"SPAR_EXTERNAL_CLICK isTrusted={result}");
    }

    private static async Task CachingAsync(LinuxHost host)
    {
        var before = new Dictionary<string, int>(host.AssetHits);
        host.Reload();
        await Task.WhenAny(host.UiReady, Task.Delay(10_000));
        var after = host.AssetHits;
        var report = string.Join(", ", after.Select(p => $"{p.Key}:{before.GetValueOrDefault(p.Key)}->{p.Value}"));
        Console.WriteLine($"SPAR_CACHING (no-store) {report}");
    }

    private static async Task BenchAsync(LinuxHost host, string spec)
    {
        host.Navigate("bench.html");
        await host.Loaded;
        await EvaluateAsync(host, """
            const started = performance.now();
            while (!window.sparBenchReady) {
              if (performance.now() - started > 10000) throw new Error('bench page not ready');
              await new Promise(r => setTimeout(r, 5));
            }
            return true;
            """);
        var file = File.ReadAllLines(spec);
        foreach (var line in file)
        {
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#'))
            {
                continue;
            }

            // "<function> <json options>"
            var space = line.IndexOf(' ', StringComparison.Ordinal);
            var name = line[..space];
            var options = line[(space + 1)..];
            var evaluationsBefore = host.Evaluations;
            var deliveredBefore = host.Delivered;
            var timer = Stopwatch.StartNew();
            string result;
            try
            {
                result = await EvaluateAsync(host, $"return await window.sparBench.{name}({options});");
            }
            catch (InvalidOperationException exception)
            {
                result = $"{{\"error\":\"{System.Text.Json.JsonEncodedText.Encode(exception.Message)}\"}}";
            }

            Console.WriteLine($"SPAR_BENCH {name} {result} host={{\"wallMs\":{timer.Elapsed.TotalMilliseconds:F1},\"evaluations\":{host.Evaluations - evaluationsBefore},\"messages\":{host.Delivered - deliveredBefore}}}");
        }
    }

    public static void PrintMemory(string label)
    {
        var self = LinuxHost.Memory(Environment.ProcessId);
        var line = new StringBuilder($"SPAR_MEMORY {label} app rss={self.RssMB:F1} pss={self.PssMB:F1}");
        double rss = self.RssMB, pss = self.PssMB;
        foreach (var (pid, name) in LinuxHost.Descendants())
        {
            var memory = LinuxHost.Memory(pid);
            rss += memory.RssMB;
            pss += memory.PssMB;
            line.Append(CultureInfo.InvariantCulture, $" | {name}({pid}) rss={memory.RssMB:F1} pss={memory.PssMB:F1}");
        }

        line.Append(CultureInfo.InvariantCulture, $" | total rss={rss:F1} pss={pss:F1}");
        Console.WriteLine(line);
    }

    // ---- WebKit calls ----

    /// <summary>Evaluates an async function body in the page; the awaited value comes back serialized with JSON.stringify.</summary>
    public static unsafe Task<string> EvaluateAsync(LinuxHost host, string body)
    {
        var completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var handle = GCHandle.Alloc(completion);
        var bytes = Encoding.UTF8.GetBytes(body);
        fixed (byte* data = bytes)
        {
            WebKit.webkit_web_view_call_async_javascript_function(
                host.WebView, data, bytes.Length, 0, 0, 0, 0, &OnEvaluated, GCHandle.ToIntPtr(handle));
        }

        return completion.Task;
    }

    [UnmanagedCallersOnly]
    private static void OnEvaluated(nint webView, nint result, nint data)
    {
        var handle = GCHandle.FromIntPtr(data);
        var completion = (TaskCompletionSource<string>)handle.Target!;
        handle.Free();
        var value = WebKit.webkit_web_view_call_async_javascript_function_finish(webView, result, out var error);
        if (value == 0)
        {
            var message = GLib.ErrorMessage(error);
            GLib.g_error_free(error);
            completion.SetException(new InvalidOperationException(message));
            return;
        }

        var json = GLib.TakeString(Jsc.jsc_value_to_json(value, 0)) ?? "undefined";
        GObject.g_object_unref(value);
        completion.SetResult(json);
    }

    private static async Task SnapshotAsync(LinuxHost host, string path)
    {
        var timer = Stopwatch.StartNew();
        var completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var handle = GCHandle.Alloc((completion, Path.GetFullPath(path)));
        unsafe
        {
            WebKit.webkit_web_view_get_snapshot(
                host.WebView, WebKit.SnapshotRegionVisible, WebKit.SnapshotOptionsNone, 0, &OnSnapshot, GCHandle.ToIntPtr(handle));
        }

        var outcome = await completion.Task;
        Console.WriteLine($"SPAR_SNAPSHOT {timer.Elapsed.TotalMilliseconds:F1}ms {outcome}");
    }

    [UnmanagedCallersOnly]
    private static void OnSnapshot(nint webView, nint result, nint data)
    {
        var handle = GCHandle.FromIntPtr(data);
        var (completion, path) = ((TaskCompletionSource<string>, string))handle.Target!;
        handle.Free();
        var image = WebKit.webkit_web_view_get_snapshot_finish(webView, result, out var error);
        if (image == 0)
        {
            completion.SetResult("failed: " + GLib.ErrorMessage(error));
            GLib.g_error_free(error);
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
#if SPAR_GTK4
        var saved = Gtk.gdk_texture_save_to_png(image, path) != 0;
        GObject.g_object_unref(image);
#else
        var saved = Cairo.cairo_surface_write_to_png(image, path) == 0;
        Cairo.cairo_surface_destroy(image);
#endif
        completion.SetResult(saved ? $"{path} {new FileInfo(path).Length} bytes" : "save failed");
    }
}

#endif
