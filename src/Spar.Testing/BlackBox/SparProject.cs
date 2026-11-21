namespace Spar.Testing;

/// <summary>
/// A tested app as the black-box fixture launches it: what the generated <c>Projects</c> class gives for each
/// <c>SparTestedApplication</c> of a test project with <c>&lt;SparBlackBoxTests&gt;true&lt;/SparBlackBoxTests&gt;</c>,
/// as in <c>SparBlackBox.LaunchAsync(Projects.Notebook)</c>. The build publishes the app's Production NativeAOT
/// executable into the test project's <c>obj/</c> and copies it beside the tests, so the path works from the test
/// project's build output and from a publish folder of it alike.
/// </summary>
public sealed class SparProject
{
    /// <summary>The folder beside the tests that holds each tested app's executable, one folder per app.</summary>
    internal const string AppsFolder = "spar-apps";

    private readonly string _executable;
    private readonly string? _builtPath;

    /// <summary>The app's name: its assembly's, which also names its executable and the <c>Projects</c> property.</summary>
    public string Name { get; }

    /// <summary>The app id (<c>SparAppId</c>), which names its per-user folder.</summary>
    public string AppId { get; }

    /// <summary>The app's Production executable beside the tests: <c>spar-apps/&lt;name&gt;/&lt;executable&gt;</c>.</summary>
    public string ExecutablePath => _builtPath ?? Path.Combine(AppContext.BaseDirectory, AppsFolder, Name, _executable);

    /// <summary>The app's project folder on the machine that built the tests.</summary>
    public string ProjectDirectory { get; }

    public SparProject(string name, string appId, string executable, string projectDirectory)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentException.ThrowIfNullOrEmpty(appId);
        ArgumentException.ThrowIfNullOrEmpty(executable);
        Name = name;
        AppId = appId;
        _executable = executable;
        ProjectDirectory = projectDirectory;
    }

    /// <summary>
    /// An executable a runner built elsewhere, by its absolute path, for Spar's own tests: a Development build of a test
    /// app, or a build with another page-security declaration (read by the build, so an executable of its own). Its app id
    /// is the folder name the build uses (a Development build's ends in <c>.Development</c>).
    /// </summary>
    internal static SparProject Built(string executablePath, string name, string appId) =>
        new(name, appId, Path.GetFileName(executablePath), Path.GetDirectoryName(executablePath) ?? "", executablePath);

    private SparProject(string name, string appId, string executable, string projectDirectory, string builtPath)
        : this(name, appId, executable, projectDirectory)
    {
        _builtPath = Path.GetFullPath(builtPath);
    }

    public override string ToString() => $"{Name} ({AppId})";
}
