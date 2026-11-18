using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Playwright;

namespace Spar.WindowAutomation;

/// <summary>
/// The Browser surface's shape (plan level 4): one headless Chromium launched by the library per test run, a context
/// per test, the page served over loopback (here a small static server instead of Spar's loopback host). Runs the
/// same <see cref="Journey"/> unchanged.
/// </summary>
internal static class BrowserSurface
{
    public static async Task<int> RunAsync(Options options)
    {
        var runs = options.Int("runs", 10);
        using var server = new StaticServer(View.PageFolder);
        var clock = Stopwatch.StartNew();
        using var playwright = await Playwright.CreateAsync();
        var driverMs = clock.Elapsed.TotalMilliseconds;
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
        Log.Write("SETUP", $"driver-start={driverMs:F1}ms launch={clock.Elapsed.TotalMilliseconds - driverMs:F1}ms version={browser.Version} origin={server.Origin}");
        var steps = new Timings();
        var test = new Timings();
        var failures = 0;
        for (var run = 0; run < runs; run++)
        {
            var started = Stopwatch.GetTimestamp();
            var context = await test.TimeAsync("new context+page", async () => await browser.NewContextAsync(new() { ViewportSize = new() { Width = 1008, Height = 729 } }));
            try
            {
                var page = await context.NewPageAsync();
                var errors = new List<string>();
                page.PageError += (_, error) => errors.Add(error);
                await test.TimeAsync("navigate", () => page.GotoAsync(server.Origin + "/"));
                var screenshot = options.Has("screenshots") && run == 0 ? Path.Combine(Paths.Output, "browser-settings.png") : null;
                await test.TimeAsync("journey", () => Journey.RunAsync(page, steps, screenshot));
                if (errors.Count > 0)
                {
                    throw new InvalidOperationException("page errors: " + string.Join(" | ", errors));
                }
            }
            catch (Exception exception)
            {
                failures++;
                Log.Write("FAIL", $"run={run} {exception.GetType().Name}: {exception.Message.ReplaceLineEndings(" ")}");
            }
            finally
            {
                await test.TimeAsync("close", () => context.CloseAsync());
                test.Add("per test", Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            }
        }

        test.Report("TEST");
        steps.Report("STEP");
        Log.Write("RESULT", $"runs={runs} failures={failures} served={server.Served}");
        return failures == 0 ? 0 : 1;
    }
}

/// <summary>A minimal HTTP/1.1 static server on 127.0.0.1 (one request per connection); client-side routes get index.html.</summary>
internal sealed class StaticServer : IDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly string _folder;
    private int _served;

    public StaticServer(string folder)
    {
        _folder = folder;
        _listener.Start();
        _ = Task.Run(AcceptLoopAsync);
    }

    public string Origin => $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}";

    public int Served => Volatile.Read(ref _served);

    public void Dispose() => _listener.Stop();

    private async Task AcceptLoopAsync()
    {
        while (true)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync();
            }
            catch (Exception exception) when (exception is ObjectDisposedException or SocketException)
            {
                return;
            }

            _ = Task.Run(() => ServeAsync(client));
        }
    }

    private async Task ServeAsync(TcpClient client)
    {
        using (client)
        {
            var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
            var requestLine = await reader.ReadLineAsync();
            while (!string.IsNullOrEmpty(await reader.ReadLineAsync()))
            {
            }

            if (requestLine is null)
            {
                return;
            }

            Interlocked.Increment(ref _served);
            var path = requestLine.Split(' ')[1].Split('?')[0].TrimStart('/');
            if (path.Length == 0 || !Path.HasExtension(path))
            {
                path = "index.html";
            }

            var file = Path.GetFullPath(Path.Combine(_folder, path));
            var found = file.StartsWith(_folder, StringComparison.OrdinalIgnoreCase) && File.Exists(file);
            var body = found ? await File.ReadAllBytesAsync(file) : [];
            var type = Path.GetExtension(file) switch
            {
                ".html" => "text/html; charset=utf-8",
                ".js" => "text/javascript; charset=utf-8",
                ".css" => "text/css; charset=utf-8",
                ".json" => "application/json",
                _ => "application/octet-stream",
            };
            var head = $"HTTP/1.1 {(found ? "200 OK" : "404 Not Found")}\r\nContent-Type: {type}\r\nContent-Length: {body.Length}\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n";
            await stream.WriteAsync(Encoding.ASCII.GetBytes(head));
            await stream.WriteAsync(body);
        }
    }
}
