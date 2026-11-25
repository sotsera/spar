using System.Buffers;
using System.Text.Json;

namespace Spar.Devtools;

/// <summary>
/// A tool's refusal, as a JSON-RPC error with a stable reason in <c>data.reason</c> (the research review's devtools
/// rules): <c>unauthorized</c>, <c>stale_target</c>, <c>duplicate_id</c>, <c>timeout</c> (unfinished, never failed),
/// <c>not_found</c>, <c>ambiguous</c>, <c>no_page</c>, <c>unavailable</c>, <c>invalid</c>.
/// </summary>
internal sealed class DevtoolsError(string reason, string message, int code = JsonRpc.ToolError) : Exception(message)
{
    public string Reason { get; } = reason;

    public int Code { get; } = code;

    /// <summary>More of the answer for the reader, such as the elements an ambiguous target matched; written as given.</summary>
    public Action<Utf8JsonWriter>? Details { get; init; }
}

/// <summary>
/// Newline-delimited JSON-RPC 2.0 (decision "M2a's decide-first questions", item 3): one request per line, one response
/// per line, ids unique per connection. Notifications (no id) are not used, and a batch is refused.
/// </summary>
internal static class JsonRpc
{
    public const int ParseError = -32700;
    public const int InvalidRequest = -32600;
    public const int MethodNotFound = -32601;
    public const int InvalidParams = -32602;
    public const int ToolError = -32000;

    /// <summary>The longest request line read; a longer one ends the connection.</summary>
    public const int MaxRequest = 1024 * 1024;

    /// <summary>
    /// The next line from <paramref name="stream"/>, without its line end; null at the end of the stream. Throws
    /// <see cref="InvalidDataException"/> for a line over <see cref="MaxRequest"/> bytes.
    /// </summary>
    public static async ValueTask<byte[]?> ReadLineAsync(Stream stream, ArrayBufferWriter<byte> line, byte[] buffer, Pending pending, CancellationToken cancellationToken)
    {
        line.ResetWrittenCount();
        while (true)
        {
            if (pending.Count == 0)
            {
                pending.Count = await stream.ReadAsync(buffer, cancellationToken);
                pending.Start = 0;
                if (pending.Count == 0)
                {
                    return line.WrittenCount > 0 ? Trim(line.WrittenSpan) : null;
                }
            }

            var available = buffer.AsSpan(pending.Start, pending.Count);
            var end = available.IndexOf((byte)'\n');
            var take = end < 0 ? available : available[..end];
            if (line.WrittenCount + take.Length > MaxRequest)
            {
                throw new InvalidDataException($"A request line is longer than {MaxRequest} bytes.");
            }

            line.Write(take);
            var consumed = end < 0 ? available.Length : end + 1;
            pending.Start += consumed;
            pending.Count -= consumed;
            if (end >= 0)
            {
                return Trim(line.WrittenSpan);
            }
        }
    }

    public static byte[] Result(JsonElement id, Action<Utf8JsonWriter> result) => Response(id, writer =>
    {
        writer.WritePropertyName("result");
        result(writer);
    });

    public static byte[] Error(JsonElement? id, int code, string message, string? reason, Action<Utf8JsonWriter>? details = null) =>
        Response(id, writer =>
        {
            writer.WriteStartObject("error");
            writer.WriteNumber("code", code);
            writer.WriteString("message", message);
            if (reason is not null || details is not null)
            {
                writer.WriteStartObject("data");
                if (reason is not null)
                {
                    writer.WriteString("reason", reason);
                }

                details?.Invoke(writer);
                writer.WriteEndObject();
            }

            writer.WriteEndObject();
        });

    /// <summary>The id as the request gave it (a number or a string); anything else is not an id.</summary>
    public static bool IsId(JsonElement id) => id.ValueKind is JsonValueKind.Number or JsonValueKind.String;

    private static byte[] Response(JsonElement? id, Action<Utf8JsonWriter> body)
    {
        var output = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(output))
        {
            writer.WriteStartObject();
            writer.WriteString("jsonrpc", "2.0");
            writer.WritePropertyName("id");
            if (id is { } value && IsId(value))
            {
                value.WriteTo(writer);
            }
            else
            {
                writer.WriteNullValue();
            }

            body(writer);
            writer.WriteEndObject();
        }

        output.Write("\n"u8);
        return output.WrittenSpan.ToArray();
    }

    private static byte[] Trim(ReadOnlySpan<byte> line) => (line.Length > 0 && line[^1] == '\r' ? line[..^1] : line).ToArray();

    /// <summary>The bytes read from the stream that no line has taken yet.</summary>
    internal sealed class Pending
    {
        public int Start { get; set; }

        public int Count { get; set; }
    }
}
