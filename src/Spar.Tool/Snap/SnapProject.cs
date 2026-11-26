namespace Spar.Tool.Snap;

/// <summary>
/// The test project that holds the scenes (decision "M2c's decide-first questions", item 7), found without evaluating
/// MSBuild: a project file whose folder's C# files mark a method <c>[SparScene(</c> (or <c>[Spar.Testing.SparScene(</c>).
/// From a project file or a folder that has one, that project when it has scenes; otherwise, as from an app's folder,
/// the projects in the folders beside it (an app and its test projects are usually siblings). Folders named <c>bin</c>, <c>obj</c> and
/// <c>node_modules</c> are not read.
/// </summary>
internal static partial class SnapProject
{
    private static readonly string[] Skipped = ["bin", "obj", "node_modules", ".git"];

    public static string? Find(string path, out string problem)
    {
        problem = "";
        var full = Path.GetFullPath(path);
        IEnumerable<string> candidates;
        string where;
        if (File.Exists(full) && full.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
        {
            candidates = HasScenes(full) ? [full] : Siblings(Path.GetDirectoryName(full)!);
            where = full;
        }
        else if (Directory.Exists(full))
        {
            var own = Directory.GetFiles(full, "*.csproj").Where(HasScenes).ToList();
            candidates = own.Count > 0 ? own : Siblings(full);
            where = full;
        }
        else
        {
            problem = $"{full} is not a project file or a folder.";
            return null;
        }

        var found = candidates.Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal).ToList();
        if (found.Count == 1)
        {
            return found[0];
        }

        problem = found.Count == 0
            ? $"no test project with scenes was found from {where}: add a [SparScene(\"name\")] method to the app's end-to-end test project, or name it with --project."
            : $"several test projects have scenes ({string.Join(", ", found.Select(Path.GetFileName))}): name one with --project.";
        return null;
    }

    /// <summary>The projects with scenes in the folders beside <paramref name="folder"/>.</summary>
    private static IEnumerable<string> Siblings(string folder)
    {
        var parent = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(folder));
        if (parent is null)
        {
            return [];
        }

        return Directory.GetDirectories(parent)
            .Where(sibling => !Skipped.Contains(Path.GetFileName(sibling), StringComparer.OrdinalIgnoreCase))
            .SelectMany(sibling => Directory.GetFiles(sibling, "*.csproj"))
            .Where(HasScenes);
    }

    private static bool HasScenes(string project)
    {
        foreach (var file in SourceFiles(Path.GetDirectoryName(project)!))
        {
            if (SceneMark().IsMatch(File.ReadAllText(file)))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary><c>[SparScene(</c>, also with its namespace (<c>[Spar.Testing.SparScene(</c>) or its <c>Attribute</c> suffix.</summary>
    [System.Text.RegularExpressions.GeneratedRegex(@"\[\s*(?:[\w.]+\.)?SparScene(?:Attribute)?\s*\(", System.Text.RegularExpressions.RegexOptions.CultureInvariant)]
    private static partial System.Text.RegularExpressions.Regex SceneMark();

    private static IEnumerable<string> SourceFiles(string folder)
    {
        foreach (var file in Directory.EnumerateFiles(folder, "*.cs"))
        {
            yield return file;
        }

        foreach (var child in Directory.EnumerateDirectories(folder))
        {
            if (Skipped.Contains(Path.GetFileName(child), StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (var file in SourceFiles(child))
            {
                yield return file;
            }
        }
    }
}
