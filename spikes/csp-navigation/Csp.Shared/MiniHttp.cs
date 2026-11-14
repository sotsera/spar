using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Csp.Shared;

public sealed record MiniRequest(string Method, string Target, Dictionary<string, string> Headers, byte[] Body)
{
    public string? Header(string name) => Headers.TryGetValue(name, out var value) ? value : null;
}

/// <summary>
/// A minimal HTTP/1.1 server on 127.0.0.1 (one request per connection, Connection: close). Spike only: it serves the
/// "external" origin, and the app itself in loopback mode.
/// </summary>
public sealed class MiniHttp : IDisposable
{
    private readonly TcpListener _listener;
    private readonly Func<MiniRequest, HostResponse> _handler;

    public MiniHttp(int port, Func<MiniRequest, HostResponse> handler)
    {
        _listener = new TcpListener(IPAddress.Loopback, port);
        _handler = handler;
        _listener.Start();
        _ = AcceptAsync();
    }

    public void Dispose() => _listener.Stop();

    private async Task AcceptAsync()
    {
        while (true)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync();
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (SocketException)
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
                var request = await ReadRequestAsync(stream);
                if (request is null)
                {
                    return;
                }

                var response = _handler(request);
                var head = new StringBuilder();
                head.Append("HTTP/1.1 ").Append(response.Status).Append(' ').Append(response.Reason).Append("\r\n");
                head.Append("Content-Type: ").Append(response.ContentType).Append("\r\n");
                head.Append("Content-Length: ").Append(response.Body.Length).Append("\r\n");
                head.Append("Connection: close\r\n");
                foreach (var (name, value) in response.Headers)
                {
                    head.Append(name).Append(": ").Append(value).Append("\r\n");
                }

                head.Append("\r\n");
                await stream.WriteAsync(Encoding.ASCII.GetBytes(head.ToString()));
                if (request.Method != "HEAD")
                {
                    await stream.WriteAsync(response.Body);
                }
            }
            catch (IOException)
            {
                // The client went away.
            }
        }
    }

    private static async Task<MiniRequest?> ReadRequestAsync(NetworkStream stream)
    {
        var buffer = new byte[64 * 1024];
        var length = 0;
        int headerEnd;
        while ((headerEnd = IndexOf(buffer.AsSpan(0, length), "\r\n\r\n"u8)) < 0)
        {
            if (length == buffer.Length)
            {
                return null;
            }

            var read = await stream.ReadAsync(buffer.AsMemory(length));
            if (read == 0)
            {
                return null;
            }

            length += read;
        }

        var lines = Encoding.ASCII.GetString(buffer, 0, headerEnd).Split("\r\n");
        var start = lines[0].Split(' ');
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines.Skip(1))
        {
            var colon = line.IndexOf(':', StringComparison.Ordinal);
            if (colon > 0)
            {
                headers[line[..colon].Trim()] = line[(colon + 1)..].Trim();
            }
        }

        var bodyLength = headers.TryGetValue("Content-Length", out var value) && int.TryParse(value, out var n) ? n : 0;
        var body = new byte[bodyLength];
        var have = Math.Min(bodyLength, length - headerEnd - 4);
        Array.Copy(buffer, headerEnd + 4, body, 0, have);
        while (have < bodyLength)
        {
            var read = await stream.ReadAsync(body.AsMemory(have));
            if (read == 0)
            {
                break;
            }

            have += read;
        }

        return new MiniRequest(start[0], start.Length > 1 ? start[1] : "/", headers, body);
    }

    private static int IndexOf(ReadOnlySpan<byte> data, ReadOnlySpan<byte> value) => data.IndexOf(value);
}
