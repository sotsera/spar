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

    public FolderOptions Crashes(string path)
    {
        CrashesFolder = path;
        return this;
    }
}

/// <summary>What the last run left behind.</summary>
public sealed record CrashRecord(DateTimeOffset At, string Exception, string AppVersion, string OsVersion);

/// <summary>Health of the app across runs; injectable. Spar never uploads anything by itself.</summary>
public interface IAppHealth
{
    /// <summary>The crash of the previous run, if there was one.</summary>
    CrashRecord? LastCrash { get; }

    /// <summary>Packs logs and crash records into one zip, written safely into a grant.</summary>
    Task<Result<FileGrant, FileAccessError>> CollectAsync(FolderGrant destination, string fileName, CancellationToken cancellationToken);
}
