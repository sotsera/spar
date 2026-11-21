using System.Text.Json;

namespace Spar.DevServer;

/// <summary>
/// <c>obj/spar-dev-server.json</c>, which the Spar Vite plugin writes once the server Spar started listens, and removes
/// when it closes (plan section 4): the real address, not one parsed from npm's or Vite's output, the instance Spar gave
/// the server, and the Vite process with its start time, so a later run of the app can tell its own server from another
/// that took the same port or process id. The plugin writes it through a temporary file and a rename, so a reader sees
/// the whole file or none.
/// </summary>
/// <param name="Url">The server's origin, <c>http://127.0.0.1:&lt;port&gt;</c>.</param>
/// <param name="Instance">The id Spar gave the server when it started it (<c>SPAR_DEV_SERVER_INSTANCE</c>).</param>
/// <param name="ProcessId">The Vite process's id.</param>
/// <param name="StartedUnixMilliseconds">When the Vite process started, by its own clock, to about a millisecond.</param>
internal sealed record DevServerState(string Url, string Instance, int ProcessId, long StartedUnixMilliseconds)
{
    /// <summary>The format this host reads; the plugin writes <c>"version": 1</c>.</summary>
    public const int Version = 1;

    /// <summary>The file's state, or null when it is missing, being replaced, or not in this format.</summary>
    public static DevServerState? Read(string path)
    {
        byte[] bytes;
        try
        {
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (file.Length > 4096)
            {
                return null;
            }

            bytes = new byte[file.Length];
            file.ReadExactly(bytes);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        return Parse(bytes);
    }

    /// <summary>The state in <paramref name="json"/>, or null when it is not one this host reads.</summary>
    internal static DevServerState? Parse(ReadOnlySpan<byte> json)
    {
        int? version = null;
        string? url = null;
        string? instance = null;
        int? processId = null;
        long? started = null;
        try
        {
            var reader = new Utf8JsonReader(json);
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            {
                return null;
            }

            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                var name = reader.GetString();
                reader.Read();
                switch (name)
                {
                    case "version" when reader.TokenType == JsonTokenType.Number:
                        version = reader.GetInt32();
                        break;
                    case "url" when reader.TokenType == JsonTokenType.String:
                        url = reader.GetString();
                        break;
                    case "instance" when reader.TokenType == JsonTokenType.String:
                        instance = reader.GetString();
                        break;
                    case "pid" when reader.TokenType == JsonTokenType.Number:
                        processId = reader.GetInt32();
                        break;
                    case "started" when reader.TokenType == JsonTokenType.Number:
                        started = reader.GetInt64();
                        break;
                    default:
                        reader.Skip();
                        break;
                }
            }
        }
        catch (Exception exception) when (exception is JsonException or FormatException or InvalidOperationException)
        {
            return null;
        }

        if (version != Version || url is null || instance is not { Length: > 0 } || processId is not > 0 || started is null)
        {
            return null;
        }

        return new DevServerState(url, instance, processId.Value, started.Value);
    }
}
