namespace Spar;

/// <summary>Where the app keeps its files. Every folder derives from one data root unless the app sets it.</summary>
public sealed record SparFolders(string DataRoot, string Data, string Logs, string Cache)
{
    /// <summary>The web engine's own folder (its profile): beside the app's data, under the same root.</summary>
    internal string WebEngine => Path.Combine(DataRoot, "WebView2");

    /// <summary>The folders under <paramref name="dataRoot"/>, with the app's own choices applied.</summary>
    public static SparFolders Under(string dataRoot, FolderOptions options) => new(
        dataRoot,
        options.DataFolder ?? Path.Combine(dataRoot, "data"),
        options.LogsFolder ?? Path.Combine(dataRoot, "logs"),
        Path.Combine(dataRoot, "cache"));

    /// <summary>
    /// A desktop app's folders: one per-user root named after the app, in the platform's own per-user folder. This is
    /// the one place that decides it. Provisional (M1b step 1): the root is named after the app's assembly until the
    /// app id and its per-channel names arrive (decision "M1b's remaining questions without a spike", M1b step 5).
    /// </summary>
    internal static SparFolders ForApp(string appName, FolderOptions options) =>
        Under(Path.Combine(PerUserRoot(), appName), options);

    /// <summary>A test host moves every folder under its own root, including the ones the app set.</summary>
    internal static SparFolders ForTest(string dataRoot) =>
        new(dataRoot, Path.Combine(dataRoot, "data"), Path.Combine(dataRoot, "logs"), Path.Combine(dataRoot, "cache"));

    /// <summary>
    /// The platform's per-user data folder. On Windows it is read from <c>LOCALAPPDATA</c> (decision "M1b's remaining
    /// questions without a spike": the black-box data root is the environment only), falling back to the known folder
    /// when the variable is missing, relative, or a network path, which the profile must never be.
    /// </summary>
    private static string PerUserRoot()
    {
        if (OperatingSystem.IsWindows())
        {
            var variable = Environment.GetEnvironmentVariable("LOCALAPPDATA");
            if (IsLocalAbsolute(variable))
            {
                return variable!;
            }
        }

        return Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.Create);
    }

    private static bool IsLocalAbsolute(string? path) =>
        !string.IsNullOrEmpty(path)
        && Path.IsPathFullyQualified(path)
        && !path.StartsWith(@"\\", StringComparison.Ordinal)
        && !path.StartsWith("//", StringComparison.Ordinal);
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
