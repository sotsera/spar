using Spar.Tool.Project;

namespace Spar.Tool.Skills;

/// <summary>
/// <c>spar skills</c> (plan, M2e; decision "M2e's decide-first questions", item 4): copies the app-author skills this
/// tool ships with, the same version as its SDK, into the app's <c>.agents/skills/</c>, for agents that read skills from
/// the repository rather than from a plugin. It writes only files whose content changed, and removes only files inside
/// the skill folders it owns that the tool no longer ships.
/// </summary>
internal static class SkillsCommand
{
    public const string Usage = """
        Usage: spar skills [--project <path>] [--list]

        Copies the Spar skills for agents (spar-app, spar-dev-loop, spar-testing, spar-shipping), at this tool's version,
        into .agents/skills/ beside the app's solution (or the app's folder without one). Files already up to date are
        left alone, so run it again after updating Spar.

        Options:
          --project <path>   The app's project file or folder (default: the current folder).
          --list             List the skills and where they would go, and write nothing.
        """;

    /// <summary>Where the tool's own copy of the skills is, beside its assembly.</summary>
    public static string Shipped => Path.Combine(AppContext.BaseDirectory, "skills");

    public static int Run(string[] args, TextWriter output, TextWriter error, string workingDirectory, string? shipped = null)
    {
        string? project = null;
        var list = false;
        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "-h" or "--help" or "-?":
                    output.WriteLine(Usage);
                    return ExitCodes.Success;
                case "--project" when index + 1 < args.Length:
                    project = args[++index];
                    break;
                case "--list":
                    list = true;
                    break;
                default:
                    error.WriteLine($"spar skills: unknown or incomplete option '{args[index]}'.");
                    error.WriteLine(Usage);
                    return ExitCodes.Usage;
            }
        }

        var source = shipped ?? Shipped;
        if (!Directory.Exists(source))
        {
            error.WriteLine($"spar skills: this tool has no skills beside it ({source}).");
            return ExitCodes.Internal;
        }

        if (AppProject.Find(Path.Combine(workingDirectory, project ?? "."), out var problem) is not { } app)
        {
            error.WriteLine($"spar skills: {problem}");
            return ExitCodes.Errors;
        }

        var target = Path.Combine(Root(app.Directory), ".agents", "skills");
        var skills = Directory.GetDirectories(source).Select(Path.GetFileName).OfType<string>().Order(StringComparer.Ordinal).ToList();
        if (list)
        {
            foreach (var skill in skills)
            {
                output.WriteLine(skill);
            }

            output.WriteLine($"into {target}");
            return ExitCodes.Success;
        }

        var (written, removed) = (0, 0);
        foreach (var skill in skills)
        {
            (var skillWritten, var skillRemoved) = Copy(Path.Combine(source, skill), Path.Combine(target, skill));
            written += skillWritten;
            removed += skillRemoved;
        }

        output.WriteLine(written + removed == 0
            ? $"The {skills.Count} skills in {target} are up to date."
            : $"{skills.Count} skills in {target}: {written} file(s) written, {removed} removed.");
        return ExitCodes.Success;
    }

    /// <summary>The solution's folder above the app (where an agent starts), or the app's own folder without one.</summary>
    private static string Root(string appDirectory)
    {
        for (var folder = new DirectoryInfo(appDirectory); folder is not null; folder = folder.Parent)
        {
            if (folder.EnumerateFiles("*.slnx").Any() || folder.EnumerateFiles("*.sln").Any())
            {
                return folder.FullName;
            }

            if (Directory.Exists(Path.Combine(folder.FullName, ".git")))
            {
                break;
            }
        }

        return appDirectory;
    }

    /// <summary>One skill's files, written when their bytes differ; files the tool no longer ships removed.</summary>
    private static (int Written, int Removed) Copy(string from, string to)
    {
        var written = 0;
        var wanted = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(from, file);
            wanted.Add(relative);
            var destination = Path.Combine(to, relative);
            var bytes = File.ReadAllBytes(file);
            if (File.Exists(destination) && File.ReadAllBytes(destination).AsSpan().SequenceEqual(bytes))
            {
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.WriteAllBytes(destination, bytes);
            written++;
        }

        var removed = 0;
        if (Directory.Exists(to))
        {
            foreach (var file in Directory.EnumerateFiles(to, "*", SearchOption.AllDirectories))
            {
                if (!wanted.Contains(Path.GetRelativePath(to, file)))
                {
                    File.Delete(file);
                    removed++;
                }
            }
        }

        return (written, removed);
    }
}
