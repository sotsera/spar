using System.Buffers;
using System.Diagnostics;
using System.IO.Pipes;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace Spar.Tool.Devtools;

/// <summary>A running app as its discovery file, <c>obj/spar-devtools.json</c>, records it.</summary>
internal sealed record DevtoolsTarget(int ProcessId, string Run, string Host, string Transport, string Address, string Token, string App)
{
    /// <summary>The project's record, or null with why there is no running app to talk to.</summary>
    public static DevtoolsTarget? Find(string projectDirectory, out string problem)
    {
        var file = Path.Combine(projectDirectory, "obj", "spar-devtools.json");
        if (!File.Exists(file))
        {
            problem = $"No Development run of this app is listening ({file} is missing). Start it with dotnet run, F5 or spar dev; "
                + "a build with SparDevtools=false has no devtools.";
            return null;
        }

        DevtoolsTarget? target;
        try
        {
            target = Parse(File.ReadAllBytes(file));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            problem = $"{file} cannot be read: {exception.Message}";
            return null;
        }

        if (target is null)
        {
            problem = $"{file} is not a discovery file this tool reads; update the spar tool to the SDK's version.";
            return null;
        }

        if (!Running(target.ProcessId))
        {
            problem = $"The run recorded in {file} (process {target.ProcessId}) has ended; start the app again.";
            return null;
        }

        problem = "";
        return target;
    }

    public static DevtoolsTarget? Parse(ReadOnlySpan<byte> json)
    {
        try
        {
            var reader = new Utf8JsonReader(json);
            using var document = JsonDocument.ParseValue(ref reader);
            var root = document.RootElement;
            if (!root.TryGetProperty("version", out var version) || !version.TryGetInt32(out var number) || number != 1
                || !root.TryGetProperty("pid", out var pid) || !pid.TryGetInt32(out var processId))
            {
                return null;
            }

            string Text(string name) => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
            return Text("address") is { Length: > 0 } address && Text("token") is { Length: > 0 } token
                ? new DevtoolsTarget(processId, Text("run"), Text("host"), Text("transport"), address, token, Text("app"))
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool Running(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return false;
        }
    }
}

/// <summary>How the tool opens the app's socket; tests give their own.</summary>
internal interface IDevtoolsTransport
{
    Task<Stream> OpenAsync(DevtoolsTarget target, CancellationToken cancellationToken);
}

/// <summary>The named pipe on Windows, the Unix socket elsewhere, as the discovery file names them.</summary>
internal sealed class RealDevtoolsTransport : IDevtoolsTransport
{
    public async Task<Stream> OpenAsync(DevtoolsTarget target, CancellationToken cancellationToken)
    {
        if (target.Transport == "pipe")
        {
            var pipe = new NamedPipeClientStream(".", target.Address, PipeDirection.InOut, PipeOptions.Asynchronous);
            await pipe.ConnectAsync(cancellationToken);
            return pipe;
        }

        var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        try
        {
            await socket.ConnectAsync(new UnixDomainSocketEndPoint(target.Address), cancellationToken);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }
}

/// <summary>One JSON-RPC conversation with the app: <c>initialize</c> with the token, then the tool's request.</summary>
internal sealed class DevtoolsConnection : IAsyncDisposable
{
    private readonly Stream _stream;
    private readonly DevtoolsTarget _target;
    private readonly byte[] _buffer = new byte[64 * 1024];
    private readonly ArrayBufferWriter<byte> _line = new();
    private int _start;
    private int _count;
    private int _nextId;

    private DevtoolsConnection(Stream stream, DevtoolsTarget target)
    {
        _stream = stream;
        _target = target;
    }

    public static async Task<DevtoolsConnection> OpenAsync(IDevtoolsTransport transport, DevtoolsTarget target, CancellationToken cancellationToken)
    {
        var connection = new DevtoolsConnection(await transport.OpenAsync(target, cancellationToken), target);
        try
        {
            using var hello = await connection.RequestAsync("initialize", writer => writer.WriteString("token", target.Token), cancellationToken);
            if (hello.RootElement.TryGetProperty("error", out var error))
            {
                throw new DevtoolsCallException(error.Clone());
            }

            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    /// <summary>The response to one request: an object with <c>result</c> or <c>error</c>.</summary>
    public async Task<JsonDocument> RequestAsync(string method, Action<Utf8JsonWriter> parameters, CancellationToken cancellationToken)
    {
        var request = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(request))
        {
            writer.WriteStartObject();
            writer.WriteString("jsonrpc", "2.0");
            writer.WriteNumber("id", ++_nextId);
            writer.WriteString("method", method);
            writer.WriteStartObject("params");
            if (method != "initialize")
            {
                writer.WriteString("run", _target.Run);
            }

            parameters(writer);
            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        request.Write("\n"u8);
        await _stream.WriteAsync(request.WrittenMemory, cancellationToken);
        await _stream.FlushAsync(cancellationToken);
        return JsonDocument.Parse(await ReadLineAsync(cancellationToken));
    }

    public ValueTask DisposeAsync() => _stream.DisposeAsync();

    private async Task<ReadOnlyMemory<byte>> ReadLineAsync(CancellationToken cancellationToken)
    {
        _line.ResetWrittenCount();
        while (true)
        {
            if (_count == 0)
            {
                _count = await _stream.ReadAsync(_buffer, cancellationToken);
                _start = 0;
                if (_count == 0)
                {
                    throw new IOException("The app closed the connection before it answered.");
                }
            }

            var available = _buffer.AsSpan(_start, _count);
            var end = available.IndexOf((byte)'\n');
            _line.Write(end < 0 ? available : available[..end]);
            var consumed = end < 0 ? available.Length : end + 1;
            _start += consumed;
            _count -= consumed;
            if (end >= 0)
            {
                return _line.WrittenMemory.ToArray();
            }
        }
    }
}

/// <summary>A tool's refusal as the app sent it: <c>{code, message, data:{reason}}</c>.</summary>
internal sealed class DevtoolsCallException(JsonElement error) : Exception(Describe(error))
{
    public JsonElement Error { get; } = error;

    public string Reason => Error.TryGetProperty("data", out var data) && data.TryGetProperty("reason", out var reason)
        ? reason.GetString() ?? ""
        : "";

    private static string Describe(JsonElement error) =>
        error.TryGetProperty("message", out var message) ? message.GetString() ?? "the app refused the request" : "the app refused the request";
}

internal static class JsonText
{
    /// <summary>An element as indented JSON.</summary>
    public static string Indented(JsonElement element)
    {
        var output = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(output, new JsonWriterOptions { Indented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            element.WriteTo(writer);
        }

        return Encoding.UTF8.GetString(output.WrittenSpan);
    }
}
