namespace Spar;

/// <summary>Applying an installation file to the app's folders: the runtime's part, which <c>spar doctor</c> does not compile.</summary>
internal sealed partial class InstallationFile
{
    /// <summary>Sets the folder choices the file made, over the code's.</summary>
    public void ApplyTo(FolderOptions folders)
    {
        if (DataFolder is not null)
        {
            folders.Data(DataFolder);
        }

        if (LogsFolder is not null)
        {
            folders.Logs(LogsFolder);
        }

        if (ProfileFolder is not null)
        {
            folders.Profile(ProfileFolder);
        }

        if (EphemeralBrowserData == true)
        {
            folders.EphemeralBrowserData();
        }

        if (RemoveOnExit is not null)
        {
            folders.RemoveOnExit([.. RemoveOnExit.Select(FolderOf)]);
        }
    }

    /// <summary>Sets the log levels the file chose, over the code's; a user's <c>logging.json</c> still wins over both.</summary>
    public void ApplyTo(LoggingOptions logging)
    {
        if (LogLevelName is { } level)
        {
            logging.Level(Enum.Parse<Microsoft.Extensions.Logging.LogLevel>(level));
        }

        foreach (var (category, name) in LogCategories)
        {
            logging.Level(category, Enum.Parse<Microsoft.Extensions.Logging.LogLevel>(name));
        }
    }

    /// <summary>A folder by its name in the file, which the reader has already checked.</summary>
    private static SparFolder FolderOf(string name) => name switch
    {
        "profile" => SparFolder.Profile,
        "cache" => SparFolder.Cache,
        "data" => SparFolder.Data,
        _ => SparFolder.Logs,
    };
}
