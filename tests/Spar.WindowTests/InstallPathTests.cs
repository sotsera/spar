using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace Spar.WindowTests;

/// <summary>
/// The required host test "an app installed under a long path" (plan section 5, adopted from the research review): this
/// test executable, with the app it carries, copied to a folder whose executable path is 255 characters long, just
/// under the 260 that Windows can start without long-path support, and run there on one window test. The window, its
/// page and its backend must work from there. A path over that limit cannot be started at all where long paths are
/// off (the default): that case is the installer's and the OS's, not the host's.
/// <para>
/// Every run keeps the child's output in this project's results (<c>install-path/</c>), and a failure's message names
/// that file and carries the output, so a failure in a full run can be read afterwards. A folder the child's processes
/// still hold is waited for, and a cleanup failure never hides the test's own.
/// </para>
/// </summary>
[Trait("Surface", "Window")]
public sealed class InstallPathTests
{
    /// <summary>The longest executable path Windows starts without long-path support, less a margin.</summary>
    private const int PathLength = 255;

    private const string ChildTest = "Spar.WindowTests.SmokeTests.The_window_opens_calls_the_backend_and_closes";

    /// <summary>The child's whole run: a new test process, its first window (the browser's start included) and its exit.</summary>
    private static readonly TimeSpan ChildLimit = TimeSpan.FromMinutes(2);

    /// <summary>How long the copy's folder may stay held after the child exits: its browser's processes end after it.</summary>
    private static readonly TimeSpan DeleteLimit = TimeSpan.FromSeconds(15);

    [Fact]
    public async Task The_window_works_from_an_install_folder_with_a_long_path()
    {
        WindowSupport.SkipUnlessWindows("Windows' MAX_PATH limit on the executable's path: Linux has no such limit, and the long-path case there is a black-box test (the AT-SPI step).");
        var executable = Environment.ProcessPath!;
        var name = Path.GetFileName(executable);
        var parent = Path.Combine(Results.Artifacts, "test-host", $"long-install-{Environment.ProcessId}");
        var padding = PathLength - (parent.Length + 1 + name.Length + 1);
        Assert.True(padding > 10, $"The repository's path is too long for this test: {parent}");
        var folder = Path.Combine(parent, new string('d', padding));
        var failed = true;
        try
        {
            Copy(folder);
            var copy = Path.Combine(folder, name);
            Assert.Equal(PathLength, copy.Length);

            var child = await RunAsync(copy, folder);
            var record = Save(child);

            var passed = child.Exit == 0 && child.Output.Contains("Total: 1, Errors: 0, Failed: 0", StringComparison.Ordinal);
            Assert.True(passed, $"The copy's run of {ChildTest} failed: {child.Summary}. Its output ({record}):{Environment.NewLine}{child.Output}");
            failed = false;
        }
        finally
        {
            await DeleteAsync(parent, failed);
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

    /// <summary>
    /// Runs the copy on <see cref="ChildTest"/>. A child that outlives <see cref="ChildLimit"/> is ended with its
    /// process tree and reported with what it wrote so far, rather than as a bare cancellation.
    /// </summary>
    private static async Task<ChildRun> RunAsync(string executable, string folder)
    {
        var start = new ProcessStartInfo(executable, ["-method", ChildTest])
        {
            WorkingDirectory = folder,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        var token = TestContext.Current.CancellationToken;
        var started = Stopwatch.GetTimestamp();
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync(token);
        var error = process.StandardError.ReadToEndAsync(token);
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(token);
        limit.CancelAfter(ChildLimit);
        var timedOut = false;
        try
        {
            await process.WaitForExitAsync(limit.Token);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            timedOut = true;
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(token);
        }

        var elapsed = Stopwatch.GetElapsedTime(started);
        var text = await output + await error;
        return new ChildRun(process.ExitCode, timedOut, elapsed, text);
    }

    /// <summary>Keeps the child's output in this project's results; returns the file's path.</summary>
    private static string Save(ChildRun child)
    {
        var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture);
        var file = Path.Combine(Results.Folder("install-path"), $"{stamp}-{Environment.ProcessId}.log");
        File.WriteAllText(file, $"{child.Summary}{Environment.NewLine}{child.Output}");
        Results.Append("install-path.txt", $"{(child.Exit == 0 ? "passed" : "FAILED")}: {child.Summary}; output in {Path.GetFileName(file)}");
        return file;
    }

    /// <summary>
    /// Deletes the copy, waiting while a process still holds a file or the folder in it. When it cannot, the test fails
    /// for it, unless it already failed for its own reason: then the cleanup's failure is only noted in the results.
    /// </summary>
    private static async Task DeleteAsync(string parent, bool testFailed)
    {
        var started = Stopwatch.GetTimestamp();
        var attempts = 0;
        while (true)
        {
            attempts++;
            try
            {
                if (Directory.Exists(parent))
                {
                    Directory.Delete(parent, recursive: true);
                }

                if (attempts > 1)
                {
                    Results.Append("install-path.txt", $"the copy was deleted at attempt {attempts}, {Stopwatch.GetElapsedTime(started).TotalMilliseconds:0} ms after the child's end");
                }

                return;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                if (Stopwatch.GetElapsedTime(started) < DeleteLimit)
                {
                    await Task.Delay(100, CancellationToken.None);
                    continue;
                }

                var message = $"The copy at {parent} could not be deleted within {DeleteLimit.TotalSeconds:0} s ({attempts} attempts): {exception.Message}";
                Results.Append("install-path.txt", message);
                if (!testFailed)
                {
                    Assert.Fail(message);
                }

                return;
            }
        }
    }

    private sealed record ChildRun(int Exit, bool TimedOut, TimeSpan Elapsed, string Output)
    {
        public string Summary => TimedOut
            ? $"ended after {ChildLimit.TotalMinutes:0} min without finishing"
            : $"exit code {Exit} after {Elapsed.TotalMilliseconds:0} ms";
    }
}
