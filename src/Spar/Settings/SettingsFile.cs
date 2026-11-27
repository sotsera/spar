using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace Spar.Settings;

/// <summary>
/// A settings file's format and writes: <c>{"version":N,"settings":{…}}</c>, migrated step by step to the current version,
/// laid over the defaults (a property missing from the file keeps the default's value, an unknown one is dropped), and
/// written to a staged file in the same folder, then moved over the old one.
/// </summary>
internal static class SettingsFile
{
    private static readonly JsonDocumentOptions DocumentOptions = new() { MaxDepth = Runtime.SparJson.MaxDepth };

    private static readonly JsonWriterOptions Indented = new() { Indented = true, MaxDepth = Runtime.SparJson.MaxDepth };

    private static readonly Action<ILogger, string, string, string, Exception?> SetAsideMessage = LoggerMessage.Define<string, string, string>(
        LogLevel.Error, new EventId(1, "SettingsSetAside"), "Settings file {Path} could not be read ({Reason}); it was kept as {Aside} and the app starts on the defaults");

    private static readonly Action<ILogger, string, string, Exception?> DroppedMessage = LoggerMessage.Define<string, string>(
        LogLevel.Warning, new EventId(2, "SettingsDropped"), "Settings file {Path} has properties the settings no longer have, dropped: {Names}");

    private static readonly Action<ILogger, string, Exception?> UnreachableMessage = LoggerMessage.Define<string>(
        LogLevel.Warning, new EventId(3, "SettingsUnreachable"), "Settings file {Path} cannot be reached; the app uses the last local copy, read-only, until it can");

    private static readonly Action<ILogger, string, int, Exception?> NewerMessage = LoggerMessage.Define<string, int>(
        LogLevel.Warning, new EventId(4, "SettingsNewer"), "Settings file {Path} was written by a newer version of the app (schema {Version}); it is left as it is and the app starts on the defaults, read-only");

    private static readonly Action<ILogger, string, Exception?> UnreadableMessage = LoggerMessage.Define<string>(
        LogLevel.Error, new EventId(5, "SettingsUnreadable"), "Settings file {Path} could not be read; the app starts on the defaults and leaves the file as it is");

    internal enum Kind
    {
        Read,
        Broken,
        Newer,
    }

    /// <summary>What a file's bytes hold: the merged settings JSON, or why it cannot be used.</summary>
    internal readonly record struct Parsed(Kind Kind, byte[]? Settings, string Reason, int Version);

    /// <summary>Reads a file's bytes: its version, its migrations, then its settings over <paramref name="defaults"/>.</summary>
    public static Parsed Parse(byte[] bytes, SettingsOptions options, byte[] defaults, List<string> dropped)
    {
        try
        {
            if (JsonNode.Parse(bytes, documentOptions: DocumentOptions) is not JsonObject root
                || root["version"] is not JsonValue versionValue || !versionValue.TryGetValue(out int version) || version < 1
                || root["settings"] is not JsonObject settings)
            {
                return new Parsed(Kind.Broken, null, "it is not {\"version\":N,\"settings\":{…}}", 0);
            }

            if (version > options.CurrentVersion)
            {
                return new Parsed(Kind.Newer, null, "", version);
            }

            settings = (JsonObject)settings.DeepClone();
            for (; version < options.CurrentVersion; version++)
            {
                if (!options.Migrations.TryGetValue(version, out var step))
                {
                    return new Parsed(Kind.Broken, null, $"no migration from version {version}", version);
                }

                settings = step(settings) ?? throw new InvalidOperationException($"The migration from version {version} returned null.");
            }

            var merged = (JsonObject)JsonNode.Parse(defaults, documentOptions: DocumentOptions)!;
            Merge(merged, settings, "", dropped);
            return new Parsed(Kind.Read, Bytes(merged), "", version);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException or ArgumentException)
        {
            return new Parsed(Kind.Broken, null, exception.Message, 0);
        }
    }

    /// <summary>The file's bytes for <paramref name="settings"/>, indented, so a person can read and edit it.</summary>
    public static byte[] Wrap(int version, ReadOnlyMemory<byte> settings)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, Indented))
        {
            writer.WriteStartObject();
            writer.WriteNumber("version"u8, version);
            writer.WritePropertyName("settings"u8);
            JsonNode.Parse(settings.Span, documentOptions: DocumentOptions)!.WriteTo(writer);
            writer.WriteEndObject();
        }

        stream.WriteByte((byte)'\n');
        return stream.ToArray();
    }

    /// <summary>Writes a staged file beside <paramref name="path"/>, flushed to disk, then moves it over the old one.</summary>
    public static void Write(string path, byte[] bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var staged = $"{path}.tmp-{Guid.NewGuid():N}";
        try
        {
            using (var file = new FileStream(staged, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                file.Write(bytes);
                file.Flush(flushToDisk: true);
            }

            File.Move(staged, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(staged))
            {
                File.Delete(staged);
            }
        }
    }

    /// <summary>Moves an unreadable file aside, as <c>&lt;file&gt;.bad-&lt;UTC time&gt;</c>, and logs it.</summary>
    public static void SetAside(string path, string reason, TimeProvider time, ILogger logger)
    {
        var aside = $"{path}.bad-{time.GetUtcNow().ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture)}";
        try
        {
            File.Move(path, aside, overwrite: true);
        }
        catch (IOException exception)
        {
            UnreadableMessage(logger, path, exception);
            return;
        }

        SetAsideMessage(logger, path, reason, aside, null);
    }

    public static void LogDropped(ILogger logger, string path, List<string> dropped)
    {
        if (dropped.Count > 0)
        {
            DroppedMessage(logger, path, string.Join(", ", dropped), null);
        }
    }

    public static void LogUnreachable(ILogger logger, string path, Exception exception) => UnreachableMessage(logger, path, exception);

    public static void LogNewer(ILogger logger, string path, int version) => NewerMessage(logger, path, version, null);

    public static void LogUnreadable(ILogger logger, string path, Exception exception) => UnreadableMessage(logger, path, exception);

    /// <summary>Lays <paramref name="source"/> over <paramref name="target"/>: known properties replaced, objects merged.</summary>
    private static void Merge(JsonObject target, JsonObject source, string prefix, List<string> dropped)
    {
        foreach (var (name, value) in source.ToList())
        {
            if (!target.ContainsKey(name))
            {
                dropped.Add(prefix + name);
                continue;
            }

            if (target[name] is JsonObject inner && value is JsonObject overlay)
            {
                Merge(inner, overlay, $"{prefix}{name}.", dropped);
                continue;
            }

            target[name] = value?.DeepClone();
        }
    }

    private static byte[] Bytes(JsonNode node)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            node.WriteTo(writer);
        }

        return stream.ToArray();
    }
}
