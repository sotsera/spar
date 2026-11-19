using System.Diagnostics;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using Spar.Logging;
using Spar.Windows;

namespace Spar.Tests;

/// <summary>
/// Windows single instance with its real lock file and named pipe, two claims in one process standing for two launches
/// (decision "Windows single instance: a lock file plus a pipe"): the hand-off, a primary that does not answer, a closing
/// one, and a lock nobody serves. Each test has its own data root under <c>artifacts/</c>.
/// </summary>
[SupportedOSPlatform("windows10.0.17763")]
public sealed class SingleInstanceTests : IDisposable
{
    private readonly string _root = Path.Combine(FindArtifacts(), "test-host", "single-instance", Guid.NewGuid().ToString("N")[..12]);
    private readonly HostLog _log = new();

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A lock still held by a failed test; artifacts/ is disposable.
        }
    }

    [Fact]
    public void A_later_launch_hands_its_arguments_to_the_first()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows single instance");
        var received = new List<Relaunch>();
        var first = Claim([]);
        using var primary = Assert.IsType<SingleInstance>(first.Instance);
        primary.Listen(relaunch =>
        {
            lock (received)
            {
                received.Add(relaunch);
            }

            return true;
        }, _log);

        var clock = Stopwatch.StartNew();
        var second = Claim(["--recipe", "nightly.sqlrecipe"]);
        clock.Stop();

        Assert.Null(second.Problem);
        Assert.Null(second.Instance);
        Assert.Equal((uint)Environment.ProcessId, second.ForwardedTo);
        var relaunch = Assert.Single(received);
        Assert.Equal(["--recipe", "nightly.sqlrecipe"], relaunch.Arguments);
        Assert.Equal(Environment.CurrentDirectory, relaunch.WorkingDirectory);
        Assert.True(clock.ElapsedMilliseconds < 1_000, $"the hand-off took {clock.ElapsedMilliseconds} ms");
    }

    [Fact]
    public void Ten_launches_in_a_row_all_arrive()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows single instance");
        var count = 0;
        using var primary = Claim([]).Instance!;
        primary.Listen(_ => Interlocked.Increment(ref count) > 0, _log);

        // Each launch is a process of its own, whose main thread blocks while the primary's pipe is served on the
        // primary's thread pool. Pool threads here would stand for neither: while they wait their turn, the primary's
        // loop has none left (on CI's four cores, 3 of 10 launches timed out).
        var results = new InstanceClaim[10];
        var launches = Enumerable.Range(0, results.Length).Select(index => new Thread(() =>
            results[index] = Claim([index.ToString(System.Globalization.CultureInfo.InvariantCulture)]))).ToList();
        launches.ForEach(launch => launch.Start());
        launches.ForEach(launch => launch.Join());

        Assert.All(results, result => Assert.True(result.ForwardedTo != 0, result.Problem?.Details));
        Assert.Equal(10, count);
    }

    [Fact]
    public void A_primary_that_does_not_answer_gives_not_responding_after_two_seconds()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows single instance");
        using var hung = new ManualResetEventSlim();
        using var primary = Claim([]).Instance!;
        primary.Listen(_ =>
        {
            // Stands for a primary whose process is suspended: nothing answers until the test ends.
            hung.Wait(TimeSpan.FromSeconds(10));
            return true;
        }, _log);

        var clock = Stopwatch.StartNew();
        var second = Claim(["x"]);
        clock.Stop();
        hung.Set();

        Assert.Equal(StartupProblemKind.NotResponding, second.Problem?.Kind);
        Assert.Equal(8, second.Problem!.ExitCode);
        Assert.InRange(clock.ElapsedMilliseconds, SingleInstance.AcknowledgeMilliseconds - 50, SingleInstance.AcknowledgeMilliseconds + 1_500);
    }

    [Fact]
    public void A_closing_primary_refuses_and_the_launch_says_so()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows single instance");
        using var primary = Claim([]).Instance!;
        primary.Listen(_ => false, _log);

        var second = Claim(["x"]);

        Assert.Equal(StartupProblemKind.NotResponding, second.Problem?.Kind);
        Assert.Contains("closing", second.Problem!.Details, StringComparison.Ordinal);
    }

    [Fact]
    public void A_lock_released_while_waiting_makes_the_launch_the_primary()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows single instance");
        var primary = Claim([]).Instance!;

        // No pipe: the primary is closing. It lets go of the lock 300 ms later.
        var token = TestContext.Current.CancellationToken;
        _ = Task.Run(
            async () =>
            {
                await Task.Delay(300, token);
                primary.Dispose();
            },
            token);
        var second = Claim(["x"]);

        using var next = Assert.IsType<SingleInstance>(second.Instance);
        Assert.Null(second.Problem);
    }

    [Fact]
    public void A_lock_nobody_serves_gives_not_responding_after_the_lock_wait()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows single instance");
        using var primary = Claim([]).Instance!;

        var clock = Stopwatch.StartNew();
        var second = Claim(["x"]);
        clock.Stop();

        Assert.Equal(StartupProblemKind.NotResponding, second.Problem?.Kind);
        Assert.InRange(clock.ElapsedMilliseconds, SingleInstance.LockWaitMilliseconds - 50, SingleInstance.LockWaitMilliseconds + 2_000);
    }

    [Fact]
    public void The_lock_goes_with_its_holder()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows single instance");
        Claim([]).Instance!.Dispose();

        using var again = Assert.IsType<SingleInstance>(Claim([]).Instance);
    }

    [Fact]
    public void A_junction_to_the_data_root_reaches_the_same_instance()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows single instance");
        var arrived = 0;
        using var primary = Claim([]).Instance!;
        primary.Listen(_ => Interlocked.Increment(ref arrived) > 0, _log);
        var junction = _root + "-junction";
        using (var mklink = Process.Start(new ProcessStartInfo("cmd.exe", ["/d", "/c", "mklink", "/J", junction, _root]) { UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true })!)
        {
            mklink.WaitForExit();
            Assert.Equal(0, mklink.ExitCode);
        }

        try
        {
            var second = SingleInstance.Claim(junction, new Relaunch(["x"], Environment.CurrentDirectory), _log);

            Assert.True(second.ForwardedTo != 0, second.Problem?.Details);
            Assert.Equal(1, arrived);
        }
        finally
        {
            Directory.Delete(junction);
        }
    }

    [Fact]
    public async Task Two_runs_appending_to_one_log_file_keep_every_line()
    {
        // A relaunch writes to app.log while the running instance does: two writers on one file, each with its own
        // handle, as two processes have. A FileStream in append mode lost about half the lines here.
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows log file");
        var logs = Path.Combine(_root, "logs");
        var first = LogFile.Open(logs);
        var second = LogFile.Open(logs);

        var writers = new[] { (Log: first, Name: "first"), (Log: second, Name: "second") }.Select(writer => Task.Run(async () =>
        {
            for (var i = 0; i < 2_000; i++)
            {
                writer.Log.Write(LogLevel.Information, "Test", $"{writer.Name} {i}");
                if (i % 50 == 0)
                {
                    await Task.Yield();
                }
            }
        }, TestContext.Current.CancellationToken));
        await Task.WhenAll(writers);
        first.Close();
        second.Close();

        var lines = File.ReadAllLines(Path.Combine(logs, LogFile.FileName));
        Assert.Equal(2_000, lines.Count(line => line.Contains(" Test  first ", StringComparison.Ordinal)));
        Assert.Equal(2_000, lines.Count(line => line.Contains(" Test  second ", StringComparison.Ordinal)));
        Assert.Equal(4_000, lines.Length);
    }

    [Fact]
    public void A_Windows_too_old_for_the_host_reports_OS_too_old()
    {
        // The stand-in the generated Run hands over below Windows 10 1809. The hook takes the message, so no box opens,
        // and the logs folder is this test's own.
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows only");
        var logs = Path.Combine(_root, "logs");
        StartupProblem? seen = null;
        var builder = new SparBuilder([], appId: "test");
        builder.Folders(f => f.Logs(logs)).OnStartupProblem(problem => (seen = problem) is not null);
        var app = new SparApp(builder, new Spar.Runtime.SparCatalog("test", [], _ => { }), session: null);

        var exit = new Spar.Runtime.UnsupportedWindowsHost().Run(app);

        Assert.Equal(7, exit);
        Assert.Equal((StartupProblemKind.OsTooOld, "Windows", "10.0.19045"), (seen!.Kind, seen.Component, seen.Required));
        Assert.Contains("Start-up problem OsTooOld", File.ReadAllText(Path.Combine(logs, LogFile.FileName)), StringComparison.Ordinal);
    }

    private InstanceClaim Claim(string[] arguments) =>
        SingleInstance.Claim(_root, new Relaunch(arguments, Environment.CurrentDirectory), _log);

    private static string FindArtifacts()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Spar.slnx")))
            {
                return Path.Combine(directory.FullName, "artifacts");
            }
        }

        return Path.Combine(Path.GetTempPath(), "spar-artifacts");
    }
}
