namespace Spar;

/// <summary>Where the app keeps its files. Each folder defaults under the per-user app folder; test runs move them all under their own data root.</summary>
public sealed class FolderOptions
{
    internal FolderOptions()
    {
    }

    internal string? DataRoot { get; private set; }

    internal string? LogsFolder { get; private set; }

    internal string? CrashesFolder { get; private set; }

    internal string? ProfileFolder { get; private set; }

    /// <summary>The root every other folder defaults under (the WebView profile, settings, logs, crashes).</summary>
    public FolderOptions Data(string path)
    {
        DataRoot = path;
        return this;
    }

    public FolderOptions Logs(string path)
    {
        LogsFolder = path;
        return this;
    }

    /// <summary>The browser profile: an absolute path on a local drive, or the start stops with a message.</summary>
    public FolderOptions Profile(string path)
    {
        ProfileFolder = path;
        return this;
    }

    public FolderOptions Crashes(string path)
    {
        CrashesFolder = path;
        return this;
    }
}

/// <summary>What Spar records for diagnosing the app on a user's machine (3.13).</summary>
public sealed class DiagnosticsOptions
{
    internal DiagnosticsOptions()
    {
    }

    internal bool KeepsStallProbe { get; private set; }

    /// <summary>Keeps the UI-thread stall probe in a Production build, logging only.</summary>
    public DiagnosticsOptions KeepStallProbe()
    {
        KeepsStallProbe = true;
        return this;
    }
}

/// <summary>The log file's levels (3.13): a default and levels by category prefix; a per-user logging.json overrides them.</summary>
public sealed class LoggingOptions
{
    internal LoggingOptions()
    {
    }

    internal Microsoft.Extensions.Logging.LogLevel? DefaultLevel { get; private set; }

    internal List<(string Category, Microsoft.Extensions.Logging.LogLevel Level)> Categories { get; } = [];

    public LoggingOptions Level(Microsoft.Extensions.Logging.LogLevel level)
    {
        DefaultLevel = level;
        return this;
    }

    public LoggingOptions Level(string category, Microsoft.Extensions.Logging.LogLevel level)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(category);
        Categories.Add((category, level));
        return this;
    }
}
