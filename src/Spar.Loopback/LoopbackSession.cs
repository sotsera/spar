using System.Buffers;
using System.Buffers.Text;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

namespace Spar.Loopback;

/// <summary>Where one loopback run serves the page, and what the run does with it.</summary>
/// <param name="Port">The port of 127.0.0.1; 0 takes a free one, which the run then records.</param>
/// <param name="Token">The bearer token, base64url, which the page gets in the address's fragment.</param>
/// <param name="NewSession">The first run of a dev session: it opens the browser at the page.</param>
/// <param name="Orchestrated">An orchestrator set the port or the token: no session file, no browser.</param>
internal sealed record LoopbackAddress(int Port, string Token, bool NewSession, bool Orchestrated);

/// <summary>
/// The loopback dev session (plan section 4; decision "M1d step 2's shape: loopback in the development loop"): one address
/// and token across the backend's restarts, so an open page reconnects instead of the browser opening a new tab.
/// <list type="bullet">
/// <item><b>The record</b> is <c>obj/spar-loopback.json</c>, the project's own <c>obj/</c> folder, shared by every
/// Development run of the project: the port and the token. Its last-write time is when a run
/// last held it: each run writes it at start, touches it every minute while it runs, and once more as it ends.</item>
/// <item><b>A dev session</b> is the time from a run that found no usable record (missing, unreadable, or last held more
/// than <see cref="SessionGap"/> ago) to the last run that finds it held within that gap. Restarts from
/// <c>dotnet watch</c> and repeated <c>dotnet run</c>s keep it, since their gaps are seconds; the first run after a
/// night away starts a new one. Only the first run of a session opens the browser.</item>
/// <item><b>A new session</b> keeps the recorded port, if there was one, and takes a new token: a page left open from an
/// earlier session is then refused instead of joining this one, and says so.</item>
/// <item><b>An orchestrator</b> sets <see cref="PortVariable"/> or <see cref="TokenVariable"/> (or both): each value
/// given is used, a missing token is new for every run and a missing port is any free one, the record is neither read
/// nor written, and no browser opens, since the orchestrator shows the address itself.</item>
/// <item><b>The file holds a secret:</b> whoever reads the token can call every command of the app's Development build
/// as the developer. It is created readable and writable by its owner only (mode <c>0600</c> on Linux; on Windows an
/// access list with the current user alone, not inherited from the folder), and written through a temporary file of the
/// same protection and a rename, so it is never readable by others, even for a moment. The protection holds against
/// other users of the machine, not against the developer's own programs, which can read the project's sources and
/// build output too (plan section 3c, "What the controls defend").</item>
/// </list>
/// </summary>
internal static class LoopbackSession
{
    /// <summary>The record's name, in the project's <c>obj/</c> folder.</summary>
    public const string FileName = "spar-loopback.json";

    /// <summary>An orchestrator's port of 127.0.0.1 for the page.</summary>
    public const string PortVariable = "SPAR_LOOPBACK_PORT";

    /// <summary>An orchestrator's token: base64url, at least 16 and at most 48 bytes once decoded.</summary>
    public const string TokenVariable = "SPAR_LOOPBACK_TOKEN";

    /// <summary>The format this host writes and reads.</summary>
    private const int Version = 1;

    /// <summary>The longest time between two runs of one dev session.</summary>
    public static readonly TimeSpan SessionGap = TimeSpan.FromHours(4);

    /// <summary>
    /// This run's address: from the environment's variables when an orchestrator set one, otherwise from the record at
    /// <paramref name="file"/> (null when the build gave none: then every run is a new session on a free port).
    /// </summary>
    /// <param name="problem">Why a variable cannot be used; null otherwise.</param>
    public static LoopbackAddress? Resolve(string? file, Func<string, string?> environment, DateTimeOffset now, out string? problem)
    {
        problem = null;
        var portText = environment(PortVariable);
        var tokenText = environment(TokenVariable);
        if (portText is { Length: > 0 } || tokenText is { Length: > 0 })
        {
            return Orchestrated(portText, tokenText, out problem);
        }

        if (file is null || Read(file) is not { } recorded)
        {
            return new LoopbackAddress(0, NewToken(), NewSession: true, Orchestrated: false);
        }

        if (now - recorded.Held > SessionGap)
        {
            return new LoopbackAddress(recorded.Port, NewToken(), NewSession: true, Orchestrated: false);
        }

        return new LoopbackAddress(recorded.Port, recorded.Token, NewSession: false, Orchestrated: false);
    }

    /// <summary>Records the run's address at <paramref name="file"/>, owner-only; false when it cannot (the run goes on).</summary>
    public static bool Write(string file, int port, string token) =>
        Spar.Hosting.OwnerOnlyFile.WriteJson(file, writer =>
        {
            writer.WriteNumber("version", Version);
            writer.WriteNumber("port", port);
            writer.WriteString("token", token);
        });

    /// <summary>Marks the record as held now: a run touches it every minute and as it ends.</summary>
    public static void Touch(string file, DateTimeOffset now)
    {
        try
        {
            File.SetLastWriteTimeUtc(file, now.UtcDateTime);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Another run replaced or removed it; the session's gap is measured from that run's writes instead.
        }
    }

    /// <summary>A new token: 24 random bytes, base64url.</summary>
    public static string NewToken() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(24));

    /// <summary>The token's bytes, or null when <paramref name="token"/> is not base64url of 16 to 48 bytes.</summary>
    public static byte[]? TokenBytes(string token)
    {
        if (token.Length is 0 or > 64)
        {
            return null;
        }

        Span<byte> decoded = stackalloc byte[48];
        var status = Base64Url.DecodeFromChars(token, decoded, out var consumed, out var written);
        if (status != OperationStatus.Done || consumed != token.Length || written < 16)
        {
            return null;
        }

        return decoded[..written].ToArray();
    }

    /// <summary>The record's port, token and last-held time, or null when it is missing or not in this format.</summary>
    internal static (int Port, string Token, DateTimeOffset Held)? Read(string file)
    {
        byte[] bytes;
        DateTimeOffset held;
        try
        {
            var info = new FileInfo(file);
            if (!info.Exists || info.Length > 4096)
            {
                return null;
            }

            held = info.LastWriteTimeUtc;
            bytes = File.ReadAllBytes(file);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        return Parse(bytes) is { } parsed ? (parsed.Port, parsed.Token, held) : null;
    }

    /// <summary>The port and token in <paramref name="json"/>, or null when it is not a record this host reads.</summary>
    internal static (int Port, string Token)? Parse(ReadOnlySpan<byte> json)
    {
        int? version = null;
        int? port = null;
        string? token = null;
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
                    case "port" when reader.TokenType == JsonTokenType.Number:
                        port = reader.GetInt32();
                        break;
                    case "token" when reader.TokenType == JsonTokenType.String:
                        token = reader.GetString();
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

        if (version != Version || port is not (> 0 and <= 65535) || token is null || TokenBytes(token) is null)
        {
            return null;
        }

        return (port.Value, token);
    }

    private static LoopbackAddress? Orchestrated(string? portText, string? tokenText, out string? problem)
    {
        problem = null;
        var port = 0;
        if (portText is { Length: > 0 }
            && (!int.TryParse(portText, NumberStyles.None, CultureInfo.InvariantCulture, out port) || port is < 1 or > 65535))
        {
            problem = $"{PortVariable} is '{portText}', not a port from 1 to 65535.";
            return null;
        }

        if (tokenText is { Length: > 0 } && TokenBytes(tokenText) is null)
        {
            problem = $"{TokenVariable} is not base64url of 16 to 48 bytes (Spar's own tokens are 24 random bytes).";
            return null;
        }

        var token = tokenText is { Length: > 0 } ? tokenText : NewToken();
        return new LoopbackAddress(port, token, NewSession: false, Orchestrated: true);
    }
}
