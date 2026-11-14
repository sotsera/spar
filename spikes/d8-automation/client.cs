// Spike client: discovers a running D8.App devtools endpoint through its lockfile and calls its tools.
// Usage: dotnet run client.cs -- <version|tree|fill NAME TEXT [via]|click NAME [via]|waitFor TEXT|screenshot FILE|
//                                 bench N|negative|shutdown>
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

var discovery = Stopwatch.StartNew();
var session = await Discover();
Console.WriteLine($"discovered {session.Endpoint} pid={session.Pid} in {discovery.Elapsed.TotalMilliseconds:F1} ms");

var command = args.Length > 0 ? args[0] : "version";
switch (command)
{
    case "version":
        Print(await Call(session, "version", "{}"));
        break;
    case "tree":
        Print(await Call(session, "tree", "{}"));
        break;
    case "fill":
        Print(await Call(session, "fill", Json(("name", args[1]), ("text", args[2]), ("via", args.Length > 3 ? args[3] : "input"))));
        break;
    case "click":
        Print(await Call(session, "click", Json(("name", args[1]), ("via", args.Length > 2 ? args[2] : "input"))));
        break;
    case "waitFor":
        Print(await Call(session, "waitFor", Json(("text", args[1]))));
        break;
    case "screenshot":
    {
        var (body, ms) = await Call(session, "screenshot", "{}");
        using var document = JsonDocument.Parse(body);
        var png = document.RootElement.GetProperty("data").GetBytesFromBase64();
        await File.WriteAllBytesAsync(args[1], png);
        Console.WriteLine($"screenshot: {png.Length} bytes -> {args[1]} ({ms:F1} ms round trip)");
        break;
    }
    case "bench":
        await Bench(session, int.Parse(args[1]));
        break;
    case "negative":
        await Negative(session);
        break;
    case "shutdown":
        Print(await Call(session, "shutdown", "{}"));
        break;
    default:
        throw new ArgumentException("unknown command " + command);
}

static async Task<Session> Discover()
{
    var directory = Path.Combine(Path.GetTempPath(), "spar-devtools-spike");
    if (!Directory.Exists(directory))
    {
        throw new InvalidOperationException("No lockfile directory " + directory);
    }

    foreach (var file in Directory.EnumerateFiles(directory, "*.json"))
    {
        Session session;
        try
        {
            using var document = JsonDocument.Parse(await File.ReadAllTextAsync(file));
            var root = document.RootElement;
            session = new Session(root.GetProperty("endpoint").GetString()!, root.GetProperty("pid").GetInt32(),
                root.GetProperty("token").GetString()!);
        }
        catch (Exception exception) when (exception is IOException or JsonException)
        {
            continue;
        }

        // Liveness: the pid exists and an authenticated probe answers.
        try
        {
            using var process = Process.GetProcessById(session.Pid);
        }
        catch (ArgumentException)
        {
            Console.WriteLine($"stale lockfile {file} (pid {session.Pid} gone)");
            continue;
        }

        try
        {
            await Call(session, "version", "{}");
            return session;
        }
        catch (HttpRequestException exception)
        {
            Console.WriteLine($"lockfile {file}: probe failed: {exception.Message}");
        }
    }

    throw new InvalidOperationException("No live devtools endpoint found.");
}

static async Task<(string Body, double Ms)> Call(Session session, string tool, string json, string? tokenOverride = null)
{
    using var request = new HttpRequestMessage(HttpMethod.Post, session.Endpoint + tool)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };
    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenOverride ?? session.Token);
    var start = Stopwatch.GetTimestamp();
    using var response = await Http.Client.SendAsync(request);
    var body = await response.Content.ReadAsStringAsync();
    var ms = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
    if (response.StatusCode != HttpStatusCode.OK)
    {
        throw new HttpRequestException($"{tool}: {(int)response.StatusCode} {body}", null, response.StatusCode);
    }

    return (body, ms);
}

static async Task Bench(Session session, int n)
{
    var series = new Dictionary<string, List<(double Client, double Server)>>();
    async Task Run(string name, string tool, string json)
    {
        var (body, ms) = await Call(session, tool, json);
        using var document = JsonDocument.Parse(body);
        var server = document.RootElement.GetProperty("serverMs").GetDouble();
        if (!series.TryGetValue(name, out var list))
        {
            series[name] = list = [];
        }

        list.Add((ms, server));
    }

    await Run("screenshot.first", "screenshot", "{}");
    for (var i = 0; i < n; i++)
    {
        await Run("version", "version", "{}");
        await Run("tree", "tree", "{}");
        await Run("fill", "fill", Json(("name", "Name"), ("text", $"http-{i}")));
        await Run("click", "click", Json(("name", "Save")));
        await Run("waitFor", "waitFor", Json(("text", $": http-{i} (")));
        await Run("screenshot", "screenshot", "{}");
    }

    Console.WriteLine($"{"tool",-18} {"n",4} {"client med",11} {"client p95",11} {"server med",11} {"overhead med",13}");
    foreach (var (name, list) in series)
    {
        var client = list.Select(x => x.Client).Order().ToArray();
        var server = list.Select(x => x.Server).Order().ToArray();
        var overhead = list.Select(x => x.Client - x.Server).Order().ToArray();
        Console.WriteLine($"{name,-18} {list.Count,4} {Median(client),11:F2} {P95(client),11:F2} {Median(server),11:F2} {Median(overhead),13:F2}");
    }
}

static async Task Negative(Session session)
{
    await Expect("wrong token", () => Call(session, "version", "{}", tokenOverride: "nope"), HttpStatusCode.Unauthorized);

    using var rebinding = new HttpRequestMessage(HttpMethod.Post, session.Endpoint + "version");
    rebinding.Headers.Host = "evil.example:80";
    rebinding.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.Token);
    using var hostResponse = await Http.Client.SendAsync(rebinding);
    Console.WriteLine($"foreign Host header -> {(int)hostResponse.StatusCode}");

    using var browser = new HttpRequestMessage(HttpMethod.Post, session.Endpoint + "version");
    browser.Headers.Add("Origin", "http://evil.example");
    browser.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.Token);
    using var originResponse = await Http.Client.SendAsync(browser);
    Console.WriteLine($"Origin header -> {(int)originResponse.StatusCode}");
}

static async Task Expect(string name, Func<Task> action, HttpStatusCode expected)
{
    try
    {
        await action();
        Console.WriteLine($"{name}: unexpectedly succeeded");
    }
    catch (HttpRequestException exception) when (exception.StatusCode == expected)
    {
        Console.WriteLine($"{name} -> {(int)expected} as expected");
    }
}

static void Print((string Body, double Ms) result)
{
    var text = result.Body.Length > 1500 ? result.Body[..1500] + "…" : result.Body;
    Console.WriteLine($"{text}\n({result.Ms:F1} ms round trip)");
}

static string Json(params (string Key, string Value)[] pairs)
{
    using var stream = new MemoryStream();
    using (var writer = new Utf8JsonWriter(stream))
    {
        writer.WriteStartObject();
        foreach (var (key, value) in pairs)
        {
            writer.WriteString(key, value);
        }

        writer.WriteEndObject();
    }

    return Encoding.UTF8.GetString(stream.ToArray());
}

static double Median(double[] sorted) => sorted[sorted.Length / 2];

static double P95(double[] sorted) => sorted[Math.Clamp((int)Math.Ceiling(0.95 * sorted.Length) - 1, 0, sorted.Length - 1)];

internal sealed record Session(string Endpoint, int Pid, string Token);

internal static class Http
{
    public static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(30) };
}
