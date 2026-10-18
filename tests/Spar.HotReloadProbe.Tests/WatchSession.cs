using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Channels;

namespace Sotsera.Spar.HotReloadProbe.Tests;

internal sealed class WatchSession : IAsyncDisposable
{
    private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan _phaseTimeout = TimeSpan.FromSeconds(75);
    private readonly object _logGate = new();
    private readonly StreamWriter _log;
    private readonly Process _watcher;
    private readonly Task _stdout;
    private readonly Task _stderr;
    private readonly Dictionary<(int ProcessId, DateTime StartedAtUtc), Process> _applications = [];
    private readonly TaskCompletionSource<DateTimeOffset> _compilationError = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Channel<ProbeSample> _samples = Channel.CreateBounded<ProbeSample>(
        new BoundedChannelOptions(128)
        {
            FullMode = BoundedChannelFullMode.DropOldest
        });

    public string DirectoryPath
    {
        get;
    }

    public WatchSession(string scenario)
    {
        var repository = FindRepository();
        DirectoryPath = Path.Combine(repository, "artifacts", "hot-reload", scenario + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(DirectoryPath);

        foreach (var source in Directory.EnumerateFiles(Path.Combine(AppContext.BaseDirectory, "Fixture")))
        {
            File.Copy(source, Path.Combine(DirectoryPath, Path.GetFileName(source)));
        }

        _log = new StreamWriter(Path.Combine(DirectoryPath, "watch.log")) { AutoFlush = true };
        var hashes = Directory.EnumerateFiles(DirectoryPath)
            .Where(path => Path.GetExtension(path) is ".cs" or ".csproj")
            .Order(StringComparer.Ordinal)
            .Select(path => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))) + "  " + Path.GetFileName(path));
        File.WriteAllLines(Path.Combine(DirectoryPath, "sources.sha256"), hashes);
        var dotnet = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet", "dotnet.exe");
        var start = new ProcessStartInfo(dotnet)
        {
            WorkingDirectory = DirectoryPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        foreach (var argument in new[]
        {
            "watch", "--project", "Spar.HotReloadProbe.csproj", "--non-interactive", "run",
            "--no-launch-profile", "--disable-build-servers", "--property:UseSharedCompilation=false"
        })
        {
            start.ArgumentList.Add(argument);
        }

        // Auto-restart is confined to the disposable fixture; it is not Spar's production retirement policy.
        start.Environment["DOTNET_WATCH_RESTART_ON_RUDE_EDIT"] = "1";
        start.Environment["DOTNET_WATCH_SUPPRESS_BROWSER_REFRESH"] = "1";
        start.Environment["DOTNET_WATCH_SUPPRESS_LAUNCH_BROWSER"] = "1";
        start.Environment["DOTNET_WATCH_SUPPRESS_EMOJIS"] = "1";
        start.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        try
        {
            _watcher = Process.Start(start) ?? throw new InvalidOperationException("Could not start dotnet watch.");
        }
        catch
        {
            _log.Dispose();
            throw;
        }
        _stdout = PumpAsync(_watcher.StandardOutput, "stdout");
        _stderr = PumpAsync(_watcher.StandardError, "stderr");
    }

    public async Task<ProbeSample> WaitForSampleAsync(Func<ProbeSample, bool> predicate, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_phaseTimeout);

        try
        {
            while (true)
            {
                var sample = await _samples.Reader.ReadAsync(timeout.Token).ConfigureAwait(false);

                if (predicate(sample))
                {
                    return sample;
                }
            }
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"Expected probe behavior was not observed. See {DirectoryPath}/watch.log", exception);
        }
    }

    public Task<DateTimeOffset> WaitForCompilationErrorAsync(CancellationToken cancellationToken)
    {
        return _compilationError.Task.WaitAsync(_phaseTimeout, cancellationToken);
    }

    public async Task ReplaceAsync(string fileName, string oldText, string newText, CancellationToken cancellationToken)
    {
        var path = Path.Combine(DirectoryPath, fileName);
        var source = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);

        if (!source.Contains(oldText, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Fixture replacement was not found in {fileName}: {oldText}");
        }

        await File.WriteAllTextAsync(path, source.Replace(oldText, newText, StringComparison.Ordinal), cancellationToken)
            .ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (!_watcher.HasExited)
            {
                _watcher.Kill(entireProcessTree: true);
            }

            await _watcher.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false);
            await Task.WhenAll(_stdout, _stderr).WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false);

            foreach (var application in _applications.Values)
            {
                await application.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false);
            }

            WriteLog("cleanup", "Owned watcher and all observed application processes exited (forced fixture teardown).");
        }
        finally
        {
            foreach (var application in _applications.Values)
            {
                application.Dispose();
            }

            _watcher.Dispose();
            await _log.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static string FindRepository()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Spar.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Run this probe test from the Spar checkout.");
    }

    private async Task PumpAsync(StreamReader reader, string stream)
    {
        while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line)
        {
            WriteLog(stream, line);

            if (line.Contains("error CS", StringComparison.Ordinal))
            {
                _compilationError.TrySetResult(DateTimeOffset.UtcNow);
            }

            if (!line.StartsWith('{'))
            {
                continue;
            }

            var sample = JsonSerializer.Deserialize<ProbeSample>(line, _jsonOptions);

            if (sample is null)
            {
                continue;
            }

            var identity = (sample.ProcessId, sample.ProcessStartedAtUtc);

            if (!_applications.ContainsKey(identity))
            {
                var application = Process.GetProcessById(sample.ProcessId);

                if (application.StartTime.ToUniversalTime() != sample.ProcessStartedAtUtc)
                {
                    application.Dispose();
                    throw new InvalidOperationException("The observed process identity changed before tracking.");
                }

                _applications.Add(identity, application);
            }

            _samples.Writer.TryWrite(sample);
        }
    }

    private void WriteLog(string stream, string message)
    {
        lock (_logGate)
        {
            _log.WriteLine($"{DateTimeOffset.UtcNow:O} [{stream}] {message}");
        }
    }
}

internal sealed record ProbeSample(
    DateTimeOffset ObservedAt,
    int ProcessId,
    DateTime ProcessStartedAtUtc,
    Guid InstanceId,
    int Counter,
    string Greeting,
    UpdateSnapshot Update);

internal sealed record UpdateSnapshot(long Revision, int ClearCacheCount, int CallbackThreadId, string[] UpdatedTypes);
