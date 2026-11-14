using System.Diagnostics;
using System.Reflection;
using System.Text.RegularExpressions;

namespace Loop.Tests;

/// <summary>Starts the real app executable in loopback mode and reads its ready line, as an agent or CI would.</summary>
public sealed partial class LoopbackApp : IAsyncDisposable
{
    private readonly Process _process;
    private readonly List<string> _output;

    private LoopbackApp(Process process, List<string> output, string url, string build, TimeSpan readyAfter)
    {
        _process = process;
        _output = output;
        Url = url;
        Build = build;
        ReadyAfter = readyAfter;
    }

    public string Url { get; }

    public string Build { get; }

    public TimeSpan ReadyAfter { get; }

    public IReadOnlyList<string> Output
    {
        get
        {
            lock (_output)
            {
                return [.. _output];
            }
        }
    }

    public string BaseAddress => Url[..Url.IndexOf('#')].TrimEnd('/');

    public static async Task<LoopbackApp> StartAsync(CancellationToken cancellationToken, params string[] extraArgs)
    {
        // Stored relative to the test output folder (see AppPathMetadata.targets), so clones and moved repos still work.
        var relative = typeof(LoopbackApp).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().Single(a => a.Key == "LoopAppPath").Value!;
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, relative));
        var startInfo = new ProcessStartInfo(path, ["--loopback", "--exit-with-stdin", .. extraArgs])
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        // .NET 11: the OS ends the app if the test process dies. Not available on macOS.
        if (OperatingSystem.IsWindows() || OperatingSystem.IsLinux())
        {
            startInfo.KillOnParentExit = true;
        }

        var stopwatch = Stopwatch.StartNew();
        var process = Process.Start(startInfo)!;
        var output = new List<string>();
        var ready = new TaskCompletionSource<Match>(TaskCreationOptions.RunContinuationsAsynchronously);
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null)
            {
                ready.TrySetException(new InvalidOperationException("The app exited before it was ready."));
                return;
            }

            lock (output)
            {
                output.Add(e.Data);
            }

            if (ReadyLine().Match(e.Data) is { Success: true } match)
            {
                ready.TrySetResult(match);
            }
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                lock (output)
                {
                    output.Add("stderr: " + e.Data);
                }
            }
        };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        var readyLine = await ready.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
        return new LoopbackApp(process, output, readyLine.Groups["url"].Value, readyLine.Groups["build"].Value, stopwatch.Elapsed);
    }

    public async Task<string> WaitForOutputAsync(string prefix, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            if (Output.FirstOrDefault(l => l.StartsWith(prefix, StringComparison.Ordinal)) is { } line)
            {
                return line;
            }

            await Task.Delay(25, cancellationToken);
        }

        throw new TimeoutException($"No '{prefix}' line. Output:\n{string.Join('\n', Output)}");
    }

    /// <summary>Ends the app abruptly, like a crash or a development restart.</summary>
    public async Task KillAsync()
    {
        _process.Kill(entireProcessTree: true);
        await _process.WaitForExitAsync();
        _process.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        if (!IsRunning())
        {
            return;
        }

        // Closing stdin is the app's "your tool is gone" signal; it must exit cleanly on its own.
        _process.StandardInput.Close();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            await _process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            _process.Kill(entireProcessTree: true);
            throw new InvalidOperationException("The app did not exit after its stdin closed.");
        }
        finally
        {
            _process.Dispose();
        }
    }

    private bool IsRunning()
    {
        try
        {
            return !_process.HasExited;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    [GeneratedRegex(@"^SPAR_READY url=(?<url>\S+) build=(?<build>\S+)")]
    private static partial Regex ReadyLine();
}
