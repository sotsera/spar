using AwesomeAssertions;

namespace Sotsera.Spar.Development.Tests;

public sealed class WatcherProcessTests
{
    [Theory(Timeout = 30000)]
    [InlineData("stdout")]
    [InlineData("stderr")]
    public async Task ReaderFailureRetainsStartedProcessUntilActualRetirement(string output)
    {
        var directory = Path.Combine(Path.GetTempPath(), "spar-watcher-owner-" + Guid.NewGuid().ToString("N"));
        var project = CreateProject(directory);
        using var parent = new OwnedProcessJob();
        using var watcher = new WatcherProcess(parent);
        try
        {
            watcher.Start(project, directory, 1, new Dictionary<string, string>());
            var process = watcher.Process!;
            var handle = process.Handle;
            parent.Contains(process).Should().BeTrue();
            process.HasExited.Should().BeFalse();

            // Existing writers remain valid; future readers are denied deterministically.
            using (var blocked = new FileStream(Path.Combine(directory, "watcher-1." + output + ".log"),
                FileMode.Open, FileAccess.Write, FileShare.Write))
            {
                var open = () => watcher.OpenOutput(directory, 1);
                open.Should().Throw<IOException>();
            }

            watcher.Process.Should().BeSameAs(process);
            process.Handle.Should().Be(handle);
            await watcher.RetireAsync(null).WaitAsync(TestContext.Current.CancellationToken);
            watcher.Exited!.IsCompletedSuccessfully.Should().BeTrue();
            process.HasExited.Should().BeTrue();
            parent.ActiveProcessIds().Should().BeEmpty();
        }
        finally
        {
            await watcher.RetireAsync(null);
        }
    }

    [Fact(Timeout = 30000)]
    public async Task RetirementRejectsUnfinishedApplicationAndKeepsWatcherOwned()
    {
        var directory = Path.Combine(Path.GetTempPath(), "spar-watcher-barrier-" + Guid.NewGuid().ToString("N"));
        var project = CreateProject(directory);
        using var parent = new OwnedProcessJob();
        using var watcher = new WatcherProcess(parent);
        var applicationExited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            watcher.Start(project, directory, 1, new Dictionary<string, string>());
            var process = watcher.Process!;
            var retire = () => watcher.RetireAsync(applicationExited.Task);
            await retire.Should().ThrowAsync<InvalidOperationException>();
            parent.Contains(process).Should().BeTrue();
            process.HasExited.Should().BeFalse();
            applicationExited.SetResult();
            await watcher.RetireAsync(applicationExited.Task).WaitAsync(TestContext.Current.CancellationToken);
            process.HasExited.Should().BeTrue();
            parent.ActiveProcessIds().Should().BeEmpty();
        }
        finally
        {
            await watcher.RetireAsync(null);
        }
    }

    private static string CreateProject(string directory)
    {
        Directory.CreateDirectory(directory);
        var project = Path.Combine(directory, "WatcherFixture.csproj");
        File.WriteAllText(project,
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework>"
            + "<OutputType>Exe</OutputType></PropertyGroup></Project>");
        File.WriteAllText(Path.Combine(directory, "Program.cs"), "System.Threading.Thread.Sleep(-1);");
        return project;
    }
}
