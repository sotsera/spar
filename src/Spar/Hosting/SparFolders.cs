namespace Spar;

/// <summary>Where the app keeps its files. Every folder derives from one data root unless the app sets it.</summary>
public sealed record SparFolders(string DataRoot, string Data, string Logs, string Cache)
{
    /// <summary>The folders under <paramref name="dataRoot"/>, with the app's own choices applied.</summary>
    public static SparFolders Under(string dataRoot, FolderOptions options) => new(
        dataRoot,
        options.DataFolder ?? Path.Combine(dataRoot, "data"),
        options.LogsFolder ?? Path.Combine(dataRoot, "logs"),
        Path.Combine(dataRoot, "cache"));

    /// <summary>A test host moves every folder under its own root, including the ones the app set.</summary>
    internal static SparFolders ForTest(string dataRoot) =>
        new(dataRoot, Path.Combine(dataRoot, "data"), Path.Combine(dataRoot, "logs"), Path.Combine(dataRoot, "cache"));
}

/// <summary>The folder choices <c>Program.cs</c> can make.</summary>
public sealed class FolderOptions
{
    public string? DataFolder { get; private set; }

    public string? LogsFolder { get; private set; }

    public FolderOptions Data(string path)
    {
        DataFolder = path;
        return this;
    }

    public FolderOptions Logs(string path)
    {
        LogsFolder = path;
        return this;
    }
}
