using System.Globalization;

namespace Spar.SingleInstance;

/// <summary>"verb --name value ... -- forwarded arguments".</summary>
internal sealed class Options
{
    private readonly Dictionary<string, string> _values = [];

    private Options(string verb)
    {
        Verb = verb;
    }

    public string Verb { get; }

    public string[] Forwarded { get; private set; } = [];

    public string Data => Required("--data");

    public string Naming => Value("--naming") ?? "fileid";

    public int HoldMs => Number("--hold-ms", 4000);

    public int SlowExitMs => Number("--slow-exit-ms", 0);

    public int AckTimeoutMs => Number("--ack-timeout-ms", 2000);

    public int LockWaitMs => Number("--lock-wait-ms", 5000);

    public int ConnectTimeoutMs => Number("--connect-timeout-ms", 100);

    public string? Window => Value("--window");

    public string Security => Value("--security") ?? "spar";

    public bool AllowForeground => Value("--no-allow-fg") is null;

    public long? LaunchTicks => Value("--t0") is { } text ? long.Parse(text, CultureInfo.InvariantCulture) : null;

    public static Options Parse(string[] args)
    {
        var options = new Options(args.Length > 0 ? args[0] : "");
        for (var i = 1; i < args.Length; i++)
        {
            if (args[i] == "--")
            {
                options.Forwarded = args[(i + 1)..];
                break;
            }

            var hasValue = i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal);
            options._values[args[i]] = hasValue ? args[++i] : "";
        }

        return options;
    }

    public string? Value(string name) => _values.GetValueOrDefault(name);

    public string Required(string name) => Value(name) ?? throw new ArgumentException($"{name} is required");

    public int Number(string name, int fallback) =>
        Value(name) is { } text ? int.Parse(text, CultureInfo.InvariantCulture) : fallback;
}
