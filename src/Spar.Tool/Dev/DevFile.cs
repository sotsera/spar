using System.Globalization;
using System.Text.Json;
using Spar.Hosting;

namespace Spar.Tool.Dev;

/// <summary>
/// <c>obj/spar-dev.json</c>, owner-only like the devtools' discovery file (decision "M2b's decide-first questions", item
/// 1): how <c>spar reload</c> and <c>spar dev --stop</c> find the project's running <c>spar dev</c>, its socket and its
/// token. Written once its socket listens, deleted at its end; one whose process is gone is stale.
/// </summary>
internal sealed record DevFile(int ProcessId, DateTimeOffset Started, string Transport, string Address, string Token, string Profile, bool Private)
{
    public const int Version = 1;

    public static string PathFor(string projectDirectory) => Path.Combine(projectDirectory, "obj", "spar-dev.json");

    public bool Write(string file) => OwnerOnlyFile.WriteJson(file, writer =>
    {
        writer.WriteNumber("version", Version);
        writer.WriteNumber("pid", ProcessId);
        writer.WriteString("started", Started.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture));
        writer.WriteString("transport", Transport);
        writer.WriteString("address", Address);
        writer.WriteString("token", Token);
        writer.WriteString("profile", Profile);
        writer.WriteBoolean("private", Private);
    });

    public void DeleteIfOurs(string file)
    {
        if (Read(file) is { } recorded && recorded.ProcessId == ProcessId && recorded.Token == Token)
        {
            OwnerOnlyFile.TryDelete(file);
        }
    }

    public static DevFile? Read(string file)
    {
        try
        {
            var info = new FileInfo(file);
            return info.Exists && info.Length <= 8192 ? Parse(File.ReadAllBytes(file)) : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static DevFile? Parse(ReadOnlySpan<byte> json)
    {
        try
        {
            var reader = new Utf8JsonReader(json);
            using var document = JsonDocument.ParseValue(ref reader);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("version", out var version) || !version.TryGetInt32(out var number)
                || number != Version || !root.TryGetProperty("pid", out var pid) || !pid.TryGetInt32(out var processId))
            {
                return null;
            }

            string Text(string name) => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
            var started = DateTimeOffset.TryParse(Text("started"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var time) ? time : DateTimeOffset.MinValue;
            var isPrivate = root.TryGetProperty("private", out var flag) && flag.ValueKind == JsonValueKind.True;
            return Text("address") is { Length: > 0 } address && Text("token") is { Length: > 0 } token
                ? new DevFile(processId, started, Text("transport"), address, token, Text("profile"), isPrivate)
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
