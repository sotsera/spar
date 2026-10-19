using System.Diagnostics;
using System.Text;
using AwesomeAssertions;
using Sotsera.Spar.Development;
using Sotsera.Spar.Windows;

namespace Sotsera.Spar.Development.Tests;

public sealed class DevelopmentTests
{
    [Fact]
    public void ConfigurationPreservesOmittedDefaults()
    {
        var directory = Path.Combine(Path.GetTempPath(), "spar-settings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, "spar.dev.json");
        File.WriteAllText(file, """{"project":"sample.csproj","application":"sample.exe","frontend":{"port":0}}""");
        var settings = DevelopmentSettings.Load(file);
        settings.StartupTimeoutSeconds.Should().Be(30);
        settings.ShutdownTimeoutSeconds.Should().Be(10);
        settings.Frontend.FileName.Should().Be("cmd.exe");
        settings.Frontend.Arguments.Should().Contain("npm");
        settings.Frontend.Environment.Should().BeEmpty();
        settings.Frontend.WorkingDirectory.Should().Be(Path.Combine(directory, "frontend"));
    }

    [Theory]
    [InlineData("http://example.com/private/")]
    [InlineData("http://localhost/private/")]
    [InlineData("http://127.0.0.1/")]
    [InlineData("http://127.0.0.1/private/?redirect=elsewhere")]
    [InlineData("https://127.0.0.1/private/")]
    public void LoopbackContentRejectsUnscopedOrAmbiguousAuthority(string url)
    {
        var create = () => new SparLoopbackContent(new Uri(url), new string('a', 64));
        create.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task PrivateInputFramesAreBoundedBeforeParsing()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(new string('x', BoundedLineReader.MaximumBytes + 1) + "\n"));
        var reader = new BoundedLineReader(stream);
        var read = () => reader.ReadAsync(TestContext.Current.CancellationToken);
        await read.Should().ThrowAsync<InvalidDataException>();
    }

    [Fact(Timeout = 30000)]
    public async Task CreatedChildBelongsToJobAndJobCloseTerminatesOnlyOwnedWork()
    {
        var directory = Path.Combine(Path.GetTempPath(), "spar-job-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        using var unrelated = Process.Start(new ProcessStartInfo("node")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            ArgumentList = { "-e", "setInterval(() => {}, 1000)" }
        })!;
        _ = unrelated.Handle;
        Process? child = null;
        var job = new OwnedProcessJob();
        try
        {
            child = job.Start("node", ["-e", "console.log(process.env.SPAR_TEST_MARKER); setInterval(() => {}, 1000)"],
                directory, new Dictionary<string, string> { ["SPAR_TEST_MARKER"] = "space and quote: \" ok" },
                Path.Combine(directory, "stdout.log"), Path.Combine(directory, "stderr.log"));
            job.Contains(child).Should().BeTrue();
            job.Contains(unrelated).Should().BeFalse();
            job.ActiveProcessIds().Should().Contain(child.Id);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            while (!ReadShared(Path.Combine(directory, "stdout.log")).Contains("space and quote: \" ok", StringComparison.Ordinal))
            {
                await Task.Delay(20, timeout.Token);
            }

            job.Dispose();
            await child.WaitForExitAsync(timeout.Token);
            unrelated.HasExited.Should().BeFalse();
        }
        finally
        {
            job.Dispose();
            if (child is not null)
            {
                await child.WaitForExitAsync(TestContext.Current.CancellationToken);
                child.Dispose();
            }
            if (!unrelated.HasExited)
            {
                unrelated.Kill();
                await unrelated.WaitForExitAsync(TestContext.Current.CancellationToken);
            }
        }
    }

    private static string ReadShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
