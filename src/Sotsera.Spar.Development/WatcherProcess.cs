using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Sotsera.Spar.Development;

/// <summary>Retains one SDK watcher and its nested job, including incomplete startup.</summary>
internal sealed class WatcherProcess(OwnedProcessJob parent) : IDisposable
{
    internal const string SupportedSdk = "10.0.402";

    private static readonly Regex _compilerDiagnostic = new(@"\berror [A-Z]+\d+\b",
        RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private readonly OwnedProcessJob _job = new(parent);
    private WatchOutput? _stdout;
    private WatchOutput? _stderr;
    private bool _ready;
    private bool _started;

    internal Process? Process { get; private set; }
    internal Task? Exited { get; private set; }

    // The caller registers this owner before Start; failures never relinquish its job/process.
    internal void Start(string project, string directory, int generation,
        IReadOnlyDictionary<string, string> applicationVariables)
    {
        if (_started)
        {
            throw new InvalidOperationException("A watcher can start only once.");
        }
        _started = true;
        var variables = new Dictionary<string, string>(applicationVariables)
        {
            ["DOTNET_WATCH_RESTART_ON_RUDE_EDIT"] = "0",
            ["DOTNET_WATCH_SUPPRESS_BROWSER_REFRESH"] = "1",
            ["DOTNET_WATCH_SUPPRESS_LAUNCH_BROWSER"] = "1",
            ["DOTNET_WATCH_SUPPRESS_EMOJIS"] = "1",
            ["DOTNET_WATCH_LOG_LEVEL"] = "Debug",
            ["DOTNET_CLI_UI_LANGUAGE"] = "en-US",
            ["MSBUILDDISABLENODEREUSE"] = "1"
        };
        var stem = Path.Combine(directory, "watcher-" + generation.ToString(CultureInfo.InvariantCulture));
        // Redirected input never confirms the SDK restart prompt: the coordinator drains first.
        Process = _job.Start("dotnet",
            ["watch", "--project", project, "--verbose", "run", "--no-launch-profile",
                "--disable-build-servers", "--property:UseSharedCompilation=false",
                "--property:HotReloadAutoRestart=false", "--", "--spar-dev"],
            Path.GetDirectoryName(project)!, variables, stem + ".stdout.log", stem + ".stderr.log");
        Exited = Process.WaitForExitAsync();
    }

    internal void OpenOutput(string directory, int generation)
    {
        var stem = Path.Combine(directory, "watcher-" + generation.ToString(CultureInfo.InvariantCulture));
        _stdout = new WatchOutput(stem + ".stdout.log");
        _stderr = new WatchOutput(stem + ".stderr.log");
    }

    internal static bool IsCompilerDiagnostic(string line)
    {
        return _compilerDiagnostic.IsMatch(line);
    }

    internal IReadOnlyList<WatcherObservation> ReadAvailable()
    {
        var observations = new List<WatcherObservation>();
        foreach (var line in _stdout!.ReadAvailable().Concat(_stderr!.ReadAvailable()))
        {
            if (!_ready && line.Contains("dotnet watch", StringComparison.Ordinal)
                && line.Contains("Waiting for changes", StringComparison.Ordinal))
            {
                _ready = true;
                observations.Add(new WatcherObservation("ready"));
            }
            if (IsCompilerDiagnostic(line))
            {
                observations.Add(new WatcherObservation("diagnostic", line.Length > 2048 ? line[..2048] : line));
            }
            if (line.TrimStart().StartsWith("? Do you want to restart your app?", StringComparison.Ordinal))
            {
                observations.Add(new WatcherObservation("restart"));
            }
        }
        return observations;
    }

    internal async Task RetireAsync(Task? applicationExited)
    {
        if (applicationExited is not null && !applicationExited.IsCompletedSuccessfully)
        {
            throw new InvalidOperationException("The watcher must remain owned until actual application completion.");
        }
        // With no admitted application this also drains helpers from incomplete startup.
        _job.Terminate();
        while (_job.ActiveProcessIds().Length != 0)
        {
            await Task.Delay(50).ConfigureAwait(false);
        }
        if (Exited is not null)
        {
            await Exited.ConfigureAwait(false);
        }
    }

    public void Dispose()
    {
        _stdout?.Dispose();
        _stderr?.Dispose();
        Process?.Dispose();
        // Final safeguard; disposal alone is never a successful retirement receipt.
        _job.Dispose();
    }
}

internal sealed record WatcherObservation(string Kind, string? Detail = null);
