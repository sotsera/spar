namespace Spar.Testing;

/// <summary>
/// Where a test run writes what it leaves behind: data roots and browser profiles (<see cref="Root"/>) and results
/// (<see cref="Results"/>). The runner of a run can name both through the environment, as Windows Sandbox's runner does
/// in its guest, which has no repository: tests never look for the repository's files to find them. Without the
/// variables, the repository's <c>artifacts/</c> above the test's output (the folder beside <c>global.json</c>), and
/// otherwise the system's temporary folder.
/// </summary>
internal static class TestArtifacts
{
    /// <summary>The folder for data roots and browser profiles: <c>test-host/</c> and <c>webview2-profiles/</c> go under it.</summary>
    public const string ArtifactsVariable = "SPAR_TEST_ARTIFACTS";

    /// <summary>The folder for results: each test project writes under <c>&lt;folder&gt;/&lt;project&gt;/</c>.</summary>
    public const string ResultsVariable = "SPAR_TEST_RESULTS";

    /// <summary>The artifacts folder of this run, or null outside a repository with no variable set.</summary>
    public static string? Root => FromVariable(ArtifactsVariable) ?? RepositoryArtifacts();

    /// <summary>The results folder of <paramref name="project"/>, created; under the temporary folder as a last resort.</summary>
    public static string Results(string project)
    {
        ArgumentException.ThrowIfNullOrEmpty(project);
        var root = FromVariable(ResultsVariable)
            ?? (RepositoryArtifacts() is { } artifacts ? Path.Combine(artifacts, "test-results") : null)
            ?? Path.Combine(Path.GetTempPath(), "spar-test-results");
        var folder = Path.Combine(root, project);
        Directory.CreateDirectory(folder);
        return folder;
    }

    /// <summary>A fully qualified folder named by <paramref name="name"/>; a relative or empty value is ignored.</summary>
    private static string? FromVariable(string name) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } value && Path.IsPathFullyQualified(value) ? value : null;

    private static string? RepositoryArtifacts()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var artifacts = Path.Combine(directory.FullName, "artifacts");
            if (File.Exists(Path.Combine(directory.FullName, "global.json")) && Directory.Exists(artifacts))
            {
                return artifacts;
            }
        }

        return null;
    }
}
