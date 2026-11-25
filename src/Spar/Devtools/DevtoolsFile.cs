using System.Globalization;
using System.Text.Json;
using Spar.Hosting;

namespace Spar.Devtools;

/// <summary>
/// The discovery file, <c>obj/spar-devtools.json</c> in the app's project, owner-only like the loopback dev session's
/// record: how a tool finds the running app (its process, its run, the socket and the token) and which build it is.
/// The app writes it once its socket listens and deletes it at exit; a file whose process is gone is stale.
/// <code>
/// {"version":1,"pid":4242,"run":"5177cff9","started":"2026-11-25T21:30:41.586Z","host":"windows",
///  "transport":"pipe","address":"spar-devtools-…","token":"…","app":"com.example.Notebook","identity":"[spar-build-identity/1 …]"}
/// </code>
/// </summary>
internal sealed record DevtoolsFile(
    int ProcessId, string Run, DateTimeOffset Started, string Host, string Transport, string Address, string Token, string App, string Identity)
{
    public const int Version = 1;

    /// <summary>The largest file a reader accepts; a real one is under 1 KB.</summary>
    private const int MaxBytes = 8192;

    public bool Write(string file) => OwnerOnlyFile.WriteJson(file, writer =>
    {
        writer.WriteNumber("version", Version);
        writer.WriteNumber("pid", ProcessId);
        writer.WriteString("run", Run);
        writer.WriteString("started", Started.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture));
        writer.WriteString("host", Host);
        writer.WriteString("transport", Transport);
        writer.WriteString("address", Address);
        writer.WriteString("token", Token);
        writer.WriteString("app", App);
        writer.WriteString("identity", Identity);
    });

    /// <summary>
    /// Deletes <paramref name="file"/> when it is still this run's: a later run of the same project may have replaced
    /// it already, and its file is left alone.
    /// </summary>
    public void DeleteIfOurs(string file)
    {
        if (Read(file) is { } recorded && recorded.ProcessId == ProcessId && recorded.Run == Run)
        {
            OwnerOnlyFile.TryDelete(file);
        }
    }

    /// <summary>The file's record, or null when it is missing, too large, or not a record of this version.</summary>
    public static DevtoolsFile? Read(string file)
    {
        try
        {
            var info = new FileInfo(file);
            if (!info.Exists || info.Length > MaxBytes)
            {
                return null;
            }

            return Parse(File.ReadAllBytes(file));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static DevtoolsFile? Parse(ReadOnlySpan<byte> json)
    {
        try
        {
            var reader = new Utf8JsonReader(json);
            using var document = JsonDocument.ParseValue(ref reader);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("version", out var version)
                || !version.TryGetInt32(out var number) || number != Version)
            {
                return null;
            }

            var started = DateTimeOffset.TryParse(Text(root, "started"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var time)
                ? time
                : DateTimeOffset.MinValue;
            return root.TryGetProperty("pid", out var pid) && pid.TryGetInt32(out var processId)
                && Text(root, "address") is { Length: > 0 } address && Text(root, "token") is { Length: > 0 } token
                ? new DevtoolsFile(
                    processId, Text(root, "run") ?? "", started, Text(root, "host") ?? "", Text(root, "transport") ?? "",
                    address, token, Text(root, "app") ?? "", Text(root, "identity") ?? "")
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? Text(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
