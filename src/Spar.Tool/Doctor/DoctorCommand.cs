using Spar.Runtime;
using Spar.Tool.Machine;
using Spar.Tool.Project;

namespace Spar.Tool.Doctor;

/// <summary>
/// <c>spar doctor [--project &lt;path&gt;] [--app &lt;file&gt;] [--verbose] [--json]</c>: the machine, the app's project (the one in
/// the current folder, or <c>--project</c>), and the build's identity (<c>--app</c>, or the project's newest build
/// output), each finding with a stable id, a severity and its fix. Exit codes: <see cref="ExitCodes"/>.
/// </summary>
internal static class DoctorCommand
{
    public const string Usage = """
        Usage: spar doctor [options]

        Checks the machine, the Spar app's project and the build's identity, and says how to fix what it finds.

        Options:
          --project <path>  The app's project file or folder (default: the current folder, or the one app in a folder in it).
          --app <file>      The executable or DLL whose build identity to read (default: the project's newest build output).
          --verbose         List every check, not only what needs attention.
          --json            Write every finding as JSON instead of text.
          -h, --help        Show this help.

        Exit codes: 0 nothing to fix, 1 warnings, 2 errors, 64 a wrong command line, 70 the doctor itself failed.
        """;

    public static int Run(IReadOnlyList<string> arguments, IMachine machine, TextWriter output, TextWriter error, string toolVersion, string workingDirectory)
    {
        string? projectPath = null;
        string? appFile = null;
        var json = false;
        var verbose = false;
        for (var index = 0; index < arguments.Count; index++)
        {
            switch (arguments[index])
            {
                case "--verbose" or "-v":
                    verbose = true;
                    break;
                case "--json":
                    json = true;
                    break;
                case "--project" when index + 1 < arguments.Count:
                    projectPath = arguments[++index];
                    break;
                case "--app" when index + 1 < arguments.Count:
                    appFile = arguments[++index];
                    break;
                case "-h" or "--help" or "-?":
                    output.WriteLine(Usage);
                    return ExitCodes.Success;
                default:
                    error.WriteLine($"spar doctor: unknown or incomplete option '{arguments[index]}'.");
                    error.WriteLine(Usage);
                    return ExitCodes.Usage;
            }
        }

        var report = Diagnose(machine, workingDirectory, projectPath, appFile, toolVersion);
        if (json)
        {
            DoctorReport.WriteJson(output, report);
        }
        else
        {
            DoctorReport.WriteText(output, report, verbose);
        }

        return report.ExitCode;
    }

    /// <summary>Every check, in the order they are shown: the machine, the project, the build's identity.</summary>
    public static Report Diagnose(IMachine machine, string workingDirectory, string? projectPath, string? appFile, string toolVersion)
    {
        var findings = new List<Finding>();
        var project = AppProject.Find(Path.Combine(workingDirectory, projectPath ?? "."), out var problem);
        var folder = project?.Directory ?? Path.GetFullPath(workingDirectory);
        var globalJson = GlobalJson.Find(folder);

        findings.AddRange(DotnetChecks.Check(machine, folder, globalJson, toolVersion));
        findings.AddRange(NodeChecks.Check(machine));
        if (machine.Platform == Platform.Windows)
        {
            findings.AddRange(WindowsChecks.Check(machine));
        }
        else if (machine.Platform == Platform.Linux)
        {
            findings.AddRange(LinuxChecks.Check(machine));
        }

        if (project is not null)
        {
            findings.Add(Finding.Info(FindingIds.Project, Area.Project, $"{project.Name}, {project.File}."));
            findings.AddRange(ProjectChecks.Check(project));
        }
        else if (projectPath is not null)
        {
            findings.Add(Finding.Error(FindingIds.Project, Area.Project, problem, "Name a Spar app's project file or its folder with --project."));
        }
        else if (appFile is null)
        {
            findings.Add(Finding.Info(FindingIds.Project, Area.Project, $"{problem} The project's checks were skipped; run doctor in the app's folder, or name it with --project."));
        }

        var (identityFindings, identity, file) = IdentityChecks.Check(appFile is null ? null : Path.Combine(workingDirectory, appFile), project);
        findings.AddRange(identityFindings);
        findings.AddRange(InstallationChecks.Check(file));
        return new Report(toolVersion, project?.File, findings, identity, file);
    }

    /// <summary>What doctor found: its findings in order, and the identity it read with its file.</summary>
    public sealed record Report(string ToolVersion, string? ProjectFile, IReadOnlyList<Finding> Findings, BuildIdentity? Identity, string? IdentityFile)
    {
        public int Errors => Findings.Count(f => f.Severity == Severity.Error);

        public int Warnings => Findings.Count(f => f.Severity == Severity.Warning);

        public int ExitCode => Errors > 0 ? ExitCodes.Errors : Warnings > 0 ? ExitCodes.Warnings : ExitCodes.Success;
    }
}
