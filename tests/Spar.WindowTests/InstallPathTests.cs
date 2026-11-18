using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Spar.WindowTests;

/// <summary>
/// The required host test "an app installed under a long path" (plan section 5, adopted from the research review): this
/// test executable, with the app it carries, copied to a folder whose executable path is 255 characters long, just
/// under the 260 that Windows can start without long-path support, and run there on one window test. The window, its
/// page and its backend must work from there. A path over that limit cannot be started at all where long paths are
/// off (the default): that case is the installer's and the OS's, not the host's.
/// </summary>
[Trait("Surface", "Window")]
public sealed class InstallPathTests
{
    /// <summary>The longest executable path Windows starts without long-path support, less a margin.</summary>
    private const int PathLength = 255;

    private const string ChildTest = "Spar.WindowTests.SmokeTests.The_window_opens_calls_the_backend_and_closes";

    [Fact]
    public async Task The_window_works_from_an_install_folder_with_a_long_path()
    {
        WindowSupport.SkipUnlessSupported();
        var executable = Environment.ProcessPath!;
        var name = Path.GetFileName(executable);
        var parent = Path.Combine(Results.RepositoryRoot, "artifacts", "test-host", $"long-install-{Environment.ProcessId}");
        var padding = PathLength - (parent.Length + 1 + name.Length + 1);
        Assert.True(padding > 10, $"The repository's path is too long for this test: {parent}");
        var folder = Path.Combine(parent, new string('d', padding));
        try
        {
            Copy(folder);
            var copy = Path.Combine(folder, name);
            Assert.Equal(PathLength, copy.Length);

            var (exit, output) = await RunAsync(copy, folder);

            Assert.True(exit == 0, output);
            Assert.Contains("Total: 1, Errors: 0, Failed: 0", output, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(parent, recursive: true);
        }
    }

    /// <summary>The executable alone under NativeAOT; under CoreCLR the whole output folder it loads from.</summary>
    private static void Copy(string folder)
    {
        Directory.CreateDirectory(folder);
        if (!RuntimeFeature.IsDynamicCodeCompiled)
        {
            File.Copy(Environment.ProcessPath!, Path.Combine(folder, Path.GetFileName(Environment.ProcessPath!)));
            return;
        }

        var source = AppContext.BaseDirectory;
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(folder, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }

    private static async Task<(int Exit, string Output)> RunAsync(string executable, string folder)
    {
        var start = new ProcessStartInfo(executable, ["-method", ChildTest])
        {
            WorkingDirectory = folder,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        var error = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        limit.CancelAfter(TimeSpan.FromMinutes(2));
        try
        {
            await process.WaitForExitAsync(limit.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw;
        }

        return (process.ExitCode, await output + await error);
    }
}
