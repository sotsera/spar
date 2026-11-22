using Spar.Tool.Frontend;
using Spar.Tool.Project;

namespace Spar.Tool.Init;

/// <summary>
/// What one <c>spar init frontend</c> works on: the app, the frontend's final folder, and, with a framework, the staging
/// folder under the app's <c>obj/spar/init/</c> that Vite's scaffolder writes into until the user says yes.
/// </summary>
internal sealed class InitPlan(AppProject project, string frontend, string relative, string? framework)
{
    public AppProject Project { get; } = project;

    /// <summary>The frontend's final folder.</summary>
    public string Frontend { get; } = frontend;

    /// <summary>The frontend's folder relative to the app's, with <c>/</c>: <c>frontend</c>, or what <c>SparFrontendPath</c> gets.</summary>
    public string Relative { get; } = relative;

    public string? Framework { get; } = framework;

    /// <summary>Vite's TypeScript template for the framework.</summary>
    public string? Template => Framework switch
    {
        "react" => "react-ts",
        "vue" => "vue-ts",
        _ => null,
    };

    /// <summary>The scaffolder's working folder: under <c>obj/</c>, one of this run's own.</summary>
    public string StagingParent { get; } = Path.Combine(project.Directory, "obj", "spar", "init", Guid.NewGuid().ToString("N")[..12]);

    /// <summary>The scaffolded frontend before it moves into place; null without a framework.</summary>
    public string? Staged => Framework is null ? null : Path.Combine(StagingParent, Path.GetFileName(Frontend));

    /// <summary>The folder the edits read from: the staged scaffold, or the existing frontend.</summary>
    public string Source => Staged ?? Frontend;

    /// <summary>Deletes this run's staging folder, if it is still there (it is moved into place when the user says yes).</summary>
    public void DeleteStaging()
    {
        if (!Directory.Exists(StagingParent))
        {
            return;
        }

        try
        {
            Directory.Delete(StagingParent, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Under obj/, which the build owns: what is left there does no harm and a clean removes it.
        }
    }
}

/// <summary>One file init changes: where it is written, how it is shown, and its text before (null for a new file) and after.</summary>
internal sealed record FileEdit(string Path, string Shown, string? Before, string After);

/// <summary>Everything init would change, worked out before anything is written.</summary>
internal sealed record InitChanges(IReadOnlyList<FileEdit> Edits, string? ViteProblem, bool Install, bool StandInClient)
{
    public bool Any => Edits.Count > 0 || Install;

    public static InitChanges Compute(InitPlan plan)
    {
        var edits = new List<FileEdit>();
        var project = plan.Project;
        var source = plan.Source;
        var shownFrontend = plan.Relative;

        var packagePath = System.IO.Path.Combine(source, "package.json");
        var packageText = File.ReadAllText(packagePath);
        var specifier = PackageJson.ClientSpecifier(plan.Frontend, project.ClientDirectory);
        var (contents, _) = PackageJson.Read(packageText);
        var updated = packageText;
        if (contents is null || !PackageJson.NamesClient(contents.SparDependency, plan.Frontend, project.ClientDirectory))
        {
            updated = PackageJson.Set(updated, "dependencies", PackageJson.SparPackage, specifier, first: true);
        }

        if (contents?.DevScript is null)
        {
            updated = PackageJson.Set(updated, "scripts", "dev", "vite", first: false);
        }

        if (contents?.BuildScript is null)
        {
            updated = PackageJson.Set(updated, "scripts", "build", "vite build", first: false);
        }

        if (updated != packageText)
        {
            edits.Add(new FileEdit(System.IO.Path.Combine(plan.Frontend, "package.json"), $"{shownFrontend}/package.json", packageText, updated));
        }

        string? viteProblem = null;
        if (ViteConfig.Find(source) is { } config)
        {
            var name = System.IO.Path.GetFileName(config);
            var text = File.ReadAllText(config);
            var (edited, problem) = ViteConfig.AddPlugin(text, name);
            viteProblem = problem;
            if (edited is not null && edited != text)
            {
                edits.Add(new FileEdit(System.IO.Path.Combine(plan.Frontend, name), $"{shownFrontend}/{name}", text, edited));
            }
        }
        else
        {
            viteProblem = $"{shownFrontend}/ has no vite.config file.";
        }

        var defaultFolder = string.Equals(plan.Relative, AppProject.DefaultFrontend, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        var propertyMatches = project.FrontendPathProperty is { } property && string.Equals(property, plan.Relative, StringComparison.Ordinal);
        if (!propertyMatches && !(defaultFolder && project.FrontendPathProperty is null))
        {
            var projectText = File.ReadAllText(project.File);
            if (ProjectFileEdit.SetFrontendPath(projectText, plan.Relative) is { } edited && edited != projectText)
            {
                edits.Add(new FileEdit(project.File, System.IO.Path.GetFileName(project.File), projectText, edited));
            }
        }

        var lockfile = System.IO.Path.Combine(source, Sdk.Tasks.FrontendInstallState.Lockfile);
        var install = updated != packageText || !File.Exists(lockfile) || !Directory.Exists(System.IO.Path.Combine(source, "node_modules"));
        var standIn = install && !File.Exists(System.IO.Path.Combine(project.ClientDirectory, "package.json"));
        return new InitChanges(edits, viteProblem, install, standIn);
    }
}
