using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Spar.Tool.Dev;

/// <summary>
/// What one reload came to (decision "M2b's decide-first questions", item 6): the build, and the app afterwards, which
/// is the new run when the build succeeded and the previous one, kept, when it failed.
/// </summary>
internal sealed record ReloadResult(BuildResult Build, string AppState, int? ProcessId, string? Run, double? ReadyMilliseconds, double? StartSeconds, int? ExitCode, IReadOnlyList<string> Output, string? PreviousRun, string? ReadyPhase = null)
{
    /// <summary>The app is running and its page reported <c>ui-ready</c>.</summary>
    public const string Ready = "ready";

    /// <summary>The build failed and the previous run is still running.</summary>
    public const string Kept = "kept";

    /// <summary>The build failed and no run is going.</summary>
    public const string None = "none";

    /// <summary>The new run ended before its page was ready.</summary>
    public const string Exited = "exited";

    /// <summary>The new run did not report <c>ui-ready</c> within the limit; it still runs.</summary>
    public const string Starting = "starting";

    /// <summary><c>spar doctor</c>'s codes: 0 ready without warnings, 1 ready with warnings, 2 a failed build or start.</summary>
    public int ExitCodeForTool => !Build.Succeeded || AppState is not Ready ? ExitCodes.Errors : Build.Warnings.Any() ? ExitCodes.Warnings : ExitCodes.Success;

    public void Write(Utf8JsonWriter writer)
    {
        writer.WriteStartObject();
        writer.WriteStartObject("build");
        writer.WriteBoolean("succeeded", Build.Succeeded);
        writer.WriteNumber("exitCode", Build.ExitCode);
        writer.WriteNumber("seconds", Math.Round(Build.Duration.TotalSeconds, 2));
        WriteDiagnostics(writer, "errors", Build.Errors);
        WriteDiagnostics(writer, "warnings", Build.Warnings);
        writer.WriteEndObject();

        writer.WriteStartObject("app");
        writer.WriteString("state", AppState);
        Optional(writer, "pid", ProcessId);
        if (Run is not null)
        {
            writer.WriteString("run", Run);
        }

        if (ReadyMilliseconds is { } ready)
        {
            writer.WriteNumber("readyMs", Math.Round(ready, 1));
            writer.WriteString("readyPhase", ReadyPhase ?? "ui-ready");
        }

        if (StartSeconds is { } start)
        {
            writer.WriteNumber("startSeconds", Math.Round(start, 2));
        }

        Optional(writer, "exitCode", ExitCode);
        if (PreviousRun is not null)
        {
            writer.WriteString("previous", PreviousRun);
        }

        writer.WriteStartArray("output");
        foreach (var line in Output)
        {
            writer.WriteStringValue(line);
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    /// <summary>The reload as lines for a person or an agent: the build, each diagnostic at its place, then the app.</summary>
    public static string Describe(JsonElement result)
    {
        var text = new StringBuilder();
        var build = result.GetProperty("build");
        var errors = build.GetProperty("errors");
        var warnings = build.GetProperty("warnings");
        var seconds = build.GetProperty("seconds").GetDouble().ToString("0.0", CultureInfo.InvariantCulture);
        var counts = $"{Count(errors.GetArrayLength(), "error")}, {Count(warnings.GetArrayLength(), "warning")}";
        text.Append(build.GetProperty("succeeded").GetBoolean() ? $"built in {seconds} s ({counts})" : $"build failed in {seconds} s ({counts})").Append('\n');
        foreach (var diagnostic in errors.EnumerateArray().Concat(warnings.EnumerateArray()))
        {
            text.Append("  ").Append(DescribeDiagnostic(diagnostic)).Append('\n');
        }

        var app = result.GetProperty("app");
        var state = app.GetProperty("state").GetString();
        var pid = app.TryGetProperty("pid", out var id) ? id.GetInt32().ToString(CultureInfo.InvariantCulture) : "?";
        var run = app.TryGetProperty("run", out var name) ? name.GetString() : null;
        var previous = app.TryGetProperty("previous", out var before) ? before.GetString() : null;
        text.Append(state switch
        {
            Ready => $"running: pid {pid}, run {run}, {Phase(app)} at {Number(app, "readyMs", "0.#")} ms ({Number(app, "startSeconds", "0.0")} s from its start)"
                + (previous is null ? "" : $"; the previous run {previous}"),
            Kept => $"the previous run is still running (pid {pid}{(run is null ? "" : ", run " + run)}): fix the errors and reload",
            None => "no run: fix the errors and reload",
            Exited => $"the new run exited with {(app.TryGetProperty("exitCode", out var code) ? code.GetInt32() : -1)} before its page was ready; its last lines:",
            Starting => $"the new run (pid {pid}) did not report ui-ready within 60 s; it still runs; its last lines:",
            _ => state,
        }).Append('\n');
        if (state is Exited or Starting)
        {
            foreach (var line in app.GetProperty("output").EnumerateArray())
            {
                text.Append("  | ").Append(line.GetString()).Append('\n');
            }
        }

        return text.ToString().TrimEnd('\n');
    }

    private static string DescribeDiagnostic(JsonElement diagnostic)
    {
        var place = diagnostic.TryGetProperty("file", out var file) ? file.GetString() : null;
        if (place is not null && diagnostic.TryGetProperty("line", out var line))
        {
            place += diagnostic.TryGetProperty("column", out var column) ? $"({line.GetInt32()},{column.GetInt32()})" : $"({line.GetInt32()})";
        }

        var code = diagnostic.GetProperty("code").GetString();
        var head = $"{diagnostic.GetProperty("severity").GetString()}{(string.IsNullOrEmpty(code) ? "" : " " + code)}";
        return (place is null ? "" : place + ": ") + head + ": " + diagnostic.GetProperty("message").GetString();
    }

    private static void WriteDiagnostics(Utf8JsonWriter writer, string name, IEnumerable<BuildDiagnostic> diagnostics)
    {
        writer.WriteStartArray(name);
        foreach (var diagnostic in diagnostics)
        {
            writer.WriteStartObject();
            writer.WriteString("severity", diagnostic.Severity);
            writer.WriteString("code", diagnostic.Code);
            writer.WriteString("message", diagnostic.Message);
            if (diagnostic.File is not null)
            {
                writer.WriteString("file", diagnostic.File);
            }

            Optional(writer, "line", diagnostic.Line);
            Optional(writer, "column", diagnostic.Column);
            if (diagnostic.Project is not null)
            {
                writer.WriteString("project", diagnostic.Project);
            }

            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void Optional(Utf8JsonWriter writer, string name, int? value)
    {
        if (value is { } number)
        {
            writer.WriteNumber(name, number);
        }
    }

    private static string Number(JsonElement element, string name, string format) =>
        element.TryGetProperty(name, out var value) ? value.GetDouble().ToString(format, CultureInfo.InvariantCulture) : "?";

    /// <summary>The phase the run was ready at: <c>ui-ready</c>, or loopback's listening, whose page opens in a browser.</summary>
    private static string Phase(JsonElement app) =>
        app.TryGetProperty("readyPhase", out var phase) && phase.GetString() == "loopback listening" ? "listening for its page in a browser" : "ui-ready";

    private static string Count(int count, string noun) => count == 1 ? $"1 {noun}" : $"{count} {noun}s";
}
