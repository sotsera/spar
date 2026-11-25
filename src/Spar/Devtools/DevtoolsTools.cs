using System.Globalization;
using System.Text;
using System.Text.Json;
using Spar.Runtime;

namespace Spar.Devtools;

/// <summary>
/// The tools (plan section 6): <c>version</c>, <c>contracts</c>, <c>call</c>, <c>calls</c> and <c>logs</c> here, the
/// page's <c>tree</c>, <c>click</c>, <c>fill</c>, <c>wait</c> and <c>screenshot</c> in <c>PageTools.cs</c>. Each answers
/// a bounded result; a tool that runs out of time answers <c>timeout</c>, which means unfinished, never failed.
/// </summary>
internal sealed partial class DevtoolsTools
{
    public static readonly string[] Names = ["version", "contracts", "call", "calls", "logs", "tree", "click", "fill", "wait", "screenshot"];

    /// <summary>How long <c>call</c> waits for its command by default; the command keeps running after it.</summary>
    private static readonly TimeSpan CallWait = TimeSpan.FromSeconds(30);

    /// <summary>The largest command result <c>call</c> returns in full.</summary>
    private const int MaxValue = 256 * 1024;

    private readonly DevtoolsContext _context;
    private readonly DevtoolsFile _file;
    private long _callId;

    public DevtoolsTools(DevtoolsContext context, DevtoolsFile file)
    {
        _context = context;
        _file = file;
        Calls = new CallRecorder(context.Running.Dispatcher.Time);
    }

    public CallRecorder Calls { get; }

    public async Task<byte[]> RunAsync(JsonElement id, string name, JsonElement parameters, CancellationToken cancellationToken)
    {
        try
        {
            Action<Utf8JsonWriter> result = name switch
            {
                "version" => Version(),
                "contracts" => Contracts(),
                "call" => await CallAsync(parameters, cancellationToken),
                "calls" => RecentCalls(parameters),
                "logs" => Logs(parameters),
                "tree" => await TreeAsync(parameters),
                "click" => await ClickAsync(parameters, cancellationToken),
                "fill" => await FillAsync(parameters, cancellationToken),
                "wait" => await WaitAsync(parameters, cancellationToken),
                "screenshot" => await ScreenshotAsync(),
                _ => throw new DevtoolsError("not_found", $"There is no tool '{name}'. The tools: {string.Join(", ", Names)}.", JsonRpc.MethodNotFound),
            };
            return JsonRpc.Result(id, result);
        }
        catch (DevtoolsError error)
        {
            return JsonRpc.Error(id, error.Code, error.Message, error.Reason, error.Details);
        }
    }

    private Action<Utf8JsonWriter> Version() => writer =>
    {
        var identity = BuildIdentity.Parse(_context.Running.Catalog.BuildIdentity.Span);
        writer.WriteStartObject();
        writer.WriteString("app", _file.App);
        writer.WriteString("run", _context.Run);
        writer.WriteNumber("pid", Environment.ProcessId);
        writer.WriteString("host", _context.Host);
        writer.WriteString("started", _file.Started);
        writer.WriteString("spar", SparVersion());
        writer.WriteString("runtime", System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription);
        writer.WriteString("os", System.Runtime.InteropServices.RuntimeInformation.OSDescription);
        writer.WriteString("build", BuildIdentity.Describe(_context.Running.Catalog.BuildIdentity.Span));
        if (identity is not null)
        {
            writer.WriteString("variant", identity.Variant);
            writer.WriteString("revision", identity.Revision);
            if (identity.Dirty is { } dirty)
            {
                writer.WriteBoolean("dirty", dirty);
            }

            writer.WriteString("fingerprint", identity.Fingerprint);
        }

        writer.WriteBoolean("page", _context.Pages.Current is not null);
        if (_context.Pages.Current is { } page)
        {
            writer.WriteBoolean("trustedInput", page.TrustedInput);
        }

        writer.WriteEndObject();
    };

    /// <summary>The catalog's commands and the contract snapshot the build embedded (records, enums and errors included).</summary>
    private Action<Utf8JsonWriter> Contracts()
    {
        var catalog = _context.Running.Catalog;
        var snapshot = ContractSnapshot(catalog.AppAssembly);
        return writer =>
        {
            writer.WriteStartObject();
            writer.WriteString("fingerprint", catalog.Fingerprint);
            writer.WriteStartArray("commands");
            foreach (var entry in catalog.Commands.OrderBy(entry => entry.Contract, StringComparer.Ordinal).ThenBy(entry => entry.Command, StringComparer.Ordinal))
            {
                writer.WriteStartObject();
                writer.WriteString("name", $"{entry.Contract}.{entry.Command}");
                if (entry.TimeoutSeconds > 0)
                {
                    writer.WriteNumber("timeoutSeconds", entry.TimeoutSeconds);
                }

                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            if (snapshot is not null)
            {
                writer.WriteString("snapshot", snapshot);
            }

            writer.WriteEndObject();
        };
    }

    private async Task<Action<Utf8JsonWriter>> CallAsync(JsonElement parameters, CancellationToken cancellationToken)
    {
        var name = Text(parameters, "command") ?? throw Invalid("call needs \"command\": \"contract.command\".");
        var dot = name.IndexOf('.', StringComparison.Ordinal);
        if (dot <= 0 || dot == name.Length - 1)
        {
            throw Invalid($"\"{name}\" is not contract.command; contracts lists them.");
        }

        var (contract, command) = (name[..dot], name[(dot + 1)..]);
        if (!_context.Running.Dispatcher.Knows(contract, command))
        {
            throw new DevtoolsError("not_found", $"The app has no command {name}; contracts lists them.");
        }

        var request = parameters.TryGetProperty("request", out var given) && given.ValueKind != JsonValueKind.Null
            ? Encoding.UTF8.GetBytes(given.GetRawText())
            : [];
        var wait = Milliseconds(parameters, "timeout") ?? CallWait;
        var callId = Interlocked.Increment(ref _callId);

        // The command runs on its own: the wait ends at the limit, the command does not (a timeout proves nothing stopped).
        var running = _context.Running.Dispatcher.InvokeAsync(contract, command, callId, request, CancellationToken.None, SparCaller.Devtools).AsTask();
        if (await Task.WhenAny(running, Task.Delay(wait, cancellationToken)) != running)
        {
            throw new DevtoolsError("timeout", $"{name} did not finish within {wait.TotalMilliseconds:0} ms; it may still be running, and calls shows it when it ends.");
        }

        var outcome = await running;
        return writer => WriteOutcome(writer, name, callId, outcome);
    }

    private Action<Utf8JsonWriter> RecentCalls(JsonElement parameters)
    {
        var limit = Math.Clamp(Number(parameters, "limit") ?? 50, 1, CallRecorder.Capacity);
        var since = Number(parameters, "since") ?? 0;
        var calls = Calls.Since(since, limit);
        return writer =>
        {
            writer.WriteStartObject();
            writer.WriteNumber("next", Calls.Next);
            writer.WriteStartArray("calls");
            foreach (var call in calls)
            {
                call.Write(writer);
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        };
    }

    private Action<Utf8JsonWriter> Logs(JsonElement parameters)
    {
        var limit = Math.Clamp(Number(parameters, "limit") ?? 200, 1, DevtoolsLog.Capacity);
        var since = Since(Text(parameters, "since"));
        var source = Text(parameters, "source");
        var level = LevelRank(Text(parameters, "level") ?? "info");
        var entries = _context.Log.Since(since, DevtoolsLog.Capacity)
            .Where(entry => (source is null || entry.Source == source) && LevelRank(entry.Level) >= level)
            .TakeLast(limit)
            .ToList();
        return writer =>
        {
            writer.WriteStartObject();
            writer.WriteStartArray("entries");
            foreach (var entry in entries)
            {
                entry.Write(writer);
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        };
    }

    private static void WriteOutcome(Utf8JsonWriter writer, string name, long callId, SparOutcome outcome)
    {
        writer.WriteStartObject();
        writer.WriteString("command", name);
        writer.WriteNumber("callId", callId);
        switch (outcome.Kind)
        {
            case SparOutcomeKind.Value:
                writer.WriteString("outcome", "success");
                WritePayload(writer, "value", outcome.Payload.Span);
                break;
            case SparOutcomeKind.Error:
                writer.WriteString("outcome", "error");
                WritePayload(writer, "error", outcome.Payload.Span);
                break;
            default:
                var failure = outcome.Failure!;
                writer.WriteString("outcome", "failure");
                writer.WriteStartObject("failure");
                writer.WriteString("kind", failure.Kind);
                writer.WriteString("code", failure.Code);
                writer.WriteString("execution", failure.Execution);
                if (failure.Detail is not null)
                {
                    writer.WriteString("detail", failure.Detail);
                }

                writer.WriteEndObject();
                break;
        }

        writer.WriteEndObject();
    }

    /// <summary>The command's JSON as JSON, or, past <see cref="MaxValue"/> bytes, its size and its start.</summary>
    private static void WritePayload(Utf8JsonWriter writer, string name, ReadOnlySpan<byte> payload)
    {
        if (payload.IsEmpty)
        {
            return;
        }

        if (payload.Length > MaxValue)
        {
            writer.WriteStartObject(name);
            writer.WriteNumber("truncatedBytes", payload.Length);
            writer.WriteString("start", Encoding.UTF8.GetString(payload[..4096]));
            writer.WriteEndObject();
            return;
        }

        writer.WritePropertyName(name);
        writer.WriteRawValue(payload, skipInputValidation: false);
    }

    private static string? ContractSnapshot(System.Reflection.Assembly? app)
    {
        using var stream = app?.GetManifestResourceStream(SparDevtools.ContractsResource);
        if (stream is null)
        {
            return null;
        }

        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    /// <summary><c>30s</c>, <c>5m</c>, <c>2h</c> back from now, or an ISO 8601 time; everything when absent.</summary>
    internal static DateTimeOffset Since(string? text)
    {
        if (text is null)
        {
            return DateTimeOffset.MinValue;
        }

        if (text.Length > 1 && double.TryParse(text.AsSpan(0, text.Length - 1), NumberStyles.Float, CultureInfo.InvariantCulture, out var amount))
        {
            TimeSpan? back = text[^1] switch
            {
                's' => TimeSpan.FromSeconds(amount),
                'm' => TimeSpan.FromMinutes(amount),
                'h' => TimeSpan.FromHours(amount),
                _ => null,
            };
            if (back is { } span)
            {
                return DateTimeOffset.UtcNow - span;
            }
        }

        return DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var time)
            ? time
            : throw Invalid($"since is '{text}': a duration such as 30s, 5m or 2h, or an ISO 8601 time.");
    }

    /// <summary>Spar's version as its package names it, without the source revision the build appends.</summary>
    private static string SparVersion()
    {
        var version = typeof(DevtoolsTools).Assembly
            .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), inherit: false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>()
            .FirstOrDefault()?.InformationalVersion ?? "";
        var plus = version.IndexOf('+', StringComparison.Ordinal);
        return plus < 0 ? version : version[..plus];
    }

    private static int LevelRank(string level) => level switch
    {
        "error" => 3,
        "warn" => 2,
        "info" => 1,
        _ => 0,
    };

    private static DevtoolsError Invalid(string message) => new("invalid", message, JsonRpc.InvalidParams);

    private static string? Text(JsonElement parameters, string name) =>
        parameters.ValueKind == JsonValueKind.Object && parameters.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int? Number(JsonElement parameters, string name) =>
        parameters.ValueKind == JsonValueKind.Object && parameters.TryGetProperty(name, out var value) && value.TryGetInt32(out var number)
            ? number
            : null;

    private static TimeSpan? Milliseconds(JsonElement parameters, string name) =>
        Number(parameters, name) is { } milliseconds ? TimeSpan.FromMilliseconds(Math.Clamp(milliseconds, 1, 600_000)) : null;
}
