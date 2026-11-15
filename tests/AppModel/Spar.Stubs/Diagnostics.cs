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
