namespace Spar;

/// <summary>Where the app keeps its data. Every folder derives from one data root unless the app sets it.</summary>
public sealed record SparFolders(string DataRoot, string Data, string Logs, string Cache, string WebView)
{
    public static SparFolders Under(string dataRoot, FolderOptions options, string? webView = null) => new(
        dataRoot,
        options.DataFolder ?? Path.Combine(dataRoot, "data"),
        options.LogsFolder ?? Path.Combine(dataRoot, "logs"),
        Path.Combine(dataRoot, "cache"),
        webView ?? Path.Combine(dataRoot, "webview"));

    /// <summary>A test host moves every folder under its own root, including the ones the app set.</summary>
    public static SparFolders ForTest(string dataRoot, string webView) =>
        new(dataRoot, Path.Combine(dataRoot, "data"), Path.Combine(dataRoot, "logs"), Path.Combine(dataRoot, "cache"), webView);
}

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
