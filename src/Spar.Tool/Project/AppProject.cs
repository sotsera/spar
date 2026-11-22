using System.Xml;
using System.Xml.Linq;

namespace Spar.Tool.Project;

/// <summary>
/// A Spar app's project as the tool reads it, without evaluating it: <c>&lt;Project Sdk="Spar.Sdk"&gt;</c> with an
/// <c>Exe</c> or <c>WinExe</c> output and no <c>IsTestProject</c>, as Spar.Sdk tells an app from a test project. The
/// properties are the project file's own literal values; one set in an imported file is not seen.
/// </summary>
internal sealed record AppProject(string File, string Name, string AssemblyName, string? FrontendPathProperty, string? SdkAttribute)
{
    /// <summary>The frontend folder's default, Spar.Sdk's <c>SparFrontendPath</c>.</summary>
    public const string DefaultFrontend = "frontend";

    public string Directory => Path.GetDirectoryName(File)!;

    /// <summary>The frontend folder: <c>SparFrontendPath</c> from the project, else <c>frontend/</c>.</summary>
    public string FrontendDirectory => Path.GetFullPath(Path.Combine(Directory, FrontendPathProperty ?? DefaultFrontend));

    /// <summary>The generated <c>@spar/app</c> package's folder, which the page's dependency names.</summary>
    public string ClientDirectory => Path.Combine(Directory, "obj", "spar", "client");

    /// <summary>The committed contract snapshot beside the project.</summary>
    public string SnapshotFile => Path.Combine(Directory, Name + ".contracts.snapshot");

    /// <summary>
    /// The app named by <paramref name="path"/> (a project file or a folder), or in the current folder: a folder's one
    /// Spar app project, looking one level down when the folder has none (a solution's folder). Null, with the reason,
    /// when there is none or more than one.
    /// </summary>
    public static AppProject? Find(string path, out string problem)
    {
        problem = "";
        var full = Path.GetFullPath(path);
        if (System.IO.File.Exists(full))
        {
            var project = Read(full);
            if (project is null)
            {
                problem = $"{full} is not a Spar app project: it needs <Project Sdk=\"Spar.Sdk\"> and an Exe or WinExe output, and is not a test project.";
            }

            return project;
        }

        if (!System.IO.Directory.Exists(full))
        {
            problem = $"{full} does not exist.";
            return null;
        }

        var here = Apps(full);
        if (here.Count == 0)
        {
            here = [.. System.IO.Directory.EnumerateDirectories(full).Where(folder => !IsBuildFolder(folder)).SelectMany(Apps)];
        }

        if (here.Count == 1)
        {
            return here[0];
        }

        problem = here.Count == 0
            ? $"No Spar app project in {full} or the folders in it."
            : $"More than one Spar app project in {full}: name one with --project ({string.Join(", ", here.Select(p => Path.GetRelativePath(full, p.File)))}).";
        return null;
    }

    /// <summary>The project file as a Spar app, or null when it is not one (or cannot be read).</summary>
    public static AppProject? Read(string file)
    {
        XDocument document;
        try
        {
            document = XDocument.Load(file);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or XmlException)
        {
            return null;
        }

        var root = document.Root;
        var sdk = (string?)root?.Attribute("Sdk");
        if (root is null || sdk is null || !IsSparSdk(sdk))
        {
            return null;
        }

        var outputType = Property(root, "OutputType");
        var isTest = string.Equals(Property(root, "IsTestProject"), "true", StringComparison.OrdinalIgnoreCase);
        if (isTest || outputType is null || !(outputType.Equals("Exe", StringComparison.OrdinalIgnoreCase) || outputType.Equals("WinExe", StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        var name = Path.GetFileNameWithoutExtension(file);
        var frontend = Property(root, "SparFrontendPath")?.Replace('\\', '/').TrimEnd('/');
        return new AppProject(Path.GetFullPath(file), name, Property(root, "AssemblyName") ?? name, frontend, sdk);
    }

    /// <summary>A property's last literal value in the project file (the last one wins, as in MSBuild), or null.</summary>
    public static string? Property(XElement root, string name) =>
        root.Elements().Where(e => e.Name.LocalName == "PropertyGroup").Elements().LastOrDefault(e => e.Name.LocalName == name)?.Value.Trim();

    private static bool IsSparSdk(string sdk) =>
        sdk.Split(';').Any(part => part.Trim().Split('/')[0].Equals("Spar.Sdk", StringComparison.OrdinalIgnoreCase));

    private static List<AppProject> Apps(string folder) =>
        [.. System.IO.Directory.EnumerateFiles(folder, "*.csproj").Select(Read).OfType<AppProject>()];

    private static bool IsBuildFolder(string folder) => Path.GetFileName(folder) is "bin" or "obj" or "node_modules" or ".git";
}
