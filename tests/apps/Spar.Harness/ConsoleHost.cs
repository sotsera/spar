using System.Text;
using System.Text.Json;
using Spar.Bridge;

namespace Spar.Harness;

/// <summary>
/// A host with no window: it builds the app as a desktop host will, then writes what the core does to standard
/// output, one line per event, so a CoreCLR run and a NativeAOT run can be compared byte for byte. Its argument is
/// the path of the shared conformance vectors (<c>tests/conformance/bridge-messages.json</c>). The exit code is 0
/// when every vector passed, 1 when one failed, and 2 for a wrong command line.
/// </summary>
internal static class ConsoleHost
{
    public const int MaxMessageBytes = 64 * 1024;

    /// <summary>One trace context for every call, so the correlation ids in failures are the same in every run.</summary>
    private const string TraceParent = "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01";

    private const string ShapesRequest =
        """{"text":"café 😀 lone \ud800 end","note":null,"flag":true,"count":-2147483648,"big":"9223372036854775807","ratio":0.1,"key":"0f8fad5b-d9cb-469f-a165-70867728950e","at":"2026-11-16T10:20:30.1234567+01:00","maybe":42,"tags":["a","b"],"cells":["x",null],"items":[{"name":"one"}],"extra":{"deep":[1,2]},"parent":{"text":"p","flag":false,"count":0,"big":"-1","ratio":1e308,"key":"00000000-0000-0000-0000-000000000000","at":"2000-01-01T00:00:00+00:00","tags":[],"cells":[],"items":[],"parent":null}}""";

    public static int Run(SparApp app, string[] args)
    {
        if (args.Length != 1 || !File.Exists(args[0]))
        {
            Console.Error.WriteLine("Usage: Spar.Harness <path of tests/conformance/bridge-messages.json>");
            return 2;
        }

        var output = new StringBuilder();
        var vectorsPassed = RunVectors(args[0], output);

        // A console app's main thread has no synchronization context, so waiting here blocks nothing else.
        RunSessionsAsync(app, output).GetAwaiter().GetResult();

        // UTF-8 bytes, whatever the console's code page.
        using var stdout = Console.OpenStandardOutput();
        stdout.Write(Encoding.UTF8.GetBytes(output.ToString()));
        return vectorsPassed ? 0 : 1;
    }

    /// <summary>Each vector through the codec: canonical ones round-trip exactly, malformed ones are refused.</summary>
    private static bool RunVectors(string path, StringBuilder output)
    {
        using var vectors = JsonDocument.Parse(File.ReadAllBytes(path));
        var root = vectors.RootElement;
        var passed = true;

        foreach (var vector in root.GetProperty("canonical").EnumerateArray())
        {
            var wire = vector.GetProperty("wire").GetString()!;
            var result = BridgeCodec.TryDecode(wire, out var message, out var error)
                ? BridgeCodec.EncodeToString(message!) == wire ? "ok" : "re-encoded differently"
                : $"refused: {error!.Reason}";
            passed &= result == "ok";
            Line(output, $"canonical {Name(vector)}: {result}");
        }

        foreach (var vector in root.GetProperty("decode").EnumerateArray())
        {
            var wire = vector.GetProperty("wire").GetString()!;
            var decoded = BridgeCodec.TryDecode(wire, out var message, out var error);
            passed &= decoded;
            Line(output, $"decode {Name(vector)}: {(decoded ? Describe(message!) : $"refused: {error!.Reason}")}");
        }

        foreach (var vector in root.GetProperty("malformed").EnumerateArray())
        {
            var wire = vector.GetProperty("wire").GetString()!;
            var decoded = BridgeCodec.TryDecode(wire, out _, out var error);
            passed &= !decoded;
            Line(output, $"malformed {Name(vector)}: {(decoded ? "accepted" : $"refused, id {error!.Id?.ToString() ?? "none"}")}");
        }

        return passed;
    }

    /// <summary>The app built as a host builds it, then one page that knows the contracts and one that does not.</summary>
    private static async Task RunSessionsAsync(SparApp app, StringBuilder output)
    {
        var builder = app.Builder;
        var catalog = app.Catalog;

        // The harness writes no file; the folders only have to exist as values.
        var folders = SparFolders.Under(Path.Combine(AppContext.BaseDirectory, "data"), builder.FolderOptions);
        SparRunningApp.AddSparServices(builder.Services, catalog, folders, builder.BridgeOptions.Freeze());
        await using var running = SparRunningApp.Build(builder.Services, catalog, folders);

        Line(output, "session: the page's contracts match");
        await RunSessionAsync(running, Script(catalog.Fingerprint), output);

        Line(output, "session: the page was built against other contracts");
        await RunSessionAsync(running, [new(Hello("0000000000000000")), new(Call(1, "check", """{"name":"ok"}"""))], output);
    }

    /// <summary>Feeds the steps to one document's session, writing each message in and everything the host sends.</summary>
    private static async Task RunSessionAsync(SparRunningApp running, IReadOnlyList<Step> steps, StringBuilder output)
    {
        var sent = new List<string>();
        using var session = new BridgeSession(
            running.Dispatcher,
            running.Catalog.Fingerprint,
            "harness",
            message => Add(sent, $"< {message}"),
            includeDetails: false);
        session.Dropped += reason => Add(sent, $"dropped: {reason}");

        foreach (var step in steps)
        {
            Line(output, $"> {Shorten(step.Message)}");
            session.Receive(step.Message);
            if (!step.Settle)
            {
                continue;
            }

            // Every command it started has sent its terminal message, so the output's order is the same in every run.
            await session.WhenIdleAsync();
            lock (sent)
            {
                sent.ForEach(line => Line(output, line));
                sent.Clear();
            }
        }

        session.Close();
    }

    /// <summary>The script for a page built against the app's own contracts.</summary>
    private static Step[] Script(string fingerprint) =>
    [
        new(Hello(fingerprint)),
        new(Call(1, "shapes", ShapesRequest)),
        new(Call(2, "check", """{"name":"ok"}""")),
        new(Call(3, "check", """{"name":""}""")),
        new(Call(4, "check", """{"name":"no"}""")),
        new(Call(5, "big", """{"name":"-9223372036854775808"}""")),
        new(Call(6, "ratio", """{"name":"NaN"}""")),
        new(Call(7, "fail", """{"name":"seven"}""")),
        new(Call(8, "missing", "{}")),
        new(Call(9, "shapes", """{"text":""")),
        new(Call(10, "wait", """{"name":"w"}"""), Settle: false),
        new("""{"type":"cancel","id":10}""" + "\t"),
        new(Call(11, "check", $$"""{"name":"{{new string('x', MaxMessageBytes)}}"}""")),
        new(Call(5, "check", """{"name":"late"}""")),
        new("""{"type":"later","id":12}""" + "\t"),
    ];

    private static string Hello(string fingerprint) =>
        $$"""{"type":"hello","protocol":1,"types":["open","result","failure","event","closing"],"contracts":"{{fingerprint}}"}""" + "\t";

    private static string Call(long id, string command, string payload) =>
        $$"""{"type":"call","id":{{id}},"contract":"echo","command":"{{command}}","traceparent":"{{TraceParent}}"}""" + "\t" + payload;

    private static string Name(JsonElement vector) => vector.GetProperty("name").GetString()!;

    /// <summary>Every field of a decoded message; a message of an unknown type decodes but cannot be encoded.</summary>
    private static string Describe(BridgeMessage message) => string.Join(
        "|",
        message.TypeName ?? BridgeMessage.TypeNameOf(message.Type),
        message.Id,
        message.Protocol,
        message.Types is null ? "" : string.Join(",", message.Types),
        message.Contracts,
        message.Doc,
        message.Contract,
        message.Command,
        message.TraceParent,
        message.TraceState,
        message.Credits,
        message.Ok,
        message.Dropped,
        message.Kind,
        message.Code,
        message.Execution,
        message.ItemsSent,
        message.Name,
        Encoding.UTF8.GetString(message.Payload.Span));

    private static string Shorten(string message) =>
        message.Length <= 400 ? message : $"{message[..120]}… ({Encoding.UTF8.GetByteCount(message)} bytes)";

    private static void Add(List<string> sent, string line)
    {
        lock (sent)
        {
            sent.Add(line);
        }
    }

    /// <summary>LF only, so the output is the same on every OS.</summary>
    private static void Line(StringBuilder output, string line) => output.Append(line).Append('\n');

    /// <summary>A message for the session; <paramref name="Settle"/> waits for what it started before the next.</summary>
    private sealed record Step(string Message, bool Settle = true);
}
