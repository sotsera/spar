namespace Spar.WindowTests;

/// <summary>Where window tests can run: Windows until the Linux host arrives (M1c). Elsewhere each is an explicit skip.</summary>
internal static class WindowSupport
{
    public static void SkipUnlessSupported() =>
        Assert.SkipUnless(OperatingSystem.IsWindows(), "The Window surface runs on Windows; the Linux host arrives in M1c.");
}

/// <summary>Bounded waits for state the page API does not see, such as the backend's or the log's.</summary>
internal static class Waits
{
    public static async Task UntilAsync(Func<bool> condition, TimeSpan limit, string? what = null)
    {
        var deadline = DateTime.UtcNow + limit;
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline)
            {
                Assert.Fail($"Timed out after {limit.TotalSeconds:0.#} s waiting for {what ?? "the condition"}.");
            }

            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }
}

/// <summary>Folders of this repository the tests write to, under <c>artifacts/test-results/Spar.WindowTests/</c>.</summary>
internal static class Results
{
    private static readonly Lazy<string> Root = new(FindRoot);

    public static string Folder(string name)
    {
        var folder = Path.Combine(Root.Value, "artifacts", "test-results", "Spar.WindowTests", name);
        Directory.CreateDirectory(folder);
        return folder;
    }

    /// <summary>The repository's root, the folder holding <c>global.json</c> and <c>artifacts</c>, above the test's output.</summary>
    public static string RepositoryRoot => Root.Value;

    private static string FindRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "global.json")) && Directory.Exists(Path.Combine(directory.FullName, "artifacts")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("These tests run from a build inside the Spar repository.");
    }
}
