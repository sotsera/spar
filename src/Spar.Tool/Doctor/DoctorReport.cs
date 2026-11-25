using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Spar.Tool.Doctor;

/// <summary>
/// Doctor's two outputs: text for people (by default what needs attention, its fix below it; with <c>--verbose</c> every
/// finding by area) and, with <c>--json</c>, every finding in one JSON object for agents and scripts, written with
/// <see cref="Utf8JsonWriter"/> (no reflection):
/// <c>{"tool":…,"version":…,"project":…,"findings":[{"id","area","severity","message","fix"}…],"identity":{…}|null,
/// "errors":N,"warnings":N,"exitCode":N}</c>.
/// </summary>
internal static class DoctorReport
{
    /// <summary>
    /// The text report. By default only what needs attention, under its area, with the project doctor looked at (or why
    /// it found none) and a count of the checks that passed; <paramref name="verbose"/> lists every check, as JSON does.
    /// </summary>
    public static void WriteText(TextWriter output, DoctorCommand.Report report, bool verbose)
    {
        output.WriteLine($"spar doctor {report.ToolVersion}");
        if (!verbose && report.Findings.FirstOrDefault(f => f.Id == FindingIds.Project && f.Severity == Severity.Info) is { } project)
        {
            output.WriteLine($"Project: {project.Message}");
        }

        foreach (var area in new[] { Area.Machine, Area.Project, Area.Identity })
        {
            var findings = report.Findings.Where(f => f.Area == area && (verbose || f.Severity != Severity.Info)).ToList();
            if (findings.Count == 0)
            {
                continue;
            }

            output.WriteLine();
            output.WriteLine(area switch
            {
                Area.Machine => "Machine",
                Area.Project => "Project",
                _ => "Build identity",
            });
            foreach (var finding in findings)
            {
                WriteFinding(output, finding);
            }
        }

        output.WriteLine();
        output.WriteLine(Summary(report, verbose));
    }

    public static void WriteJson(TextWriter output, DoctorCommand.Report report)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            writer.WriteStartObject();
            writer.WriteString("tool", "spar");
            writer.WriteString("version", report.ToolVersion);
            writer.WriteString("project", report.ProjectFile);
            writer.WriteStartArray("findings");
            foreach (var finding in report.Findings)
            {
                writer.WriteStartObject();
                writer.WriteString("id", finding.Id);
                writer.WriteString("area", Name(finding.Area));
                writer.WriteString("severity", Name(finding.Severity));
                writer.WriteString("message", finding.Message);
                writer.WriteString("fix", finding.Fix);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            if (report.Identity is { } identity)
            {
                writer.WriteStartObject("identity");
                writer.WriteString("file", report.IdentityFile);
                writer.WriteString("variant", identity.Variant);
                writer.WriteString("revision", identity.Revision);
                if (identity.Dirty is { } dirty)
                {
                    writer.WriteBoolean("dirty", dirty);
                }
                else
                {
                    writer.WriteNull("dirty");
                }

                writer.WriteString("fingerprint", identity.Fingerprint);
                writer.WriteEndObject();
            }
            else
            {
                writer.WriteNull("identity");
            }

            writer.WriteNumber("errors", report.Errors);
            writer.WriteNumber("warnings", report.Warnings);
            writer.WriteNumber("exitCode", report.ExitCode);
            writer.WriteEndObject();
        }

        output.WriteLine(Encoding.UTF8.GetString(stream.ToArray()));
    }

    public static string Name(Area area) => area switch
    {
        Area.Machine => "machine",
        Area.Project => "project",
        _ => "identity",
    };

    public static string Name(Severity severity) => severity switch
    {
        Severity.Error => "error",
        Severity.Warning => "warning",
        _ => "info",
    };

    private static void WriteFinding(TextWriter output, Finding finding)
    {
        var label = finding.Severity switch
        {
            Severity.Error => "error",
            Severity.Warning => "warn ",
            _ => "info ",
        };
        output.WriteLine($"  {label} {finding.Id,-26} {finding.Message}");
        if (finding.Fix is not { } fix)
        {
            return;
        }

        var lines = fix.Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            output.WriteLine($"        {(index == 0 ? "fix: " : "     ")}{lines[index]}");
        }
    }

    /// <summary>The last line: what to fix, and, without <c>--verbose</c>, how many checks passed and how to see them.</summary>
    private static string Summary(DoctorCommand.Report report, bool verbose)
    {
        var passed = report.Findings.Count(f => f.Severity == Severity.Info);
        var problems = report.Errors + report.Warnings == 0
            ? "Nothing to fix"
            : $"{Count(report.Errors, "error")}, {Count(report.Warnings, "warning")}";
        return verbose
            ? $"{problems}."
            : $"{problems}; {Count(passed, "check")} passed (spar doctor --verbose lists them).";
    }

    private static string Count(int count, string noun) => count == 1 ? $"1 {noun}" : $"{count} {noun}s";
}
