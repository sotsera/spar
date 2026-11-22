using System.Text.RegularExpressions;
using Spar.Runtime;
using Spar.Tool.Identity;
using Spar.Tool.Project;

namespace Spar.Tool.Doctor;

/// <summary>
/// The build's identity, read from an app's file without running it (<see cref="BuildIdentityReader"/>): the one
/// <c>--app</c> names, or the newest build output of the project under its <c>bin/</c>; compared with the contracts of
/// the project's generated client, and a Production build from a tree with uncommitted changes pointed out.
/// </summary>
internal static partial class IdentityChecks
{
    /// <summary>The findings, and the identity with the file it came from when one was read.</summary>
    public static (IReadOnlyList<Finding> Findings, BuildIdentity? Identity, string? File) Check(string? appFile, AppProject? project)
    {
        var findings = new List<Finding>();
        string? file;
        BuildIdentity? identity;
        if (appFile is not null)
        {
            (identity, file, var problem) = BuildIdentityReader.Read(appFile);
            if (identity is null)
            {
                var exists = System.IO.File.Exists(Path.GetFullPath(appFile));
                findings.Add(new Finding(FindingIds.Identity, Area.Identity, exists ? Severity.Warning : Severity.Error, problem!,
                    exists ? "Build it again with this Spar.Sdk; doctor reads the identity of an app it built." : "Name an app's executable or DLL with --app."));
                return (findings, null, null);
            }
        }
        else if (project is not null)
        {
            (identity, file) = Newest(project);
            if (identity is null)
            {
                findings.Add(Finding.Info(FindingIds.Identity, Area.Identity, $"{project.Name} has no build output with an identity under bin/ yet; build it, or name a file with --app."));
                return (findings, null, null);
            }
        }
        else
        {
            return (findings, null, null);
        }

        findings.Add(Finding.Info(FindingIds.Identity, Area.Identity, $"{file}: {Capitalized(identity.Describe())}."));
        if (project is not null && ClientFingerprint(project) is { } client && client != identity.Fingerprint)
        {
            findings.Add(Finding.Warning(FindingIds.IdentityContracts, Area.Identity,
                $"The app was built with contract fingerprint {identity.Fingerprint}, and the project's generated client has {client}: its page and this build disagree, and every call fails with contract-mismatch.",
                "Build the app again, so the executable and the page come from the same contracts."));
        }

        if (identity.Variant == "Production" && identity.Dirty == true)
        {
            findings.Add(Finding.Warning(FindingIds.IdentityDirty, Area.Identity,
                $"This Production build came from a work tree with uncommitted changes, so revision {identity.Revision} alone does not say what is in it.",
                "Commit the changes and build it again before you ship it."));
        }

        return (findings, identity, file);
    }

    /// <summary>The newest file under the project's <c>bin/</c> named after its assembly that holds an identity.</summary>
    internal static (BuildIdentity? Identity, string? File) Newest(AppProject project)
    {
        var bin = Path.Combine(project.Directory, "bin");
        if (!Directory.Exists(bin))
        {
            return (null, null);
        }

        string[] names = [project.AssemblyName + ".exe", project.AssemblyName + ".dll", project.AssemblyName];
        var candidates = Directory.EnumerateFiles(bin, "*", SearchOption.AllDirectories)
            .Where(path => names.Contains(Path.GetFileName(path), StringComparer.OrdinalIgnoreCase))
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.LastWriteTimeUtc);
        foreach (var candidate in candidates)
        {
            var (identity, file, _) = BuildIdentityReader.Read(candidate.FullName);
            if (identity is not null)
            {
                return (identity, file);
            }
        }

        return (null, null);
    }

    /// <summary>The fingerprint the project's generated client says (<c>obj/spar/client/spar.js</c>), or null without one.</summary>
    internal static string? ClientFingerprint(AppProject project)
    {
        var path = Path.Combine(project.ClientDirectory, "spar.js");
        if (!System.IO.File.Exists(path))
        {
            return null;
        }

        var match = FingerprintExport().Match(System.IO.File.ReadAllText(path));
        return match.Success ? match.Groups["value"].Value : null;
    }

    private static string Capitalized(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

    [GeneratedRegex(@"export const fingerprint = ""(?<value>[0-9a-f]+)""")]
    private static partial Regex FingerprintExport();
}
