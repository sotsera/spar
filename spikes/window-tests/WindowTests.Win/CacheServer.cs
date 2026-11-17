using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Spar.WindowTests;

/// <summary>
/// A tiny HTTP/1.1 server on 127.0.0.1 for the HTTP-cache isolation check: responses served through
/// <c>WebResourceRequested</c> never reach Chromium's HTTP cache (spikes/webview-profiles), a real origin does.
/// <c>/cache.html</c> loads <c>/c.js</c>, which is cacheable for a day; the server counts the requests for it.
/// </summary>
internal sealed class CacheServer : IDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();
    private int _scriptHits;

    public CacheServer()
    {
        _listener.Start();
        _ = Task.Run(AcceptLoopAsync);
    }

    public string Origin => $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}";

    public int ScriptHits => Volatile.Read(ref _scriptHits);

    public void Dispose()
    {
        _stop.Cancel();
        _listener.Stop();
    }

    private async Task AcceptLoopAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(_stop.Token);
            }
            catch (Exception exception) when (exception is OperationCanceledException or ObjectDisposedException or SocketException)
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
            try
            {
                var stream = client.GetStream();
                var buffer = new byte[8192];
                var read = 0;
                while (read < buffer.Length)
                {
                    var count = await stream.ReadAsync(buffer.AsMemory(read));
                    if (count == 0)
                    {
                        return;
                    }

                    read += count;
                    if (Encoding.ASCII.GetString(buffer, 0, read).Contains("\r\n\r\n", StringComparison.Ordinal))
                    {
                        break;
                    }
                }

                var requestLine = Encoding.ASCII.GetString(buffer, 0, read).Split("\r\n")[0];
                var path = requestLine.Split(' ') is { Length: >= 2 } parts ? parts[1] : "/";
                string body;
                string type;
                var cache = "no-store";
                switch (path)
                {
                    case "/cache.html":
                        body = "<!doctype html><title>cache</title><script src=\"/c.js\"></script><p>cache probe</p>";
                        type = "text/html; charset=utf-8";
                        break;
                    case "/c.js":
                        Interlocked.Increment(ref _scriptHits);
                        body = "window.cacheProbe = 1;";
                        type = "text/javascript";
                        cache = "public, max-age=86400";
                        break;
                    default:
                        body = "";
                        type = "text/plain";
                        break;
                }

                var status = body.Length == 0 ? "404 Not Found" : "200 OK";
                var content = Encoding.UTF8.GetBytes(body);
                var head = $"HTTP/1.1 {status}\r\nContent-Type: {type}\r\nContent-Length: {content.Length}\r\nCache-Control: {cache}\r\n" +
                    $"Last-Modified: Mon, 02 Nov 2026 00:00:00 GMT\r\nConnection: close\r\n\r\n";
                await stream.WriteAsync(Encoding.ASCII.GetBytes(head));
                await stream.WriteAsync(content);
            }
            catch (IOException)
            {
            }
        }
    }
}
