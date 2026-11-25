using Spar.Tool.Frontend;
using Spar.Tool.Project;

namespace Spar.Tool.Init;

/// <summary>
/// <c>spar init frontend</c> (decision "M1d step 6b's shape: the spar tool"): wires a Vite frontend into a Spar app, the
/// <c>@spar/app</c> <c>file:</c> dependency, Spar's Vite plugin and, when the folder is not <c>frontend/</c>,
/// <c>SparFrontendPath</c>; with <c>--framework react|vue</c> it first makes the frontend with Vite's own scaffolder
/// (<c>npm create vite@<see cref="CreateViteVersion"/></c>, the TypeScript template). It shows every change as a diff
/// and asks before writing (<c>--yes</c> skips the question; without a terminal to ask and without <c>--yes</c> it
/// writes nothing). It writes only inside the app's folder, and runs npm only with <c>--ignore-scripts</c>.
/// </summary>
internal static class InitCommand
{
    /// <summary>Vite's scaffolder, pinned (decision "The spar tool (implementation record; choices for review; 2026-11-22)").</summary>
    public const string CreateViteVersion = "9.3.4";

    public const string Usage = """
        Usage: spar init frontend [options]

        Wires a Vite frontend into a Spar app: the @spar/app dependency, Spar's Vite plugin and, when the folder is not
        frontend/, SparFrontendPath in the project. Shows the changes and asks before writing them.

        Options:
          --project <path>       The app's project file or folder (default: the current folder).
          --frontend <folder>    The frontend's folder, inside the app's folder (default: SparFrontendPath, or frontend).
          --framework <name>     react or vue: first make the frontend with Vite's scaffolder (npm create vite, TypeScript).
          --yes                  Write without asking.
          -h, --help             Show this help.

        Exit codes: 0 written or nothing to do, 1 nothing written, 2 a step failed, 3 written except Vite's configuration
        (the snippet to add was printed), 64 a wrong command line, 70 the tool itself failed.
        """;

    public static int Run(IReadOnlyList<string> arguments, InitConsole console, INpm npm, string workingDirectory)
    {
        var options = Parse(arguments, console);
        if (options is null)
        {
            return ExitCodes.Usage;
        }

        if (options.Help)
        {
            console.Output.WriteLine(Usage);
            return ExitCodes.Success;
        }

        var project = AppProject.Find(Path.Combine(workingDirectory, options.Project ?? "."), out var problem);
        if (project is null)
        {
            console.Error.WriteLine($"spar init frontend: {problem}");
            return ExitCodes.Errors;
        }

        var frontend = Path.GetFullPath(Path.Combine(project.Directory, options.Frontend ?? project.FrontendPathProperty ?? AppProject.DefaultFrontend));
        var relative = Path.GetRelativePath(project.Directory, frontend).Replace('\\', '/');
        if (relative == "." || relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative)
            || relative.Split('/')[0] is "bin" or "obj" or "node_modules")
        {
            console.Error.WriteLine($"spar init frontend: the frontend's folder must be a folder inside the app's folder ({project.Directory}), and not bin/, obj/ or node_modules/; {frontend} is not.");
            return ExitCodes.Usage;
        }

        var plan = new InitPlan(project, frontend, relative, options.Framework);
        return Execute(plan, options.Yes, console, npm);
    }

    private static int Execute(InitPlan plan, bool yes, InitConsole console, INpm npm)
    {
        if (plan.Framework is not null && Directory.Exists(plan.Frontend) && Directory.EnumerateFileSystemEntries(plan.Frontend).Any())
        {
            console.Error.WriteLine($"spar init frontend: {plan.Relative}/ already exists and is not empty. Leave out --framework to wire it, or name another folder with --frontend.");
            return ExitCodes.Errors;
        }

        if (plan.Framework is null && !File.Exists(Path.Combine(plan.Frontend, "package.json")))
        {
            console.Error.WriteLine($"spar init frontend: {plan.Relative}/ has no package.json. Make a frontend with --framework react (or vue), or name an existing one with --frontend.");
            return ExitCodes.Errors;
        }

        if (plan.Framework is not null && !yes && !console.Interactive)
        {
            console.Output.WriteLine($"spar init frontend would make {plan.Relative}/ with npm create vite@{CreateViteVersion} (template {plan.Template}), then wire it into {plan.Project.Name}.");
            console.Output.WriteLine("Nothing written: no terminal to ask in. Run it again with --yes to make and wire the frontend.");
            return ExitCodes.NotWritten;
        }

        try
        {
            if (plan.Framework is not null)
            {
                console.Output.WriteLine($"Making {plan.Relative}/ with npm create vite@{CreateViteVersion} --template {plan.Template} (into obj/ first, so nothing else changes until you say yes)…");
                if (!Scaffold(plan, npm, console))
                {
                    return ExitCodes.Errors;
                }
            }

            var changes = InitChanges.Compute(plan);
            Show(plan, changes, console);
            if (!changes.Any)
            {
                console.Output.WriteLine($"The frontend of {plan.Project.Name} is already wired: nothing to do.");
                return ExitCodes.Success;
            }

            if (!Confirm(yes, console))
            {
                return ExitCodes.NotWritten;
            }

            return Apply(plan, changes, npm, console);
        }
        finally
        {
            plan.DeleteStaging();
        }
    }

    private static bool Scaffold(InitPlan plan, INpm npm, InitConsole console)
    {
        Directory.CreateDirectory(plan.StagingParent);
        var name = Path.GetFileName(plan.Frontend);
        var result = npm.Run(
            ["create", "--yes", "--ignore-scripts", $"vite@{CreateViteVersion}", name, "--", "--no-interactive", "--no-immediate", "--template", plan.Template!],
            plan.StagingParent);
        if (result.ExitCode != 0 || !File.Exists(Path.Combine(plan.Staged!, "package.json")))
        {
            console.Error.WriteLine($"spar init frontend: npm create vite@{CreateViteVersion} failed (exit code {result.ExitCode}); nothing was written outside obj/.");
            console.Error.WriteLine(result.Output.TrimEnd());
            return false;
        }

        return true;
    }

    private static void Show(InitPlan plan, InitChanges changes, InitConsole console)
    {
        var output = console.Output;
        if (plan.Staged is not null)
        {
            var files = Directory.EnumerateFiles(plan.Staged, "*", SearchOption.AllDirectories)
                .Select(file => plan.Relative + "/" + Path.GetRelativePath(plan.Staged, file).Replace('\\', '/'))
                .Order(StringComparer.Ordinal)
                .ToList();
            output.WriteLine();
            output.WriteLine($"New: {plan.Relative}/, Vite's {plan.Template} template (create-vite {CreateViteVersion}), {files.Count} files:");
            foreach (var file in files)
            {
                output.WriteLine($"  {file}");
            }
        }

        foreach (var edit in changes.Edits)
        {
            output.WriteLine();
            output.Write(LineDiff.Unified(edit.Shown, edit.Before, edit.After));
        }

        if (changes.ViteProblem is { } problem)
        {
            output.WriteLine();
            output.WriteLine($"Not edited: {problem} Add Spar's plugin to it by hand:");
            output.WriteLine(ViteConfig.Snippet);
        }

        if (changes.Install)
        {
            output.WriteLine();
            var standIn = changes.StandInClient ? " (with a stand-in package.json in obj/spar/client until the first build writes the generated client)" : "";
            output.WriteLine($"Then: npm install --ignore-scripts in {plan.Relative}/, which writes package-lock.json and node_modules{standIn}.");
        }

        output.WriteLine();
    }

    private static bool Confirm(bool yes, InitConsole console)
    {
        if (yes)
        {
            return true;
        }

        if (!console.Interactive)
        {
            console.Output.WriteLine("Nothing written: no terminal to ask in. Run it again with --yes to write these changes.");
            return false;
        }

        console.Output.Write("Write these changes? [y/N] ");
        var answer = console.Input.ReadLine()?.Trim();
        if (answer is "y" or "Y" or "yes" or "Yes")
        {
            return true;
        }

        console.Output.WriteLine("Nothing written.");
        return false;
    }

    private static int Apply(InitPlan plan, InitChanges changes, INpm npm, InitConsole console)
    {
        if (plan.Staged is not null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(plan.Frontend)!);
            Directory.Move(plan.Staged, plan.Frontend);
        }

        foreach (var edit in changes.Edits)
        {
            File.WriteAllText(edit.Path, edit.After);
            console.Output.WriteLine($"Wrote {edit.Shown}.");
        }

        if (changes.Install)
        {
            if (changes.StandInClient)
            {
                Directory.CreateDirectory(plan.Project.ClientDirectory);
                File.WriteAllText(Path.Combine(plan.Project.ClientDirectory, "package.json"), "{\n  \"name\": \"@spar/app\",\n  \"version\": \"0.0.0\"\n}\n");
            }

            console.Output.WriteLine($"Running npm install --ignore-scripts in {plan.Relative}/…");
            var result = npm.Run(["install", "--ignore-scripts", "--no-audit", "--no-fund"], plan.Frontend);
            if (result.ExitCode != 0)
            {
                console.Error.WriteLine($"spar init frontend: npm install failed (exit code {result.ExitCode}); the files above were written. Run npm install --ignore-scripts in {plan.Relative}/ again once npm can reach its registry.");
                console.Error.WriteLine(result.Output.TrimEnd());
                return ExitCodes.Errors;
            }
        }

        if (changes.ViteProblem is not null)
        {
            console.Output.WriteLine("Done, except Vite's configuration: add the plugin as shown above, then build the app (dotnet build).");
            return ExitCodes.Partial;
        }

        console.Output.WriteLine($"Done. Build the app (dotnet build), then run it (dotnet run). Commit {plan.Relative}/package-lock.json.");
        return ExitCodes.Success;
    }

    private static Options? Parse(IReadOnlyList<string> arguments, InitConsole console)
    {
        if (arguments.Count == 0 || arguments[0] is "-h" or "--help")
        {
            return new Options { Help = true };
        }

        if (arguments[0] != "frontend")
        {
            console.Error.WriteLine($"spar init: unknown target '{arguments[0]}'; the one there is: spar init frontend.");
            return null;
        }

        var options = new Options();
        for (var index = 1; index < arguments.Count; index++)
        {
            var hasValue = index + 1 < arguments.Count;
            switch (arguments[index])
            {
                case "--yes" or "-y":
                    options.Yes = true;
                    break;
                case "--project" when hasValue:
                    options.Project = arguments[++index];
                    break;
                case "--frontend" when hasValue:
                    options.Frontend = arguments[++index];
                    break;
                case "--framework" when hasValue && arguments[index + 1] is "react" or "vue":
                    options.Framework = arguments[++index];
                    break;
                case "-h" or "--help":
                    options.Help = true;
                    break;
                default:
                    console.Error.WriteLine($"spar init frontend: unknown or incomplete option '{arguments[index]}' (--framework takes react or vue).");
                    console.Error.WriteLine(Usage);
                    return null;
            }
        }

        return options;
    }

    private sealed class Options
    {
        public bool Help { get; set; }

        public bool Yes { get; set; }

        public string? Project { get; set; }

        public string? Frontend { get; set; }

        public string? Framework { get; set; }
    }
}

/// <summary>Where init writes its output and asks its question; <see cref="Interactive"/> when a person can answer.</summary>
internal sealed record InitConsole(TextWriter Output, TextWriter Error, TextReader Input, bool Interactive);
