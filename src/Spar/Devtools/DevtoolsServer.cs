using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Spar.Hosting;

namespace Spar.Devtools;

/// <summary>What the devtools need from the running app and its host.</summary>
/// <param name="Running">The app's services, dispatcher and catalog.</param>
/// <param name="Run">The run's id, as every line of its log carries it.</param>
/// <param name="Host"><c>windows</c>, <c>linux</c> or <c>loopback</c>.</param>
/// <param name="DiscoveryFile">The project's <c>obj/spar-devtools.json</c>.</param>
/// <param name="Pages">Where the page tools find the window's page.</param>
/// <param name="Log">The merged log the <c>logs</c> tool reads.</param>
internal sealed record DevtoolsContext(SparRunningApp Running, string Run, string Host, string DiscoveryFile, DevtoolsPages Pages, DevtoolsLog Log)
{
    /// <summary>
    /// Starts the host's own session end (the path of Windows' <c>WM_ENDSESSION</c> and Linux's <c>SIGTERM</c>, or
    /// loopback's Ctrl+C) without waiting for it, for <c>spar dev</c>'s restarts; false when the run cannot end that way
    /// yet. Null when the host offers none.
    /// </summary>
    public Func<bool>? EndSession { get; init; }
}

/// <summary>
/// The devtools' endpoint (plan section 6; decisions "M2's decide-first questions" and "M2a's decide-first questions"):
/// a local socket only this user can open, a discovery file with its address and token, and newline-delimited JSON-RPC
/// on each connection. A connection's first request is <c>initialize</c> with the token; every request after it may
/// name the run it is for (<c>run</c>), and a request for another run answers <c>stale_target</c>. Requests on one connection run
/// one at a time; up to <see cref="MaxConnections"/> connections run side by side.
/// </summary>
internal sealed class DevtoolsServer : IAsyncDisposable
{
    public const int Protocol = 1;

    private const int MaxConnections = 8;

    private readonly DevtoolsContext _context;
    private readonly DevtoolsListener _listener;
    private readonly DevtoolsFile _file;
    private readonly ILogger _log;
    private readonly DevtoolsTools _tools;
    private readonly CancellationTokenSource _stop = new();
    private readonly Lock _gate = new();
    private readonly HashSet<Task> _connections = [];
    private Task _accepting = Task.CompletedTask;
    private int _disposed;

    private DevtoolsServer(DevtoolsContext context, DevtoolsListener listener, DevtoolsFile file, ILogger log)
    {
        _context = context;
        _listener = listener;
        _file = file;
        _log = log;
        _tools = new DevtoolsTools(context, file);
    }

    /// <summary>
    /// Listens, writes the discovery file and accepts connections; null, with a warning, when the socket cannot be made
    /// (the app runs on without devtools).
    /// </summary>
    public static DevtoolsServer? Start(DevtoolsContext context, ILogger log)
    {
        DevtoolsListener listener;
        try
        {
            listener = DevtoolsListener.Start(context.Run);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Net.Sockets.SocketException or PlatformNotSupportedException)
        {
            log.LogWarning("The devtools could not listen ({Problem}); the app runs without them.", exception.Message);
            return null;
        }

        var catalog = context.Running.Catalog;
        var file = new DevtoolsFile(
            Environment.ProcessId, context.Run, DateTimeOffset.UtcNow, context.Host, listener.Transport, listener.Address,
            Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32)), catalog.AppId ?? "",
            Encoding.ASCII.GetString(catalog.BuildIdentity.Span));
        var server = new DevtoolsServer(context, listener, file, log);
        context.Running.Dispatcher.Recorder = server._tools.Calls;
        if (!file.Write(context.DiscoveryFile))
        {
            log.LogWarning("The devtools' discovery file {File} could not be written; tools cannot find this run.", context.DiscoveryFile);
        }

        server._accepting = Task.Run(server.AcceptAsync);
        log.LogInformation("Devtools listening ({Transport}); discovery file {File}.", listener.Transport, context.DiscoveryFile);
        return server;
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _file.DeleteIfOurs(_context.DiscoveryFile);
        await _stop.CancelAsync();
        await _listener.DisposeAsync();
        Task[] running;
        lock (_gate)
        {
            running = [_accepting, .. _connections];
        }

        // Each connection ends with its stream; none is waited for longer than a moment at exit.
        await Task.WhenAny(Task.WhenAll(running), Task.Delay(1000, CancellationToken.None));
        _stop.Dispose();
    }

    private async Task AcceptAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            Stream stream;
            try
            {
                stream = await _listener.AcceptAsync(_stop.Token);
            }
            catch (Exception exception) when (exception is OperationCanceledException or ObjectDisposedException)
            {
                return;
            }
            catch (Exception exception) when (exception is IOException or System.Net.Sockets.SocketException)
            {
                _log.LogDebug("A devtools connection failed before its first request: {Problem}", exception.Message);
                continue;
            }

            lock (_gate)
            {
                if (_connections.Count >= MaxConnections)
                {
                    stream.Dispose();
                    continue;
                }

                var connection = Task.Run(() => ServeAsync(stream));
                _connections.Add(connection);
                _ = connection.ContinueWith(done => Forget(done), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }
        }
    }

    private void Forget(Task connection)
    {
        lock (_gate)
        {
            _connections.Remove(connection);
        }
    }

    private async Task ServeAsync(Stream stream)
    {
        await using var _ = stream;
        var line = new ArrayBufferWriter<byte>();
        var buffer = new byte[16 * 1024];
        var pending = new JsonRpc.Pending();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var initialized = false;
        try
        {
            while (await JsonRpc.ReadLineAsync(stream, line, buffer, pending, _stop.Token) is { } request)
            {
                if (request.Length == 0)
                {
                    continue;
                }

                var (response, close) = await AnswerAsync(request, ids, initialized);
                initialized |= response.Initialized;
                await stream.WriteAsync(response.Bytes, _stop.Token);
                await stream.FlushAsync(_stop.Token);
                if (close)
                {
                    return;
                }
            }
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or OperationCanceledException or ObjectDisposedException)
        {
            // The client went, the line was too long, or the app is stopping: the connection ends.
        }
    }

    /// <summary>One request's response, and whether the connection ends after it (a wrong token).</summary>
    private async Task<(Answer Response, bool Close)> AnswerAsync(byte[] request, HashSet<string> ids, bool initialized)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(request);
        }
        catch (JsonException exception)
        {
            return (new Answer(JsonRpc.Error(null, JsonRpc.ParseError, "The request is not JSON: " + exception.Message, null)), false);
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("id", out var id) || !JsonRpc.IsId(id)
                || !root.TryGetProperty("method", out var method) || method.ValueKind != JsonValueKind.String
                || !(root.TryGetProperty("jsonrpc", out var version) && version.ValueEquals("2.0")))
            {
                return (new Answer(JsonRpc.Error(null, JsonRpc.InvalidRequest, "A request is {\"jsonrpc\":\"2.0\",\"id\":…,\"method\":…,\"params\":{…}} on one line.", null)), false);
            }

            if (!ids.Add(id.GetRawText()))
            {
                return (new Answer(JsonRpc.Error(id, JsonRpc.ToolError, "This id was used on this connection already; the request was not run again.", "duplicate_id")), false);
            }

            var parameters = root.TryGetProperty("params", out var given) && given.ValueKind == JsonValueKind.Object ? given : default;
            var name = method.GetString()!;
            if (!initialized)
            {
                return Initialize(id, name, parameters);
            }

            if (parameters.ValueKind == JsonValueKind.Object && parameters.TryGetProperty("run", out var run)
                && run.ValueKind == JsonValueKind.String && !run.ValueEquals(_context.Run))
            {
                return (new Answer(JsonRpc.Error(id, JsonRpc.ToolError, $"The request is for run {run.GetString()}; this is run {_context.Run}. Read obj/spar-devtools.json again.", "stale_target")), false);
            }

            return (new Answer(await _tools.RunAsync(id, name, parameters, _stop.Token)), false);
        }
    }

    private (Answer Response, bool Close) Initialize(JsonElement id, string method, JsonElement parameters)
    {
        if (method != "initialize")
        {
            return (new Answer(JsonRpc.Error(id, JsonRpc.ToolError, "The first request of a connection is initialize, with the token of obj/spar-devtools.json.", "unauthorized")), true);
        }

        var token = parameters.ValueKind == JsonValueKind.Object && parameters.TryGetProperty("token", out var given) && given.ValueKind == JsonValueKind.String
            ? given.GetString() ?? ""
            : "";
        if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(token), Encoding.UTF8.GetBytes(_file.Token)))
        {
            return (new Answer(JsonRpc.Error(id, JsonRpc.ToolError, "The token is not this run's.", "unauthorized")), true);
        }

        return (new Answer(JsonRpc.Result(id, writer =>
        {
            writer.WriteStartObject();
            writer.WriteNumber("protocol", Protocol);
            writer.WriteString("run", _context.Run);
            writer.WriteNumber("pid", Environment.ProcessId);
            writer.WriteString("host", _context.Host);
            writer.WriteString("app", _file.App);
            writer.WriteStartArray("tools");
            foreach (var tool in DevtoolsTools.Names)
            {
                writer.WriteStringValue(tool);
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }), Initialized: true), false);
    }

    private readonly record struct Answer(byte[] Bytes, bool Initialized = false);
}
