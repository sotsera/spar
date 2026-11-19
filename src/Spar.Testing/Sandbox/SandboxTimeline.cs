using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Spar.Testing;

/// <summary>
/// A sandbox session's timeline: one JSON line per step, with its time since the session began, how long it took, and
/// its details, in <c>timeline.jsonl</c>. The steps are also kept in memory for the runner's summary. The sandbox's id is
/// written as <c>&lt;id&gt;</c>, so the file can go into evidence as it is.
/// </summary>
internal sealed class SandboxTimeline : IDisposable
{
    private readonly Lock _gate = new();
    private readonly StreamWriter _writer;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly List<SandboxStep> _steps = [];
    private string? _secret;

    public SandboxTimeline(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        _writer = new StreamWriter(path, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)) { AutoFlush = true };
    }

    /// <summary>The time since the session began.</summary>
    public TimeSpan Elapsed => _clock.Elapsed;

    public IReadOnlyList<SandboxStep> Steps
    {
        get
        {
            lock (_gate)
            {
                return [.. _steps];
            }
        }
    }

    /// <summary>The sandbox's id, replaced by <c>&lt;id&gt;</c> in every later line.</summary>
    public void Hide(string id) => _secret = id;

    public void Write(string step, TimeSpan? took, params (string Name, object? Value)[] details)
    {
        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            json.WriteString("step", step);
            json.WriteNumber("at_ms", Math.Round(_clock.Elapsed.TotalMilliseconds, 1));
            if (took is { } duration)
            {
                json.WriteNumber("took_ms", Math.Round(duration.TotalMilliseconds, 1));
            }

            foreach (var (name, value) in details)
            {
                WriteValue(json, name, value);
            }

            json.WriteEndObject();
        }

        var line = Encoding.UTF8.GetString(buffer.ToArray());
        if (_secret is { } secret)
        {
            line = line.Replace(secret, "<id>", StringComparison.OrdinalIgnoreCase);
        }

        lock (_gate)
        {
            _steps.Add(new SandboxStep(step, _clock.Elapsed, took));
            _writer.WriteLine(line);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _writer.Dispose();
        }
    }

    private static void WriteValue(Utf8JsonWriter json, string name, object? value)
    {
        switch (value)
        {
            case null:
                json.WriteNull(name);
                break;
            case bool flag:
                json.WriteBoolean(name, flag);
                break;
            case int number:
                json.WriteNumber(name, number);
                break;
            case long number:
                json.WriteNumber(name, number);
                break;
            case double number:
                json.WriteNumber(name, Math.Round(number, 1));
                break;
            case TimeSpan time:
                json.WriteNumber(name, Math.Round(time.TotalMilliseconds, 1));
                break;
            default:
                json.WriteString(name, Convert.ToString(value, CultureInfo.InvariantCulture));
                break;
        }
    }
}

/// <summary>One step of a session: its name, when it ended, and how long it took when it was timed.</summary>
internal readonly record struct SandboxStep(string Name, TimeSpan At, TimeSpan? Took);
