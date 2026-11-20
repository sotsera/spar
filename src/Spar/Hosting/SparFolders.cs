using System.Runtime.InteropServices;

namespace Spar;

/// <summary>
/// Where the app keeps its files, registered as a singleton for app code. Every folder derives from one per-user root
/// named after the app id (plan section 3c) unless <c>spar.Folders(…)</c> sets it; a test host moves them all under
/// its own root. <see cref="Profile"/> is the browser profile Spar asked for: the host checks the one actually in use.
/// </summary>
public sealed partial record SparFolders(string DataRoot, string Data, string Logs, string Cache, string Profile)
{
    /// <summary>What a Development build appends to the app id, for its folders and its identity to the OS.</summary>
    internal const string DevelopmentSuffix = ".Development";

    /// <summary><c>DRIVE_REMOTE</c>, what <c>GetDriveTypeW</c> answers for a drive letter mapped to a share.</summary>
    private const uint DriveRemote = 4;

    /// <summary>
    /// The browser profile's folder under the data root, named after the engine that writes it (decision "The browser
    /// profile folder per OS"): <c>WebView2</c> on Windows, <c>WebKit</c> on Linux, as the cache's <c>cache/WebKit</c>.
    /// </summary>
    internal static string ProfileFolderName => OperatingSystem.IsWindows() ? "WebView2" : "WebKit";

    /// <summary>The folders under <paramref name="dataRoot"/>, with the app's own choices applied.</summary>
    public static SparFolders Under(string dataRoot, FolderOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new SparFolders(
            dataRoot,
            options.DataFolder ?? Path.Combine(dataRoot, "data"),
            options.LogsFolder ?? Path.Combine(dataRoot, "logs"),
            Path.Combine(dataRoot, "cache"),
            options.ProfileFolder ?? Path.Combine(dataRoot, ProfileFolderName));
    }

    /// <summary>
    /// The app id as this build presents itself to the OS and names its folders (decision "M1b's remaining questions
    /// without a spike"): the id itself in Production, with <see cref="DevelopmentSuffix"/> in Development, so a Debug
    /// run never shares data, a profile or a taskbar identity with an installed copy.
    /// </summary>
    internal static string ChannelId(string appId, bool development) => development ? appId + DevelopmentSuffix : appId;

    /// <summary>
    /// A desktop app's folders: <c>%LOCALAPPDATA%\&lt;app id&gt;</c> on Windows (with <c>.Development</c> in a
    /// Development build), and the app's own choices. This is the one place that decides it.
    /// </summary>
    internal static SparFolders ForApp(string appId, bool development, FolderOptions options) =>
        Under(Path.Combine(PerUserRoot(), ChannelId(appId, development)), options);

    /// <summary>A test host moves every folder under its own root, including the ones the app set.</summary>
    internal static SparFolders ForTest(string dataRoot) => new(
        dataRoot,
        Path.Combine(dataRoot, "data"),
        Path.Combine(dataRoot, "logs"),
        Path.Combine(dataRoot, "cache"),
        Path.Combine(dataRoot, ProfileFolderName));

    /// <summary>
    /// Why the profile folder the app chose cannot be used, or null when it can (or the app chose none): it must be an
    /// absolute path on a local drive. A relative path would depend on the working directory, and a profile on a
    /// network share is slow and readable by whoever can read the share.
    /// </summary>
    internal static string? ProfileProblem(string? path)
    {
        if (path is null)
        {
            return null;
        }

        if (!Path.IsPathFullyQualified(path))
        {
            return $"The browser profile folder '{path}' (spar.Folders) is not an absolute path on a local drive.";
        }

        if (IsNetwork(path))
        {
            return $"The browser profile folder '{path}' (spar.Folders) is on a network drive, slow and readable by others: use a local drive.";
        }

        return null;
    }

    /// <summary>
    /// Why the app's data root cannot be used, or null when it can: a root on a network drive, which a network
    /// <c>LOCALAPPDATA</c> gives, is refused at start, before anything is read or written there (decision "M1b's
    /// remaining questions without a spike", its security review). Falling back to the known folder instead would move
    /// the app's data somewhere the process that started it did not ask for.
    /// </summary>
    internal static string? DataRootProblem(string dataRoot)
    {
        if (!IsNetwork(dataRoot))
        {
            return null;
        }

        return $"The app's data folder '{dataRoot}' is on a network drive (LOCALAPPDATA or spar.Folders), slow and " +
            "readable by others: the app keeps its data, logs and browser profile on a local drive.";
    }

    /// <summary>
    /// The platform's per-user data folder. On Windows it is read from <c>LOCALAPPDATA</c> (decision "M1b's remaining
    /// questions without a spike": the black-box data root is the environment only), falling back to the known folder
    /// when the variable is missing or relative. A network path is kept, and the host refuses it at start
    /// (<see cref="DataRootProblem"/>).
    /// </summary>
    private static string PerUserRoot()
    {
        if (OperatingSystem.IsWindows())
        {
            var variable = Environment.GetEnvironmentVariable("LOCALAPPDATA");
            if (!string.IsNullOrEmpty(variable) && Path.IsPathFullyQualified(variable))
            {
                return variable;
            }
        }

        return Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.Create);
    }

    /// <summary>A UNC path (<c>\\server\share</c>, <c>\\?\UNC\…</c>) or, on Windows, a drive letter mapped to a share.</summary>
    private static bool IsNetwork(string path)
    {
        if (path.StartsWith(@"\\", StringComparison.Ordinal) || path.StartsWith("//", StringComparison.Ordinal))
        {
            var device = path.StartsWith(@"\\?\", StringComparison.Ordinal) || path.StartsWith(@"\\.\", StringComparison.Ordinal);
            return !device || path.AsSpan(4).StartsWith("UNC", StringComparison.OrdinalIgnoreCase);
        }

        if (!OperatingSystem.IsWindows() || Path.GetPathRoot(path) is not { Length: > 0 } root)
        {
            return false;
        }

        return GetDriveType(root) == DriveRemote;
    }

    /// <summary>The kind of drive a root path is on (Windows only).</summary>
    [LibraryImport("kernel32", EntryPoint = "GetDriveTypeW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint GetDriveType(string rootPathName);
}

/// <summary>
/// The folder choices <c>Program.cs</c> can make, until <c>spar.ResolveFolders()</c> decides the folders: a choice
/// after that throws, so the folders <c>Program.cs</c> got never differ from the <see cref="SparFolders"/> service.
/// </summary>
public sealed class FolderOptions
{
    /// <summary>Why a folder choice after <c>spar.ResolveFolders()</c> is refused.</summary>
    internal const string ResolvedMessage =
        "The folders were already resolved by spar.ResolveFolders(): call spar.Folders(…) before it, so the folders " +
        "Program.cs got are the ones the app's services get.";

    private bool _frozen;

    public string? DataFolder { get; private set; }

    public string? LogsFolder { get; private set; }

    public string? ProfileFolder { get; private set; }

    public FolderOptions Data(string path)
    {
        ThrowIfFrozen();
        DataFolder = path;
        return this;
    }

    public FolderOptions Logs(string path)
    {
        ThrowIfFrozen();
        LogsFolder = path;
        return this;
    }

    /// <summary>
    /// The browser profile (cookies, storage, cache), by default the folder named after the web engine under the app's
    /// per-user folder: <c>WebView2</c> on Windows, <c>WebKit</c> on Linux. It must be an absolute path on a local drive:
    /// anything else stops the start with a message. Every window shares it, and no other app may use it at the same
    /// time. Spar checks the folder the browser actually uses, since an administrator's policy can move it, and no file
    /// grant ever reaches it.
    /// </summary>
    public FolderOptions Profile(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ThrowIfFrozen();
        ProfileFolder = path;
        return this;
    }

    /// <summary>Refuses further choices: the folders are decided.</summary>
    internal void Freeze() => _frozen = true;

    private void ThrowIfFrozen()
    {
        if (_frozen)
        {
            throw new InvalidOperationException(ResolvedMessage);
        }
    }
}
