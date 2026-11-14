#if SPAR_DEVTOOLS && SPAR_DEVTOOLS_SERVER
using System.Buffers;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace D8.App.Devtools;

/// <summary>
/// Minimal loopback HTTP/1.1 JSON endpoint. The socket binds 127.0.0.1:0, so the OS picks the port atomically
/// and there is no choose-then-bind race. Each request needs the per-launch bearer token and a loopback Host.
/// </summary>
internal static class Server
{
    private const int MaxRequestBytes = 1 << 20;

    public static async Task RunAsync(Host host)
    {
        var automation = new Automation(host.WebView);
        var token = Base64Url(RandomNumberGenerator.GetBytes(32));
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var endpoint = $"http://127.0.0.1:{port}/";
        var lockfile = Lockfile.Write(endpoint, token);
        Console.WriteLine($"devtools listening on {endpoint}; lockfile {lockfile}");
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Lockfile.Delete(lockfile);

        using var stopping = new CancellationTokenSource();
        var accept = Task.Run(() => AcceptLoopAsync(listener, port, token, host, automation, stopping), CancellationToken.None);
        try
        {
            await accept;
        }
        finally
        {
            listener.Stop();
            Lockfile.Delete(lockfile);
        }
    }

    private static async Task AcceptLoopAsync(
        TcpListener listener, int port, string token, Host host, Automation automation, CancellationTokenSource stopping)
    {
        while (!stopping.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync(stopping.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            _ = HandleAsync(client, port, token, host, automation, stopping);
        }
    }

    private static async Task HandleAsync(
        TcpClient client, int port, string token, Host host, Automation automation, CancellationTokenSource stopping)
    {
        using (client)
        {
            var stream = client.GetStream();
            int status;
            string body;
            try
            {
                var request = await ReadRequestAsync(stream, stopping.Token);
                (status, body) = await RouteAsync(request, port, token, host, automation, stopping);
            }
            catch (Exception exception)
            {
                (status, body) = (500, Error(exception.GetType().Name + ": " + exception.Message));
            }

            var payload = Encoding.UTF8.GetBytes(body);
            var head = Encoding.ASCII.GetBytes(
                $"HTTP/1.1 {status} {(status == 200 ? "OK" : "Error")}\r\nContent-Type: application/json\r\n" +
                $"Content-Length: {payload.Length}\r\nConnection: close\r\n\r\n");
            await stream.WriteAsync(head, CancellationToken.None);
            await stream.WriteAsync(payload, CancellationToken.None);
        }
    }

    private static async Task<(int Status, string Body)> RouteAsync(
        Request request, int port, string token, Host host, Automation automation, CancellationTokenSource stopping)
    {
        // DNS-rebinding guard: only loopback Host headers for this port. No Origin means no browser page.
        var hostHeader = request.Headers.GetValueOrDefault("host");
        if (hostHeader != $"127.0.0.1:{port}" && hostHeader != $"localhost:{port}")
        {
            return (403, Error("bad host"));
        }

        if (request.Headers.ContainsKey("origin"))
        {
            return (403, Error("browser origins are rejected"));
        }

        var authorization = request.Headers.GetValueOrDefault("authorization") ?? "";
        var expected = "Bearer " + token;
        if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(authorization), Encoding.UTF8.GetBytes(expected)))
        {
            return (401, Error("missing or wrong bearer token"));
        }

        using var arguments = JsonDocument.Parse(request.Body.Length == 0 ? "{}" : request.Body);
        var args = arguments.RootElement;
        var start = Stopwatch.GetTimestamp();
        var result = request.Path switch
        {
            "/version" => Json(w =>
            {
                w.WriteNumber("pid", Environment.ProcessId);
                w.WriteString("variant", "spike");
                w.WriteNumber("schema", 1);
            }),
            "/tree" => await host.Ui.InvokeAsync(async () => TreeJson(await automation.TreeAsync())),
            "/fill" => await host.Ui.InvokeAsync(async () =>
            {
                var tree = await automation.TreeAsync();
                var target = Automation.Find(tree, "textbox", args.GetProperty("name").GetString()!);
                var text = args.GetProperty("text").GetString()!;
                if (Via(args) == "script")
                {
                    await automation.FillWithScriptAsync("name", text);
                }
                else
                {
                    await automation.FillWithInputAsync(target.BackendNodeId, text);
                }

                return Json(w => w.WriteNumber("id", target.BackendNodeId));
            }),
            "/click" => await host.Ui.InvokeAsync(async () =>
            {
                var tree = await automation.TreeAsync();
                var target = Automation.Find(tree, "button", args.GetProperty("name").GetString()!);
                if (Via(args) == "script")
                {
                    await automation.ClickWithScriptAsync("save");
                }
                else
                {
                    await automation.ClickWithInputAsync(target.BackendNodeId);
                }

                return Json(w => w.WriteNumber("id", target.BackendNodeId));
            }),
            "/waitFor" => await host.Ui.InvokeAsync(async () =>
            {
                var text = await automation.WaitForTextWithScriptAsync(
                    "status",
                    args.GetProperty("text").GetString()!,
                    args.TryGetProperty("timeoutMs", out var timeout) ? timeout.GetInt32() : 5000);
                return Json(w => w.WriteString("text", text));
            }),
            "/screenshot" => await host.Ui.InvokeAsync(async () =>
            {
                var png = await automation.ScreenshotAsync();
                return Json(w =>
                {
                    w.WriteString("mimeType", "image/png");
                    w.WriteBase64String("data", png);
                });
            }),
            "/shutdown" => Shutdown(host, stopping),
            _ => null,
        };

        if (result is null)
        {
            return (404, Error("unknown tool " + request.Path));
        }

        var serverMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        return (200, result.Insert(1, $"\"serverMs\":{serverMs.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)},"));
    }

    private static string Shutdown(Host host, CancellationTokenSource stopping)
    {
        stopping.Cancel();
        host.Ui.Post(static state => ((Host)state!).Close(), host);
        return Json(w => w.WriteBoolean("stopping", true));
    }

    private static string Via(JsonElement args) =>
        args.TryGetProperty("via", out var via) ? via.GetString() ?? "input" : "input";

    private static string TreeJson(TreeResult tree) => Json(w =>
    {
        w.WriteNumber("schema", 1);
        w.WriteNumber("cdpMs", tree.CdpMilliseconds);
        w.WriteNumber("rawChars", tree.RawChars);
        w.WriteStartArray("nodes");
        foreach (var node in tree.Nodes)
        {
            w.WriteStartObject();
            w.WriteString("role", node.Role);
            w.WriteString("name", node.Name);
            if (node.Value is not null)
            {
                w.WriteString("value", node.Value);
            }

            w.WriteNumber("id", node.BackendNodeId);
            w.WriteEndObject();
        }

        w.WriteEndArray();
    });

    private static string Json(Action<Utf8JsonWriter> write) => Automation.Params(write);

    private static string Error(string message) => Json(w => w.WriteString("error", message));

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static async Task<Request> ReadRequestAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(8192);
        try
        {
            var length = 0;
            var headerEnd = -1;
            while (headerEnd < 0)
            {
                if (length == buffer.Length)
                {
                    if (buffer.Length >= MaxRequestBytes)
                    {
                        throw new InvalidDataException("Request headers too large.");
                    }

                    var larger = ArrayPool<byte>.Shared.Rent(buffer.Length * 2);
                    buffer.AsSpan(0, length).CopyTo(larger);
                    ArrayPool<byte>.Shared.Return(buffer);
                    buffer = larger;
                }

                var read = await stream.ReadAsync(buffer.AsMemory(length), cancellationToken);
                if (read == 0)
                {
                    throw new EndOfStreamException();
                }

                length += read;
                headerEnd = buffer.AsSpan(0, length).IndexOf("\r\n\r\n"u8);
            }

            var head = Encoding.ASCII.GetString(buffer, 0, headerEnd).Split("\r\n");
            var requestLine = head[0].Split(' ');
            var headers = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var line in head.AsSpan(1))
            {
                var colon = line.IndexOf(':');
                if (colon > 0)
                {
                    headers[line[..colon].Trim().ToLowerInvariant()] = line[(colon + 1)..].Trim();
                }
            }

            var contentLength = headers.TryGetValue("content-length", out var value) ? int.Parse(value) : 0;
            if (contentLength > MaxRequestBytes)
            {
                throw new InvalidDataException("Request body too large.");
            }

            var body = new byte[contentLength];
            var already = Math.Min(length - (headerEnd + 4), contentLength);
            buffer.AsSpan(headerEnd + 4, already).CopyTo(body);
            await stream.ReadExactlyAsync(body.AsMemory(already), cancellationToken);
            return new Request(requestLine[0], requestLine[1], headers, Encoding.UTF8.GetString(body));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private sealed record Request(string Method, string Path, Dictionary<string, string> Headers, string Body);
}

/// <summary>Discovery file: %TEMP%/spar-devtools-spike/&lt;hash of the executable path&gt;.json.</summary>
internal static class Lockfile
{
    public static string Directory => Path.Combine(Path.GetTempPath(), "spar-devtools-spike");

    public static string Write(string endpoint, string token)
    {
        System.IO.Directory.CreateDirectory(Directory);
        var identity = Environment.ProcessPath ?? AppContext.BaseDirectory;
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(identity.ToUpperInvariant())))[..16];
        var path = Path.Combine(Directory, hash + ".json");
        var json = Automation.Params(w =>
        {
            w.WriteString("endpoint", endpoint);
            w.WriteNumber("pid", Environment.ProcessId);
            w.WriteString("token", token);
            w.WriteString("executable", identity);
            w.WriteString("startedAt", DateTimeOffset.UtcNow);
        });

        // Write-then-rename so a reader never sees a partial file.
        var temporary = path + "." + Environment.ProcessId + ".tmp";
        File.WriteAllText(temporary, json);
        File.Move(temporary, path, overwrite: true);
        return path;
    }

    public static void Delete(string path)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            if (document.RootElement.GetProperty("pid").GetInt32() == Environment.ProcessId)
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // Another instance may own it now; leave it.
        }
    }
}
#endif
