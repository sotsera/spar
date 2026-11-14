using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Bridge;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

// One run: Kestrel on 127.0.0.1, a headless Chromium owned by this process, one page in one mode and encoding.
// Usage: Bridge.Loopback --mode full --enc A --run 1 [--browser <chrome-headless-shell.exe>]
string Arg(string name, string fallback)
{
    var at = Array.IndexOf(args, "--" + name);
    return at >= 0 && at + 1 < args.Length ? args[at + 1] : fallback;
}

var mode = Arg("mode", "micro");
var encoding = Arg("enc", "A");
var run = Arg("run", "1");
var verbose = args.Contains("--verbose");
var root = Root();
var browserPath = Arg("browser", "") is { Length: > 0 } given ? given : FindHeadlessShell();
var cpuStart = Process.GetCurrentProcess().TotalProcessorTime;

var builder = WebApplication.CreateSlimBuilder();
builder.Logging.ClearProviders();
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MinResponseDataRate = null;
    options.Limits.MaxRequestBodySize = 64 * 1024 * 1024;
    options.Listen(System.Net.IPAddress.Loopback, 0);
});
var app = builder.Build();
var sessions = new ConcurrentDictionary<string, BridgeSession>(StringComparer.Ordinal);
var done = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
Cdp? cdp = null;
(long Malformed, long Unrecognized, long FramesOut, long BytesOut, long PushFrames, long PushItems) lastStats = default;

app.Run(async context =>
{
    var request = context.Request;
    var response = context.Response;
    if (verbose)
    {
        Console.WriteLine($"{request.Method} {request.Path}{request.QueryString}");
    }
    switch (request.Method, request.Path.Value)
    {
        case ("GET", "/bench.html"):
            await Write(response, "text/html; charset=utf-8", Corpus.PageFile("bench.html"));
            return;
        case ("GET", "/bench.js"):
            await Write(response, "text/javascript; charset=utf-8", Corpus.PageFile("bench.js"));
            return;
        case ("GET", "/corpus.js"):
            await Write(response, "text/javascript; charset=utf-8", Corpus.Script());
            return;
        case ("POST", "/_spar/channel"):
            await ChannelAsync(context);
            return;
        case ("POST", "/_spar/send"):
            {
                var doc = request.Query["doc"].ToString();
                var body = await ReadBodyAsync(request);
                if (!sessions.TryGetValue(doc, out var session))
                {
                    response.StatusCode = 404;
                    return;
                }

                var memory = body.AsMemory();
                while (!memory.IsEmpty)
                {
                    var newline = memory.Span.IndexOf((byte)'\n');
                    var line = newline < 0 ? memory : memory[..newline];
                    session.Receive(line);
                    memory = newline < 0 ? default : memory[(newline + 1)..];
                }

                response.StatusCode = 204;
                return;
            }

        case ("POST", "/bench/mark"):
            {
                var json = "{}";
                if (cdp is not null)
                {
                    try
                    {
                        json = await cdp.MetricsAsync();
                    }
                    catch (Exception exception) when (exception is WebSocketException or InvalidOperationException or JsonException)
                    {
                        Console.Error.WriteLine("CDP failed: " + exception.Message);
                    }
                }

                await Write(response, "application/json", Encoding.UTF8.GetBytes(json));
                return;
            }

        case ("POST", "/bench/result"):
            done.TrySetResult(Encoding.UTF8.GetString(await ReadBodyAsync(request)));
            await Write(response, "application/json", "{}"u8.ToArray());
            return;
        default:
            response.StatusCode = 404;
            return;
    }
});

async Task ChannelAsync(HttpContext context)
{
    var doc = context.Request.Query["doc"].ToString();
    var frames = Channel.CreateUnbounded<byte[]>(new UnboundedChannelOptions { SingleReader = true });
    using var session = new BridgeSession(Codecs.Get(context.Request.Query["enc"].ToString()), frame =>
    {
        var line = new byte[frame.Length + 1];
        frame.Span.CopyTo(line);
        line[^1] = (byte)'\n';
        frames.Writer.TryWrite(line);
    });
    sessions[doc] = session;
    var response = context.Response;
    response.ContentType = "text/plain; charset=utf-8";
    response.Headers.CacheControl = "no-store";
    await response.StartAsync();
    var writer = response.BodyWriter;
    await writer.FlushAsync(CancellationToken.None);
    var aborted = context.RequestAborted;
    try
    {
        while (await frames.Reader.WaitToReadAsync(aborted))
        {
            while (frames.Reader.TryRead(out var line))
            {
                writer.Write(line);
            }

            // Transport flushes never take a command's token (spikes/react); the request's own abort ends it.
            var flush = await writer.FlushAsync(CancellationToken.None);
            if (flush.IsCompleted || flush.IsCanceled)
            {
                break;
            }
        }
    }
    catch (OperationCanceledException)
    {
    }
    finally
    {
        sessions.TryRemove(doc, out _);
        lastStats = session.Stats;
    }
}

await app.StartAsync();
var address = app.Services.GetService(typeof(IServer)) is IServer server
    ? server.Features.Get<IServerAddressesFeature>()!.Addresses.First()
    : throw new InvalidOperationException("No server.");
Console.WriteLine($"listening on {address}; browser {Path.GetFileName(browserPath)}");

var profile = Path.Combine(root, "artifacts", "bridge-encoding", "chromium-profile");
Directory.CreateDirectory(profile);
var portFile = Path.Combine(profile, "DevToolsActivePort");
if (File.Exists(portFile))
{
    File.Delete(portFile);
}

var start = new ProcessStartInfo(browserPath)
{
    UseShellExecute = false,
    RedirectStandardError = true,
    RedirectStandardOutput = true,
};
foreach (var argument in new[]
{
    "--headless", "--user-data-dir=" + profile, "--no-first-run", "--no-default-browser-check", "--disable-extensions",
    "--disable-background-networking", "--disable-component-update", "--disable-sync", "--mute-audio",
    "--remote-debugging-port=0", "--remote-allow-origins=*", $"{address}/bench.html?mode={mode}&enc={encoding}",
})
{
    start.ArgumentList.Add(argument);
}

var exitCode = 0;
using var browser = Process.Start(start) ?? throw new InvalidOperationException("Browser did not start.");
browser.OutputDataReceived += (_, _) => { };
browser.ErrorDataReceived += (_, _) => { };
browser.BeginOutputReadLine();
browser.BeginErrorReadLine();
try
{
    cdp = await Cdp.ConnectAsync(portFile, TimeSpan.FromSeconds(10));
    var result = await done.Task.WaitAsync(TimeSpan.FromSeconds(60));
    var cpu = Process.GetCurrentProcess().TotalProcessorTime - cpuStart;
    var s = lastStats;
    foreach (var open in sessions.Values)
    {
        s = open.Stats;
    }

    var hostJson = $"{{\"cpuMs\":{cpu.TotalMilliseconds:F0},\"malformed\":{s.Malformed},\"unrecognized\":{s.Unrecognized},\"framesOut\":{s.FramesOut},\"bytesOut\":{s.BytesOut},\"pushFrames\":{s.PushFrames},\"pushItems\":{s.PushItems},\"runtime\":\"{RuntimeInformation.FrameworkDescription}\",\"aot\":{(!RuntimeFeature.IsDynamicCodeSupported ? "true" : "false")},\"browser\":\"{Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(browserPath)))}\"}}";
    var output = Path.Combine(root, "artifacts", "bridge-encoding", "loopback");
    Directory.CreateDirectory(output);
    var file = Path.Combine(output, $"{mode}-{encoding}-{run}.json");
    File.WriteAllText(file, $"{{\"hostSide\":{hostJson},\"page\":{result}}}", new UTF8Encoding(false));
    Console.WriteLine("result written: " + Path.GetRelativePath(root, file));
}
catch (Exception exception)
{
    Console.Error.WriteLine("Loopback failure: " + exception);
    exitCode = 2;
}
finally
{
    cdp?.Dispose();

    // Only the browser tree this process started.
    if (!browser.HasExited)
    {
        browser.Kill(entireProcessTree: true);
    }

    browser.WaitForExit(15_000);
    Console.WriteLine("browser exited: " + browser.HasExited);
    await app.StopAsync();
}

return exitCode;

static async Task Write(HttpResponse response, string contentType, byte[] body)
{
    response.ContentType = contentType;
    response.Headers.CacheControl = "no-store";
    response.ContentLength = body.Length;
    await response.Body.WriteAsync(body);
}

static async Task<byte[]> ReadBodyAsync(HttpRequest request)
{
    using var copy = new MemoryStream();
    await request.Body.CopyToAsync(copy);
    return copy.ToArray();
}

static string Root()
{
    for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
    {
        if (File.Exists(Path.Combine(directory.FullName, "global.json")))
        {
            return directory.FullName;
        }
    }

    throw new InvalidOperationException("Repository root (global.json) not found.");
}

static string FindHeadlessShell()
{
    // Playwright's browser cache, as the other spikes use; no download.
    var cache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ms-playwright");
    var found = Directory.Exists(cache)
        ? Directory.GetDirectories(cache, "chromium_headless_shell-*").Order().LastOrDefault()
        : null;
    var exe = found is null ? null : Path.Combine(found, "chrome-headless-shell-win64", "chrome-headless-shell.exe");
    return exe is not null && File.Exists(exe) ? exe : throw new FileNotFoundException("chrome-headless-shell not found; pass --browser.");
}

/// <summary>A minimal CDP client on the page target: Performance.enable, then Performance.getMetrics.</summary>
internal sealed class Cdp : IDisposable
{
    private readonly ClientWebSocket _socket;
    private readonly SemaphoreSlim _gate = new(1);
    private int _next;
    private bool _enabled;

    private Cdp(ClientWebSocket socket) => _socket = socket;

    public static async Task<Cdp> ConnectAsync(string portFile, TimeSpan timeout)
    {
        var clock = Stopwatch.StartNew();
        string? port = null;
        while (clock.Elapsed < timeout && port is null)
        {
            try
            {
                port = File.Exists(portFile) ? File.ReadAllLines(portFile).FirstOrDefault() : null;
            }
            catch (IOException)
            {
            }

            if (port is null)
            {
                await Task.Delay(20);
            }
        }

        if (port is null)
        {
            throw new TimeoutException("No DevToolsActivePort.");
        }

        using var http = new HttpClient();
        string? url = null;
        while (clock.Elapsed < timeout && url is null)
        {
            var list = await http.GetByteArrayAsync($"http://127.0.0.1:{port}/json/list");
            url = PageSocket(list);
            if (url is null)
            {
                await Task.Delay(20);
            }
        }

        var socket = new ClientWebSocket();
        await socket.ConnectAsync(new Uri(url ?? throw new TimeoutException("No page target.")), CancellationToken.None);
        return new Cdp(socket);
    }

    public async Task<string> MetricsAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (!_enabled)
            {
                await CallAsync("Performance.enable");
                _enabled = true;
            }

            var answer = await CallAsync("Performance.getMetrics");
            var reader = new Utf8JsonReader(answer);
            string? name = null;
            double task = -1, script = -1;
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.PropertyName && reader.ValueTextEquals("name"u8))
                {
                    reader.Read();
                    name = reader.GetString();
                }
                else if (reader.TokenType == JsonTokenType.PropertyName && reader.ValueTextEquals("value"u8))
                {
                    reader.Read();
                    if (reader.TokenType == JsonTokenType.Number)
                    {
                        if (name == "TaskDuration")
                        {
                            task = reader.GetDouble();
                        }
                        else if (name == "ScriptDuration")
                        {
                            script = reader.GetDouble();
                        }
                    }
                }
            }

            return string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{{\"TaskDuration\":{task},\"ScriptDuration\":{script}}}");
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        _socket.Abort();
        _socket.Dispose();
    }

    private async Task<byte[]> CallAsync(string method)
    {
        var id = ++_next;
        await _socket.SendAsync(Encoding.UTF8.GetBytes($"{{\"id\":{id},\"method\":\"{method}\"}}"), WebSocketMessageType.Text, true, CancellationToken.None);
        var buffer = new byte[64 * 1024];
        while (true)
        {
            using var message = new MemoryStream();
            WebSocketReceiveResult received;
            do
            {
                received = await _socket.ReceiveAsync(buffer, CancellationToken.None);
                message.Write(buffer, 0, received.Count);
            }
            while (!received.EndOfMessage);

            var bytes = message.ToArray();
            var reader = new Utf8JsonReader(bytes);
            if (reader.Read() && reader.Read() && reader.TokenType == JsonTokenType.PropertyName && reader.ValueTextEquals("id"u8)
                && reader.Read() && reader.GetInt32() == id)
            {
                return bytes;
            }
        }
    }

    private static string? PageSocket(byte[] list)
    {
        var reader = new Utf8JsonReader(list);
        string? type = null, socket = null;
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.StartObject)
            {
                type = socket = null;
            }
            else if (reader.TokenType == JsonTokenType.PropertyName)
            {
                var isType = reader.ValueTextEquals("type"u8);
                var isSocket = reader.ValueTextEquals("webSocketDebuggerUrl"u8);
                reader.Read();
                if (isType)
                {
                    type = reader.GetString();
                }
                else if (isSocket)
                {
                    socket = reader.GetString();
                }
            }
            else if (reader.TokenType == JsonTokenType.EndObject && type == "page" && socket is not null)
            {
                return socket;
            }
        }

        return null;
    }
}
