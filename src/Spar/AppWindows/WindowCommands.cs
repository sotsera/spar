using System.Text;
using System.Text.Json;
using Spar.Files;
using Spar.Runtime;

namespace Spar.AppWindows;

/// <summary>
/// The page's own window commands, under the host contract <c>spar</c> (decision "M3d's decide-first questions", item
/// 3):
/// <list type="bullet">
/// <item><c>openWindow</c>, <c>{"kind":"…","params":…}</c>: opens a window of a declared kind, or focuses the open one
/// of a single-instance kind; the declared error <c>notOpened</c> carries the reason (a host without windows of its
/// own, the app closing, the host failing). <c>params</c> is any JSON value, at most <see cref="MaxParamsBytes"/>.</item>
/// <item><c>windowParams</c>: the JSON the window's opener gave, or <c>null</c>.</item>
/// </list>
/// An unknown kind, or a request that is not this shape, is refused as <c>rejected</c> / <c>invalid-request</c>: the
/// generated <c>WindowKind</c> union keeps a misspelled kind from compiling.
/// </summary>
internal static class WindowCommands
{
    public const string Open = "openWindow";
    public const string Params = "windowParams";

    /// <summary>The most JSON a window's params may be.</summary>
    public const int MaxParamsBytes = 64 * 1024;

    private static readonly byte[] Null = "null"u8.ToArray();

    public static bool Handles(string command) => command is Open or Params;

    public static async ValueTask<SparOutcome> InvokeAsync(HostWindow? window, string command, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        if (command == Params)
        {
            return SparOutcome.FromValue(window is null || window.Params.IsEmpty ? Null : window.Params);
        }

        if (!TryRead(payload, out var kind, out var parameters, out var problem))
        {
            return SparOutcome.FromFailure(SparFailure.InvalidRequest(problem));
        }

        if (window?.Windows is not { } windows)
        {
            return NotOpened("This page's host opens no windows of the app: windows open in the app's own desktop windows, not in a browser.");
        }

        var result = await windows.OpenAsync(kind, parameters, window.Id, cancellationToken);
        return result.Outcome switch
        {
            OpenOutcome.Opened or OpenOutcome.Focused => SparOutcome.FromValue(ReadOnlyMemory<byte>.Empty),
            OpenOutcome.UnknownKind => SparOutcome.FromFailure(SparFailure.InvalidRequest($"The app declares no window kind '{kind}'.")),
            _ => NotOpened(result.Reason ?? "The window could not be opened."),
        };
    }

    private static SparOutcome NotOpened(string reason)
    {
        var buffer = new System.Buffers.ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("code"u8, "notOpened"u8);
            writer.WriteString("reason"u8, reason);
            writer.WriteEndObject();
        }

        return SparOutcome.FromError(buffer.WrittenMemory);
    }

    /// <summary>The request's kind and its params as the page sent them (their own bytes), or why it is not one.</summary>
    internal static bool TryRead(ReadOnlyMemory<byte> payload, out string kind, out ReadOnlyMemory<byte> parameters, out string problem)
    {
        kind = "";
        parameters = ReadOnlyMemory<byte>.Empty;
        problem = "openWindow takes {\"kind\":\"…\",\"params\":…}.";
        try
        {
            var reader = new Utf8JsonReader(payload.Span, new JsonReaderOptions { MaxDepth = 64 });
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            {
                return false;
            }

            string? found = null;
            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                var name = reader.GetString();
                reader.Read();
                if (name == "kind" && reader.TokenType == JsonTokenType.String && found is null)
                {
                    found = reader.GetString();
                }
                else if (name == "params" && parameters.IsEmpty)
                {
                    var start = (int)reader.TokenStartIndex;
                    reader.Skip();
                    var slice = payload[start..(int)reader.BytesConsumed];
                    parameters = slice.Span.SequenceEqual("null"u8) ? ReadOnlyMemory<byte>.Empty : slice;
                }
                else
                {
                    reader.Skip();
                }
            }

            if (reader.TokenType != JsonTokenType.EndObject || reader.Read() || found is null)
            {
                return false;
            }

            if (parameters.Length > MaxParamsBytes)
            {
                problem = $"A window's params are at most {MaxParamsBytes / 1024} KB of JSON; these are {parameters.Length} bytes. Keep larger data in the backend and pass its id.";
                return false;
            }

            kind = found;
            return true;
        }
        catch (JsonException error)
        {
            problem = $"openWindow's request is not JSON: {error.Message}";
            return false;
        }
    }

    /// <summary>The params as text, for logs and tests.</summary>
    internal static string Text(ReadOnlyMemory<byte> parameters) => parameters.IsEmpty ? "null" : Encoding.UTF8.GetString(parameters.Span);
}
