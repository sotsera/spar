using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Spike;

namespace Loop.Loopback;

/// <summary>What the loopback host needs from the application: its generated catalog, manifest and packaged frontend.</summary>
public sealed record LoopbackApplication(
    Func<string, string, ReadOnlyMemory<byte>, CancellationToken, ValueTask<byte[]>> Invoke,
    string Manifest,
    Func<Stream> OpenFrontend,
    string? ProjectDirectory = null,
    Func<string, string, ReadOnlyMemory<byte>, CancellationToken, SparStream>? OpenStream = null);

/// <summary>
/// Serves the packaged SPA and the command bridge on 127.0.0.1 with a random port and a per-run token.
/// Assets are public (they ship inside the executable anyway); every bridge call needs the token, a matching
/// contract fingerprint and, when the browser sends one, the host's own origin.
/// </summary>
public static class LoopbackHost
{
    public static async Task<int> RunAsync(LoopbackApplication application, string[] args, CancellationToken cancellationToken = default)
    {
        var started = Stopwatch.StartNew();

        // A development session (dotnet watch, a dev server in front) keeps one address and token across restarts.
        // Relative session paths resolve against the project folder (a development build knows it), not the current directory.
        var sessionFile = ArgumentValue(args, "--dev-session", application.ProjectDirectory);
        var session = sessionFile is not null ? DevSession.TryRead(sessionFile) : null;
        // An orchestrator (Aspire) can assign the port and token through the environment; otherwise the session or a new one.
        var token = Environment.GetEnvironmentVariable("SPAR_LOOPBACK_TOKEN") is { Length: > 0 } assignedToken ? assignedToken
            : session?.Token ?? Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(24));
        var port = int.TryParse(Environment.GetEnvironmentVariable("SPAR_LOOPBACK_PORT"), out var assignedPort) ? assignedPort
            : session is not null && DevSession.IsPortFree(session.Port) ? session.Port : 0;
        var fingerprint = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(application.Manifest)))[..16];
        var assets = LoadAssets(application.OpenFrontend);

        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { Args = [] });
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls($"http://127.0.0.1:{port}");

        // A page that stops reading a stream (back-pressure) is not a slow attacker on loopback: never drop it for it.
        builder.WebHost.ConfigureKestrel(kestrel => kestrel.Limits.MinResponseDataRate = null);
        await using var app = builder.Build();
        string? origin = null;

        // In-flight calls, keyed by the page's document and call id. A closed connection or a stopping app cancels them.
        var calls = new SparCallRegistry();
        var channels = new System.Collections.Concurrent.ConcurrentDictionary<string, LoopbackChannel>(StringComparer.Ordinal);
        var stopping = app.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping;
        stopping.Register(() => calls.NewEpoch());

        // Returns a refusal (written) or null when the request may proceed.
        async Task<bool> RefuseAsync(HttpContext context, bool checkContracts)
        {
            var request = context.Request;
            var (status, error, detail) = !IsAuthorized(request, token) ? (401, "unauthorized", (string?)null)
                : request.Headers.Origin is { Count: > 0 } sent && !string.Equals(sent.ToString(), origin, StringComparison.OrdinalIgnoreCase) ? (403, "forbidden-origin", null)
                : checkContracts && !string.Equals(request.Headers["X-Spar-Contracts"].ToString(), fingerprint, StringComparison.Ordinal) ? (409, "contract-mismatch", fingerprint)
                : (0, "", null);
            if (status == 0)
            {
                return false;
            }

            await WriteJsonAsync(context.Response, status, SparMessage.ErrorJson(error, detail));
            return true;
        }

        app.MapPost("/_spar/call/{contract}/{command}", async context =>
        {
            if (await RefuseAsync(context, checkContracts: true))
            {
                return;
            }

            var (contract, command) = ((string)context.Request.RouteValues["contract"]!, (string)context.Request.RouteValues["command"]!);
            var body = await ReadBodyAsync(context);
            var call = calls.Start(Document(context), CallId(context));
            if (call is null)
            {
                await WriteJsonAsync(context.Response, 400, SparMessage.ErrorJson("cancelled-before-start", null));
                return;
            }

            // The page went away: nobody can receive the outcome, so stop the work.
            using var aborted = context.RequestAborted.Register(call.Cancel);
            var timer = Stopwatch.StartNew();
            var reply = await SparDispatch.InvokeAsync(application.Invoke, call, contract, command, body);
            calls.End(call);
            Console.WriteLine($"SPAR_CALL id={call.Id} {contract}.{command} {(reply.Kind == 'r' ? "ok" : reply.Outcome.ToString())} {timer.ElapsedMilliseconds}ms in={body.Length}B out={reply.Json.Length}B{CancelLatency(call, reply)}");
            var status = reply.Kind switch
            {
                'r' => 200,
                'c' => 499,
                'n' when reply.Field == "unknown-command" => 404,
                'n' => 400,
                _ => 500,
            };
            await WriteJsonAsync(context.Response, status, reply.Json);
        });

        // One streamed response per call: NDJSON lines in the bridge protocol. Back-pressure comes from TCP.
        app.MapPost("/_spar/stream/{contract}/{command}", async context =>
        {
            if (await RefuseAsync(context, checkContracts: true))
            {
                return;
            }

            var (contract, command) = ((string)context.Request.RouteValues["contract"]!, (string)context.Request.RouteValues["command"]!);
            var body = await ReadBodyAsync(context);
            var batch = int.TryParse(context.Request.Headers["X-Spar-Batch"], out var size) ? Math.Clamp(size, 1, 10_000) : 100;
            var call = calls.Start(Document(context), CallId(context));
            if (call is null)
            {
                await WriteJsonAsync(context.Response, 400, SparMessage.ErrorJson("cancelled-before-start", null));
                return;
            }

            context.Response.ContentType = "application/x-ndjson";
            context.Response.Headers.CacheControl = "no-store";
            await context.Response.StartAsync();
            using var aborted = context.RequestAborted.Register(() =>
            {
                Console.WriteLine($"SPAR_CONNECTION_LOST id={call.Id}");
                call.Cancel();
            });
            var writer = context.Response.BodyWriter;
            await SparDispatch.PumpAsync(
                cancellationToken => OpenStream(application, contract, command, body, cancellationToken),
                call,
                batch,
                useCredits: false,
                async (message, cancellationToken) =>
                {
                    LogFinal(call, message);

                    // The whole line enters the pipe at once. The flush never takes the call's token: cancelling a Kestrel
                    // flush aborts the connection, and with it the confirmation. A page that cancels keeps reading, so it completes.
                    message.WriteTo(writer, newline: true);
                    await writer.FlushAsync(CancellationToken.None);
                });
            calls.End(call);
        });

        // The message channel: one long-lived streamed response per document, carrying every channel stream.
        app.MapPost("/_spar/channel", async context =>
        {
            if (await RefuseAsync(context, checkContracts: false))
            {
                return;
            }

            var document = Document(context);
            context.Response.ContentType = "application/x-ndjson";
            context.Response.Headers.CacheControl = "no-store";
            await context.Response.StartAsync();
            var channel = new LoopbackChannel(context.Response.BodyWriter);
            channels[document] = channel;
            await channel.SendAsync(new SparMessage('o', document, null, default), CancellationToken.None);

            using var closed = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, stopping);
            await Task.Delay(Timeout.Infinite, closed.Token).ContinueWith(static _ => { }, TaskScheduler.Default);
            channels.TryRemove(new KeyValuePair<string, LoopbackChannel>(document, channel));
            var cancelled = calls.CancelDocument(document);
            Console.WriteLine($"SPAR_CHANNEL closed document={document} cancelled={cancelled}");
        });

        // Page-to-host messages for the channel and for cancellation: one or more lines.
        app.MapPost("/_spar/send", async context =>
        {
            if (await RefuseAsync(context, checkContracts: false))
            {
                return;
            }

            var document = Document(context);
            var text = System.Text.Encoding.UTF8.GetString(await ReadBodyAsync(context));
            foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var fields = line.Split(' ', line[0] switch { 's' => 8, 'k' => 3, _ => 2 });
                switch (line[0])
                {
                    case 'x' when fields.Length == 2:
                        Console.WriteLine($"SPAR_CANCEL id={fields[1]}");
                        calls.Cancel(document, fields[1]);
                        break;
                    case 'k' when fields.Length == 3 && int.TryParse(fields[2], out var credits):
                        calls.Find(document, fields[1])?.AddCredits(credits);
                        break;
                    case 's' when fields.Length == 8 && int.TryParse(fields[5], out var batch) && int.TryParse(fields[6], out var window):
                        StartChannelStream(document, fields[1], fields[2], fields[3], fields[4], Math.Clamp(batch, 1, 10_000), window, fields[7]);
                        break;
                }
            }

            context.Response.StatusCode = 204;
        });

        void StartChannelStream(string document, string id, string contract, string command, string sentFingerprint, int batch, int window, string body)
        {
            if (!channels.TryGetValue(document, out var channel))
            {
                Console.WriteLine($"SPAR_STREAM id={id} no-channel document={document}");
                return;
            }

            var call = calls.Start(document, id);
            if (call is null)
            {
                _ = channel.SendAsync(SparMessage.NotExecuted(id, "cancelled-before-start", null), CancellationToken.None).AsTask();
                return;
            }

            if (sentFingerprint != fingerprint)
            {
                calls.End(call);
                _ = channel.SendAsync(SparMessage.NotExecuted(id, "contract-mismatch", fingerprint), CancellationToken.None).AsTask();
                return;
            }

            var payload = System.Text.Encoding.UTF8.GetBytes(body);
            call.AddCredits(window);
            _ = Task.Run(async () =>
            {
                await SparDispatch.PumpAsync(
                    cancellationToken => OpenStream(application, contract, command, payload, cancellationToken),
                    call,
                    batch,
                    useCredits: true,
                    (message, cancellationToken) =>
                    {
                        LogFinal(call, message);
                        return channel.SendAsync(message, cancellationToken);
                    });
                calls.End(call);
            }, CancellationToken.None);
        }

        app.MapPost("/_spar/ready", (HttpRequest request) =>
        {
            if (!IsAuthorized(request, token))
            {
                return Results.StatusCode(401);
            }

            Console.WriteLine($"SPAR_UI_READY at={started.ElapsedMilliseconds}ms");
            return Results.NoContent();
        });

        app.MapGet("/{**path}", (HttpContext context, string? path) =>
        {
            // Assets come from memory, so caching saves nothing; no-store guarantees the page always runs the
            // frontend packaged in this executable, never one cached from an earlier build.
            context.Response.Headers.CacheControl = "no-store";
            var key = string.IsNullOrEmpty(path) ? "index.html" : path;
            return assets.TryGetValue(key, out var bytes)
                ? Results.Bytes(bytes, ContentType(key))
                : Results.NotFound();
        });

        await app.StartAsync(cancellationToken);
        var address = app.Services.GetRequiredService<IServer>().Features.GetRequiredFeature<IServerAddressesFeature>().Addresses.First();
        origin = address.TrimEnd('/');
        if (sessionFile is not null)
        {
            DevSession.Write(sessionFile, new DevSession(new Uri(origin).Port, token, origin, fingerprint, Environment.ProcessId));
        }

        var reused = session is not null && port != 0 ? " session=reused" : sessionFile is not null ? " session=new" : string.Empty;
        var hooks = SparTestHooks.Current;
        if (hooks is not null)
        {
            // In-process test factory: report to the test instead of the console, and stop when the test says so.
            hooks.Started?.Invoke(new LoopbackStarted($"{origin}/#token={token}", token, fingerprint, hooks.Services));
        }
        else
        {
            Console.WriteLine($"SPAR_READY url={origin}/#token={token} build={fingerprint} pid={Environment.ProcessId} at={started.ElapsedMilliseconds}ms{reused}");
        }

        // Launched by a tool: exit when that tool closes our standard input (it ended, crashed or wants us gone).
        var stdinClosed = hooks is not null
            ? Task.Delay(Timeout.Infinite, hooks.Stop).ContinueWith(_ => { }, TaskScheduler.Default)
            : args.Contains("--exit-with-stdin")
                ? Task.Run(() => { while (Console.In.ReadLine() is not null) { } }, CancellationToken.None)
                : Task.Delay(Timeout.Infinite, cancellationToken);
        await Task.WhenAny(stdinClosed, app.WaitForShutdownAsync(cancellationToken));
        await app.StopAsync(CancellationToken.None);
        return 0;
    }


    private static SparStream OpenStream(LoopbackApplication application, string contract, string command, byte[] body, CancellationToken cancellationToken) =>
        application.OpenStream is { } open
            ? open(contract, command, body, cancellationToken)
            : throw new SparUnknownCommandException("This app has no streaming commands.");

    private static string Document(HttpContext context) =>
        context.Request.Headers["X-Spar-Document"] is { Count: 1 } document ? document.ToString() : "-";

    private static string CallId(HttpContext context) =>
        context.Request.Headers["X-Spar-Call"] is { Count: 1 } id ? id.ToString() : "h" + Guid.NewGuid().ToString("N")[..8];

    private static async Task<byte[]> ReadBodyAsync(HttpContext context)
    {
        using var body = new MemoryStream();
        await context.Request.Body.CopyToAsync(body, context.RequestAborted);
        return body.ToArray();
    }

    private static async Task WriteJsonAsync(HttpResponse response, int status, ReadOnlyMemory<byte> json)
    {
        response.StatusCode = status;
        response.ContentType = "application/json";
        await response.Body.WriteAsync(json);
    }

    private static void LogFinal(SparCall call, SparMessage message)
    {
        if (message.Kind is 'c' or 'e' or 'n' or 'u')
        {
            Console.WriteLine($"SPAR_STREAM id={call.Id} {message.Outcome}{CancelLatency(call, message)}");
        }
    }

    private static string CancelLatency(SparCall call, SparMessage message) =>
        call.CancelRequestedAt == 0 ? string.Empty
            : $" cancel->{message.Outcome}={Stopwatch.GetElapsedTime(call.CancelRequestedAt).TotalMilliseconds:F2}ms";

    /// <summary>One document's downlink: messages from many streams, written one at a time.</summary>
    private sealed class LoopbackChannel(System.IO.Pipelines.PipeWriter writer)
    {
        private readonly SemaphoreSlim _write = new(1, 1);

        public async ValueTask SendAsync(SparMessage message, CancellationToken cancellationToken)
        {
            await _write.WaitAsync(cancellationToken);
            try
            {
                message.WriteTo(writer, newline: true);
                // Never cancel a flush: it would abort the shared connection (see the stream endpoint).
                await writer.FlushAsync(CancellationToken.None);
            }
            finally
            {
                _write.Release();
            }
        }
    }

    private static string? ArgumentValue(string[] args, string name, string? baseDirectory)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? Path.GetFullPath(args[index + 1], baseDirectory ?? Environment.CurrentDirectory) : null;
    }

    private static bool IsAuthorized(HttpRequest request, string token)
    {
        var header = request.Headers.Authorization.ToString();
        var expected = "Bearer " + token;
        return header.Length == expected.Length
            && CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(header), Encoding.ASCII.GetBytes(expected));
    }

    private static Dictionary<string, byte[]> LoadAssets(Func<Stream> open)
    {
        using var stream = open();
        using var zip = new ZipArchive(stream);
        var assets = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var entry in zip.Entries)
        {
            using var content = entry.Open();
            using var copy = new MemoryStream();
            content.CopyTo(copy);
            assets[entry.FullName] = copy.ToArray();
        }

        return assets;
    }

    private static string ContentType(string path) => Path.GetExtension(path) switch
    {
        ".html" => "text/html; charset=utf-8",
        ".js" => "text/javascript; charset=utf-8",
        ".css" => "text/css; charset=utf-8",
        ".json" => "application/json",
        ".svg" => "image/svg+xml",
        _ => "application/octet-stream",
    };
}

public sealed record ErrorBody(string Error, string? Detail);

/// <summary>What a development session keeps across backend restarts; the dev server reads Backend from it.</summary>
public sealed record DevSession(int Port, string Token, string Backend, string Build, int Pid)
{
    public static DevSession? TryRead(string path)
    {
        try
        {
            return File.Exists(path) ? System.Text.Json.JsonSerializer.Deserialize(File.ReadAllText(path), LoopbackJson.Default.DevSession) : null;
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    public static void Write(string path, DevSession session)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, System.Text.Json.JsonSerializer.Serialize(session, LoopbackJson.Default.DevSession));
        File.Move(temporary, path, overwrite: true);
    }

    public static bool IsPortFree(int port)
    {
        try
        {
            using var probe = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, port);
            probe.Start();
            return true;
        }
        catch (System.Net.Sockets.SocketException)
        {
            return false;
        }
    }
}

[System.Text.Json.Serialization.JsonSerializable(typeof(ErrorBody))]
[System.Text.Json.Serialization.JsonSerializable(typeof(DevSession))]
[System.Text.Json.Serialization.JsonSourceGenerationOptions(System.Text.Json.JsonSerializerDefaults.Web)]
internal sealed partial class LoopbackJson : System.Text.Json.Serialization.JsonSerializerContext;
