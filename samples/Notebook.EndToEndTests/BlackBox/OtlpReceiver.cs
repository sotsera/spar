using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Notebook.EndToEndTests;

/// <summary>
/// An OTLP/HTTP endpoint on 127.0.0.1 for one test: it keeps each request's protobuf by signal (<c>/v1/traces</c>,
/// <c>/v1/metrics</c>, <c>/v1/logs</c>) and answers 200. HTTP/1.1 with keep-alive and <c>Content-Length</c> or chunked
/// bodies, which is what .NET's exporter and the relay send.
/// </summary>
internal sealed class OtlpReceiver : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();
    private readonly ConcurrentQueue<(string Path, byte[] Body)> _requests = new();
    private readonly Task _accepting;

    public OtlpReceiver()
    {
        _listener.Start();
        _accepting = AcceptAsync();
    }

    public string Endpoint => $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}";

    public IReadOnlyList<byte[]> Bodies(string path) => [.. _requests.Where(r => r.Path == path).Select(r => r.Body)];

    /// <summary>Every span received so far, with its resource's <c>service.name</c>.</summary>
    public IReadOnlyList<ReceivedSpan> Spans() => [.. Bodies("/v1/traces").SelectMany(OtlpSpans.Read)];

    public async Task<IReadOnlyList<ReceivedSpan>> WaitForSpansAsync(Func<IReadOnlyList<ReceivedSpan>, bool> done, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            var spans = Spans();
            if (done(spans) || DateTime.UtcNow > deadline)
            {
                return spans;
            }

            await Task.Delay(100, cancellationToken);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        _listener.Stop();
        try
        {
            await _accepting;
        }
        catch (OperationCanceledException)
        {
            // Stopped.
        }

        _stop.Dispose();
    }

    private async Task AcceptAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(_stop.Token);
            }
            catch (Exception exception) when (exception is OperationCanceledException or SocketException or ObjectDisposedException)
            {
                return;
            }

            _ = ServeAsync(client);
        }
    }

    private async Task ServeAsync(TcpClient client)
    {
        using (client)
        {
            var stream = client.GetStream();
            try
            {
                while (await ReadRequestAsync(stream, _stop.Token) is { } request)
                {
                    _requests.Enqueue(request);
                    var answer = "HTTP/1.1 200 OK\r\nContent-Type: application/x-protobuf\r\nContent-Length: 0\r\n\r\n"u8.ToArray();
                    await stream.WriteAsync(answer, _stop.Token);
                }
            }
            catch (Exception exception) when (exception is IOException or OperationCanceledException or SocketException)
            {
                // The client went away, or the test ended.
            }
        }
    }

    private static async Task<(string Path, byte[] Body)?> ReadRequestAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var head = await ReadLineAsync(stream, cancellationToken);
        if (head is null or "")
        {
            return null;
        }

        var path = head.Split(' ')[1];
        var length = 0;
        var chunked = false;
        while (await ReadLineAsync(stream, cancellationToken) is { Length: > 0 } line)
        {
            var colon = line.IndexOf(':', StringComparison.Ordinal);
            var name = line[..colon].Trim();
            var value = line[(colon + 1)..].Trim();
            if (name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
            {
                length = int.Parse(value, CultureInfo.InvariantCulture);
            }
            else if (name.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase) && value.Contains("chunked", StringComparison.OrdinalIgnoreCase))
            {
                chunked = true;
            }
        }

        return (path, chunked ? await ReadChunkedAsync(stream, cancellationToken) : await ReadExactlyAsync(stream, length, cancellationToken));
    }

    private static async Task<byte[]> ReadChunkedAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        using var body = new MemoryStream();
        while (true)
        {
            var size = int.Parse((await ReadLineAsync(stream, cancellationToken))!.Split(';')[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            var chunk = await ReadExactlyAsync(stream, size, cancellationToken);
            await ReadLineAsync(stream, cancellationToken);
            if (size == 0)
            {
                return body.ToArray();
            }

            body.Write(chunk);
        }
    }

    private static async Task<byte[]> ReadExactlyAsync(NetworkStream stream, int length, CancellationToken cancellationToken)
    {
        var buffer = new byte[length];
        await stream.ReadExactlyAsync(buffer, cancellationToken);
        return buffer;
    }

    private static async Task<string?> ReadLineAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var line = new StringBuilder();
        var one = new byte[1];
        while (true)
        {
            if (await stream.ReadAsync(one, cancellationToken) == 0)
            {
                return line.Length == 0 ? null : line.ToString();
            }

            if (one[0] == '\n')
            {
                return line.ToString().TrimEnd('\r');
            }

            line.Append((char)one[0]);
        }
    }
}

/// <summary>One span as the receiver read it: ids in hexadecimal, the parent's empty for a root span.</summary>
internal sealed record ReceivedSpan(string Service, string Name, int Kind, string TraceId, string SpanId, string ParentSpanId)
{
    public const int Server = 2;
    public const int Client = 3;
}

/// <summary>
/// The few fields of OTLP's <c>ExportTraceServiceRequest</c> the tests check, read straight from the protobuf: resource
/// spans (1), their resource (1) with its attributes (1), scope spans (2), and each span's trace id (1), span id (2),
/// parent span id (4), name (5) and kind (6).
/// </summary>
internal static class OtlpSpans
{
    public static IEnumerable<ReceivedSpan> Read(byte[] request)
    {
        foreach (var resourceSpans in Fields(request, 1))
        {
            var service = Fields(resourceSpans, 1).SelectMany(resource => Fields(resource, 1)).Select(Attribute)
                .FirstOrDefault(a => a.Key == "service.name").Value ?? "";
            foreach (var span in Fields(resourceSpans, 2).SelectMany(scope => Fields(scope, 2)))
            {
                yield return new ReceivedSpan(
                    service,
                    Text(Fields(span, 5).FirstOrDefault()),
                    (int)Number(span, 6),
                    Hex(Fields(span, 1).FirstOrDefault()),
                    Hex(Fields(span, 2).FirstOrDefault()),
                    Hex(Fields(span, 4).FirstOrDefault()));
            }
        }
    }

    private static KeyValuePair<string, string?> Attribute(byte[] keyValue)
    {
        var value = Fields(keyValue, 2).FirstOrDefault();
        return new(Text(Fields(keyValue, 1).FirstOrDefault()), value is null ? null : Text(Fields(value, 1).FirstOrDefault()));
    }

    private static string Text(byte[]? bytes) => bytes is null ? "" : Encoding.UTF8.GetString(bytes);

    private static string Hex(byte[]? bytes) => bytes is null ? "" : Convert.ToHexStringLower(bytes);

    /// <summary>The length-delimited fields with this number.</summary>
    private static List<byte[]> Fields(byte[] message, int number)
    {
        var found = new List<byte[]>();
        Walk(message, (field, wireType, value, bytes) =>
        {
            if (field == number && wireType == 2)
            {
                found.Add(bytes!);
            }
        });
        return found;
    }

    private static ulong Number(byte[] message, int number)
    {
        ulong found = 0;
        Walk(message, (field, wireType, value, _) =>
        {
            if (field == number && wireType == 0)
            {
                found = value;
            }
        });
        return found;
    }

    private static void Walk(byte[] message, Action<int, int, ulong, byte[]?> visit)
    {
        var position = 0;
        while (position < message.Length)
        {
            var key = Varint(message, ref position);
            var field = (int)(key >> 3);
            var wireType = (int)(key & 7);
            switch (wireType)
            {
                case 0:
                    visit(field, wireType, Varint(message, ref position), null);
                    break;
                case 1:
                    position += 8;
                    break;
                case 2:
                    var length = (int)Varint(message, ref position);
                    visit(field, wireType, 0, message[position..(position + length)]);
                    position += length;
                    break;
                case 5:
                    position += 4;
                    break;
                default:
                    throw new InvalidDataException($"Wire type {wireType} at byte {position}.");
            }
        }
    }

    private static ulong Varint(byte[] message, ref int position)
    {
        ulong value = 0;
        for (var shift = 0; ; shift += 7)
        {
            var b = message[position++];
            value |= (ulong)(b & 0x7f) << shift;
            if ((b & 0x80) == 0)
            {
                return value;
            }
        }
    }
}
