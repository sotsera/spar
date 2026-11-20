using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Runtime.Versioning;
using System.Text;
using System.Text.RegularExpressions;
using Spar.Linux;
using Spar.Testing;

namespace Spar.Tests;

/// <summary>
/// The Linux host in a real window (plan M1c, build step 1), driven from outside: the bridge probe app
/// (<c>tests/apps/BridgeProbe</c>, a Development build with its page, <c>-p:SparDevServer=false</c>) runs under the
/// test's own X server, with private folders under <c>artifacts/linux-homes/&lt;run&gt;/</c>, and the test reads its
/// log. Until the Linux Window surface and CI image (M1c step 7), these run where a runner provides both: Linux, the
/// probe's executable in <c>SPAR_LINUX_HOST_APP</c> and an X server that is not the user's desktop in
/// <c>SPAR_LINUX_TEST_DISPLAY</c> (never WSLg's). Anywhere else they skip, saying why.
/// </summary>
[SupportedOSPlatform("linux")]
[Collection(nameof(LinuxWindowsCollection))]
public sealed class LinuxHostTests
{
    [Fact]
    public async Task The_page_calls_moves_to_a_new_document_and_closes_through_the_close_request()
    {
        await using var app = LinuxAppRun.Start("facts,echo,navigate");

        await app.WaitForAsync(@"probe note: second document (?<doc>[0-9a-f]{16}), echo second");
        var log = app.Log();
        Assert.Matches(@"INFO  Spar\.Host  \[[0-9a-f]{8}\] ui-ready at [0-9.]+ ms", log);
        Assert.Contains("probe note: main world: handler undefined, relay undefined", log, StringComparison.Ordinal);
        Assert.Matches(@"probe note: echo mean [0-9.]+ ms over 1000", log);
        var documents = Regex.Matches(log, @"Bridge: document ([0-9a-f]{16}) opened\.").Select(match => match.Groups[1].Value).ToList();
        Assert.Equal(2, documents.Count);
        Assert.NotEqual(documents[0], documents[1]);
        Assert.Contains($"probe note: first document {documents[0]}", log, StringComparison.Ordinal);
        Assert.Contains($"probe note: second document {documents[1]}", log, StringComparison.Ordinal);
        Assert.Contains($"Bridge: document {documents[0]} closed (navigation).", log, StringComparison.Ordinal);

        var webProcesses = app.WebKitProcesses();
        Assert.NotEmpty(webProcesses);
        var exit = await app.CloseAsync();

        Assert.Equal(0, exit);
        log = app.Log();
        Assert.Contains("close requested (User)", log, StringComparison.Ordinal);
        Assert.Matches(@"web processes exited after [0-9]+ ms", log);
        Assert.Contains("services disposed", log, StringComparison.Ordinal);
        Assert.All(webProcesses, process => Assert.False(LinuxAppRun.IsRunning(process.Id), $"{process.Name} {process.Id} outlived the app."));
    }

    [Fact]
    public async Task A_frame_of_another_origin_cannot_reach_the_bridge()
    {
        // Skip before the foreign site starts: on Windows its listener goes through http.sys, and a port taken in the
        // meantime failed the test in a NativeAOT run where it had nothing to check.
        LinuxAppRun.Display();
        LinuxAppRun.Probe();
        using var foreign = ForeignSite.Start();
        await using var app = LinuxAppRun.Start("frame", ("BRIDGE_PROBE_FRAME", foreign.FrameAddress));

        await app.WaitForAsync("probe note: frame done");
        await foreign.WaitForAsync("done", LinuxAppRun.Patience);

        // The frame ran, and nothing it tried reached the backend: no note, no message the bridge had to refuse.
        Assert.Equal("absent", foreign.Reports["handler"]);
        Assert.StartsWith("blocked", foreign.Reports["parent-event"], StringComparison.Ordinal);
        Assert.Equal("sent", foreign.Reports["top-post"]);
        Assert.Equal("undefined", foreign.Reports["relay"]);
        var log = app.Log();
        Assert.DoesNotContain("from-frame", log, StringComparison.Ordinal);
        Assert.DoesNotContain("dropped", log, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(log, @"Bridge: document [0-9a-f]{16} opened\."));

        Assert.Equal(0, await app.CloseAsync());
    }

    [Fact]
    public async Task A_lost_web_process_is_recovered_once_and_the_page_connects_again()
    {
        await using var app = LinuxAppRun.Start("");
        await app.WaitForAsync(@"probe note: first document [0-9a-f]{16}$");

        app.EndWebProcess();

        await app.WaitForAsync(@"probe note: first document [0-9a-f]{16} \(reloaded\)");
        var log = app.Log();
        Assert.Contains("closed (web process ended)", log, StringComparison.Ordinal);
        Assert.Contains("page reloaded after the web process ended", log, StringComparison.Ordinal);
        Assert.Equal(2, Regex.Matches(log, @"Bridge: document [0-9a-f]{16} reported ui-ready\.").Count);

        Assert.Equal(0, await app.CloseAsync());
    }

    [Fact]
    public async Task A_second_loss_before_the_page_is_ready_again_ends_the_app_with_exit_code_5()
    {
        Assert.SkipUnless(LinuxAppRun.IsDevelopmentProbe(), "Needs the Development build's automated mode: a Production app shows its message and waits.");
        await using var app = LinuxAppRun.Start("slow-ready-after-reload", ("SPAR_AUTOMATED", "1"));
        await app.WaitForAsync(@"probe note: first document [0-9a-f]{16}$");

        app.EndWebProcess();
        await app.WaitForAsync("probe note: reloaded, ready in 3 s");
        app.EndWebProcess();

        Assert.Equal(5, await app.WaitForExitAsync());
        Assert.Contains("Exiting with code 5: The page stopped working again right after it was reloaded.", app.Log(), StringComparison.Ordinal);
    }

    /// <summary>
    /// The M1c budgets on the Notebook's Production NativeAOT executable (plan, "Provisional budgets"), where the runner
    /// names it in <c>SPAR_LINUX_NOTEBOOK</c>: launch to <c>ui-ready</c> (from the test's clock at the launch to the
    /// time of the host's ready line), warm, one run discarded then 11; the close to the exit; and the total memory of
    /// the app and its WebKit processes (PSS) in one run. Recorded in
    /// <c>artifacts/test-results/Spar.Tests/linux-host-&lt;label&gt;.txt</c> (<c>SPAR_LINUX_NOTEBOOK_LABEL</c>, for runs
    /// with different settings in the environment), not asserted: times vary with the machine.
    /// </summary>
    [Fact]
    public async Task The_Notebook_starts_and_closes_and_its_numbers_are_recorded()
    {
        var notebook = Environment.GetEnvironmentVariable("SPAR_LINUX_NOTEBOOK");
        Assert.SkipWhen(string.IsNullOrEmpty(notebook), "Needs SPAR_LINUX_NOTEBOOK, the Notebook's published executable.");
        var ready = new List<double>();
        var closing = new List<double>();
        var memory = 0.0;
        for (var run = 0; run < 12; run++)
        {
            await using var app = LinuxAppRun.Launch(notebook!, "com.example.Notebook", []);
            var line = await app.WaitForAsync(@"^(?<time>\S+) INFO  Spar\.Host  \[[0-9a-f]{8}\] ui-ready at [0-9.]+ ms$");
            var readyAt = DateTime.Parse(line.Groups["time"].Value, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);
            if (run == 1)
            {
                // Settled first: the page has loaded its notes by now.
                await Task.Delay(1_000, TestContext.Current.CancellationToken);
                memory = app.TotalPssMegabytes();
            }

            var closed = Stopwatch.StartNew();
            Assert.Equal(0, await app.CloseAsync());
            if (run > 0)
            {
                ready.Add((readyAt - app.Launched).TotalMilliseconds);
                closing.Add(closed.Elapsed.TotalMilliseconds);
            }
        }

        // And once more with its web process lost: the page comes back by itself and the app still closes cleanly.
        await using (var lost = LinuxAppRun.Launch(notebook!, "com.example.Notebook", []))
        {
            await lost.WaitForAsync(@"Bridge: document [0-9a-f]{16} reported ui-ready\.");

            // The Notebook does not allow WebAssembly: its web process runs without it, and so does the one after a loss.
            Assert.Contains("WebAssembly off: JSC_useWasm=false.", lost.Log(), StringComparison.Ordinal);
            Assert.Equal("false", WebAssemblySetting(lost));
            lost.EndWebProcess();
            await lost.WaitForAsync(@"page reloaded after the web process ended(.|\n)*Bridge: document [0-9a-f]{16} reported ui-ready\.");
            Assert.Equal("false", WebAssemblySetting(lost));
            Assert.Equal(0, await lost.CloseAsync());
        }

        var size = new FileInfo(notebook!).Length;
        var results = Path.Combine(TestFolders.Artifacts(), "test-results", "Spar.Tests");
        Directory.CreateDirectory(results);
        await File.WriteAllLinesAsync(
            Path.Combine(results, $"linux-host-{Environment.GetEnvironmentVariable("SPAR_LINUX_NOTEBOOK_LABEL") ?? "default"}.txt"),
            [
                string.Create(CultureInfo.InvariantCulture, $"Recorded {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}"),
                string.Create(CultureInfo.InvariantCulture, $"Notebook executable: {size} B"),
                $"Launch to ui-ready, warm: {Summary(ready)}",
                $"Close request to exit, from outside: {Summary(closing)}",
                string.Create(CultureInfo.InvariantCulture, $"Total PSS, app and WebKit processes, one run: {memory:0.0} MB"),
                "WebAssembly: JSC_useWasm=false in the web process, and in the one after a loss",
            ],
            TestContext.Current.CancellationToken);
    }

    /// <summary><c>JSC_useWasm</c> as the app's web process started with it, waiting for the process if it is not there yet.</summary>
    private static string? WebAssemblySetting(LinuxAppRun app)
    {
        var limit = Stopwatch.StartNew();
        while (limit.Elapsed < LinuxAppRun.Patience)
        {
            foreach (var (id, name) in app.WebKitProcesses())
            {
                try
                {
                    if (name.StartsWith("WebKitWebProces", StringComparison.Ordinal)
                        && LinuxAppRun.EnvironmentOf(id).TryGetValue("JSC_useWasm", out var value))
                    {
                        return value;
                    }
                }
                catch (IOException)
                {
                    // The process ended meanwhile.
                }
            }

            Thread.Sleep(20);
        }

        return null;
    }

    internal static string Summary(List<double> milliseconds)
    {
        var sorted = milliseconds.Order().ToArray();
        var middle = sorted.Length / 2;
        var median = sorted.Length % 2 == 1 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2;
        var values = string.Join(", ", milliseconds.Select(value => value.ToString("0.0", CultureInfo.InvariantCulture)));
        return string.Create(CultureInfo.InvariantCulture, $"median {median:0.0} ms ({sorted[0]:0.0}–{sorted[^1]:0.0}, n = {sorted.Length}): {values}");
    }

    /// <summary>
    /// A site of another origin on loopback, whose page the probe embeds in a frame: its script tries every way to the
    /// bridge it might have and reports each outcome to its own server, never through the app.
    /// </summary>
    private sealed class ForeignSite : IDisposable
    {
        private const string Page = """
            <!doctype html><meta charset="utf-8"><title>Foreign</title>
            <script>
            const report = (key, value) => fetch("/report?" + key + "=" + encodeURIComponent(value));
            const call = text => '{"type":"call","id":900,"contract":"probe","command":"note"}\t' + JSON.stringify(text);
            (async () => {
              const handler = window.webkit && window.webkit.messageHandlers && window.webkit.messageHandlers.spar;
              await report("handler", handler ? "present" : "absent");
              if (handler) {
                try { handler.postMessage(call("from-frame-handler")); } catch (e) { }
              }
              try {
                parent.dispatchEvent(new CustomEvent("spar:send", { detail: call("from-frame-parent") }));
                await report("parent-event", "dispatched");
              } catch (e) {
                await report("parent-event", "blocked " + e.name);
              }
              try {
                top.postMessage(call("from-frame-post-message"), "*");
                await report("top-post", "sent");
              } catch (e) {
                await report("top-post", "threw " + e.name);
              }
              window.dispatchEvent(new CustomEvent("spar:send", { detail: call("from-frame-own-window") }));
              await report("relay", typeof sparRelay);
              await report("done", "1");
            })();
            </script>
            """;

        private readonly HttpListener _listener = new();
        private readonly ConcurrentDictionary<string, string> _reports = new(StringComparer.Ordinal);
        private readonly Task _serving;

        private ForeignSite(int port)
        {
            FrameAddress = $"http://127.0.0.1:{port.ToString(CultureInfo.InvariantCulture)}/frame.html";
            _listener.Prefixes.Add($"http://127.0.0.1:{port.ToString(CultureInfo.InvariantCulture)}/");
            _listener.Start();
            _serving = Task.Run(ServeAsync);
        }

        public string FrameAddress { get; }

        public IReadOnlyDictionary<string, string> Reports => _reports;

        public static ForeignSite Start()
        {
            using var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            var port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            return new ForeignSite(port);
        }

        public async Task WaitForAsync(string key, TimeSpan patience)
        {
            var limit = Stopwatch.StartNew();
            while (!_reports.ContainsKey(key))
            {
                Assert.True(limit.Elapsed < patience, $"The foreign frame never reported '{key}': {string.Join(", ", _reports)}");
                await Task.Delay(50, TestContext.Current.CancellationToken);
            }
        }

        public void Dispose()
        {
            _listener.Stop();
            _listener.Close();
            _serving.Wait(TimeSpan.FromSeconds(2));
        }

        private async Task ServeAsync()
        {
            while (_listener.IsListening)
            {
                HttpListenerContext context;
                try
                {
                    context = await _listener.GetContextAsync();
                }
                catch (Exception exception) when (exception is HttpListenerException or ObjectDisposedException or InvalidOperationException)
                {
                    return;
                }

                var path = context.Request.Url?.AbsolutePath ?? "";
                if (path == "/report")
                {
                    foreach (var key in context.Request.QueryString.AllKeys.OfType<string>())
                    {
                        _reports[key] = context.Request.QueryString[key] ?? "";
                    }
                }

                var body = Encoding.UTF8.GetBytes(path == "/frame.html" ? Page : "");
                context.Response.ContentType = "text/html; charset=utf-8";
                context.Response.ContentLength64 = body.Length;
                await context.Response.OutputStream.WriteAsync(body);
                context.Response.Close();
            }
        }
    }
}
