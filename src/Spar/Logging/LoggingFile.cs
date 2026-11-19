using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Spar.Logging;

/// <summary>What a valid <c>logging.json</c> sets: the default level, the category levels, or both.</summary>
internal sealed class LoggingFileSettings(LogLevel? level, IReadOnlyList<LogLevelRule> categories)
{
    public LogLevel? Level { get; } = level;

    public IReadOnlyList<LogLevelRule> Categories { get; } = categories;
}

/// <summary>
/// The per-user override of the log file's levels (decision "Logging's public shape"): <c>logging.json</c> in the app's
/// data root, read once at start, so a user can raise the level for support without a new build or administrator
/// rights. Its whole shape is <c>{"level":"Debug","categories":{"SqlTool.Copy":"Trace"}}</c>, both properties
/// optional, the levels named as <see cref="LogLevel"/> names them (any case). It is read strictly with
/// <see cref="Utf8JsonReader"/>: anything else (an unknown property, a wrong type, a comment, a second copy of a
/// property) refuses the whole file. A refused file never stops the start: the host logs one warning and keeps the
/// levels from <c>Program.cs</c>.
/// </summary>
internal static class LoggingFile
{
    /// <summary>The file's name in the data root.</summary>
    public const string FileName = "logging.json";

    /// <summary>The largest file read: a few categories fit many times over.</summary>
    public const int MaxBytes = 64 * 1024;

    /// <summary>The most categories one file may set.</summary>
    public const int MaxCategories = 100;

    /// <summary>The shape a warning shows.</summary>
    public const string Shape = """{"level":"Debug","categories":{"SqlTool.Copy":"Trace"}}""";

    private static readonly JsonReaderOptions Options = new() { MaxDepth = 4 };

    /// <summary>
    /// Reads <c>logging.json</c> in <paramref name="dataRoot"/>: null when there is none, or when it is refused, with
    /// the warning's text in <paramref name="problem"/>.
    /// </summary>
    public static LoggingFileSettings? Read(string dataRoot, out string? problem)
    {
        problem = null;
        var path = Path.Combine(dataRoot, FileName);
        byte[] bytes;
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists)
            {
                return null;
            }

            if (info.Length > MaxBytes)
            {
                problem = Ignored(path, $"it is larger than {MaxBytes / 1024} KiB");
                return null;
            }

            bytes = File.ReadAllBytes(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            problem = Ignored(path, $"it cannot be read ({exception.Message})");
            return null;
        }

        var settings = Parse(bytes, out var reason);
        if (settings is null)
        {
            problem = Ignored(path, reason!);
        }

        return settings;
    }

    /// <summary>Parses the file's bytes; null, with the reason, when they are not exactly the shape above.</summary>
    public static LoggingFileSettings? Parse(ReadOnlySpan<byte> json, out string? reason)
    {
        reason = null;
        if (json.StartsWith("﻿"u8))
        {
            // Some editors write a byte-order mark; it is not part of the JSON.
            json = json[3..];
        }

        try
        {
            var reader = new Utf8JsonReader(json, Options);
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            {
                reason = "it is not a JSON object";
                return null;
            }

            LogLevel? level = null;
            List<LogLevelRule>? categories = null;
            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                if (reader.ValueTextEquals("level"u8) && level is null)
                {
                    reader.Read();
                    if (!TryReadLevel(ref reader, out var parsed, out reason))
                    {
                        return null;
                    }

                    level = parsed;
                }
                else if (reader.ValueTextEquals("categories"u8) && categories is null)
                {
                    categories = ReadCategories(ref reader, out reason);
                    if (categories is null)
                    {
                        return null;
                    }
                }
                else
                {
                    reason = $"the property \"{reader.GetString()}\" is unknown or appears twice";
                    return null;
                }
            }

            if (reader.TokenType != JsonTokenType.EndObject || reader.Read())
            {
                reason = "it is not one JSON object";
                return null;
            }

            return new LoggingFileSettings(level, (IReadOnlyList<LogLevelRule>?)categories ?? []);
        }
        catch (JsonException exception)
        {
            reason = $"it is not valid JSON ({exception.Message})";
            return null;
        }
    }

    private static List<LogLevelRule>? ReadCategories(ref Utf8JsonReader reader, out string? reason)
    {
        reason = null;
        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
        {
            reason = "\"categories\" is not an object of category names and levels";
            return null;
        }

        var categories = new List<LogLevelRule>();
        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            var category = reader.GetString()!;
            if (LogLevels.CategoryProblem(category) is { } problem)
            {
                reason = problem;
                return null;
            }

            if (Contains(categories, category))
            {
                reason = $"the category \"{category}\" appears twice";
                return null;
            }

            if (categories.Count == MaxCategories)
            {
                reason = $"it sets more than {MaxCategories} categories";
                return null;
            }

            reader.Read();
            if (!TryReadLevel(ref reader, out var level, out reason))
            {
                return null;
            }

            categories.Add(new LogLevelRule(category, level));
        }

        return categories;
    }

    private static bool Contains(List<LogLevelRule> categories, string category)
    {
        foreach (var rule in categories)
        {
            if (string.Equals(rule.Category, category, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryReadLevel(ref Utf8JsonReader reader, out LogLevel level, out string? reason)
    {
        reason = null;
        level = LogLevel.None;
        if (reader.TokenType == JsonTokenType.String && LogLevels.TryParse(reader.GetString()!, out level))
        {
            return true;
        }

        reason = "a level is one of \"Trace\", \"Debug\", \"Information\", \"Warning\", \"Error\", \"Critical\" or \"None\"";
        return false;
    }

    private static string Ignored(string path, string reason) =>
        $"The log settings file {path} was ignored: {reason}. Its shape is {Shape}; the levels from Program.cs apply.";
}
