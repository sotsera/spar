using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using Spar.Hosting;

namespace Spar.DevServer;

/// <summary>What one attempt to reach the development server came to.</summary>
/// <param name="Address">Where the server answers; null when no address is known.</param>
/// <param name="Problem">Why the page cannot come from it; null when it is ready.</param>
/// <param name="External">Whether Spar did not start it (<c>SPAR_DEV_SERVER_URL</c>, <c>SparDevServerUrl</c>).</param>
/// <param name="Output">The server's last lines when it failed, for the error page.</param>
internal sealed record DevServerOutcome(DevServerAddress? Address, string? Problem, bool External, IReadOnlyList<string> Output)
{
    public bool IsReady => Address is not null && Problem is null;
}

/// <summary>
/// The development server of one run of a Debug build (plan section 4): found, reused or started at the app's start,
/// beside the services and the window, and watched until the run ends. Its first attempt starts at once; a request
/// for the page waits for it, never on a UI thread. In order:
/// <list type="number">
/// <item>An address given in <c>SPAR_DEV_SERVER_URL</c> or <c>SparDevServerUrl</c>: used, and nothing is started.</item>
/// <item>The server <c>obj/spar-dev-server.json</c> records, when it answers with its instance: reused, so a backend
/// restart keeps it and the page's open HMR connection reconnects to it.</item>
/// <item>Otherwise a recorded server that does not answer is ended as a whole tree, and <c>npm run dev</c> starts a new
/// one on a free port, ready once the plugin has recorded its address and it answers.</item>
/// </list>
/// A failure is an outcome, not an exception: the window shows the error page with the server's last output, the app
/// keeps running, and <see cref="RetryAsync"/> tries again. A server that was ready and then ends raises
/// <see cref="Stopped"/>, so the host reloads the page onto the error page. The run never ends the server at its exit:
/// the next run reuses it, and the plugin's watchdog ends it once nothing has used it for a minute.
/// </summary>
internal sealed class DevServerSession : IDisposable
{
    /// <summary>The category of the server's own output (decision "M1d step 1's shape").</summary>
    public const string Category = "Spar.DevServer";

    /// <summary>How long a server Spar started runs with no request and no HMR connection before the plugin ends it.</summary>
    public const int IdleSeconds = 60;

    /// <summary>The plugin's identity endpoint: it answers with the instance Spar gave the server.</summary>
    public const string IdentityPath = "/__spar/dev-server";

    /// <summary>How long a new server has to record its address and answer.</summary>
    private const int StartLimitMilliseconds = 60_000;

    private readonly DevServerSettings _settings;
    private readonly HostLog _log;
    private readonly DevServerOutput _output;
    private readonly CancellationTokenSource _ending = new();
    private readonly Lock _gate = new();
    private Task<DevServerOutcome> _current;
    private Process? _launcher;
    private DevServerState? _state;
    private int _generation;

    private DevServerSession(DevServerSettings settings, HostLog log, bool console, HttpMessageHandler? handler)
    {
        _settings = settings;
        _log = log;
        _output = new DevServerOutput(log.For(Category), console);
        handler ??= new SocketsHttpHandler
        {
            UseProxy = false,
            AutomaticDecompression = DecompressionMethods.None,
            AllowAutoRedirect = false,
            UseCookies = false,
            ConnectTimeout = TimeSpan.FromSeconds(5),
        };
        Client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        _current = Task.Run(() => AttemptAsync(0, _ending.Token));
    }

    /// <summary>Raised off every UI thread when a server that was ready ends.</summary>
    public event Action? Stopped;

    /// <summary>The settings the build and the environment gave.</summary>
    public DevServerSettings Settings => _settings;

    /// <summary>The client the proxy fetches with: no proxy, no decompression, no redirects, no cookies.</summary>
    public HttpClient Client { get; }

    /// <summary>The current attempt; it never faults.</summary>
    public Task<DevServerOutcome> Current
    {
        get
        {
            lock (_gate)
            {
                return _current;
            }
        }
    }

    /// <summary>Starts the run's session; <paramref name="console"/> echoes the server's output to standard output.</summary>
    public static DevServerSession Start(DevServerSettings settings, HostLog log, bool console = true) => new(settings, log, console, null);

    /// <summary>A session whose fetches go to <paramref name="handler"/> (Spar's tests of the proxy's rules).</summary>
    internal static DevServerSession Start(DevServerSettings settings, HostLog log, HttpMessageHandler handler) =>
        new(settings, log, console: false, handler);

    /// <summary>
    /// Tries again (the error page's Retry): the attempt under way when one is, otherwise a new one that reuses a
    /// server that answers and starts a new one otherwise.
    /// </summary>
    public Task<DevServerOutcome> RetryAsync()
    {
        lock (_gate)
        {
            if (!_current.IsCompleted)
            {
                return _current;
            }

            var generation = ++_generation;
            _current = Task.Run(() => AttemptAsync(generation, _ending.Token));
            return _current;
        }
    }

    /// <summary>
    /// A request found the ready server <paramref name="seen"/> unreachable: the session fails with
    /// <paramref name="problem"/>, unless another attempt has already replaced it.
    /// </summary>
    public DevServerOutcome Unreachable(DevServerOutcome seen, string problem)
    {
        var failed = Failed(problem, seen.External, seen.Address);
        lock (_gate)
        {
            if (_current.IsCompletedSuccessfully && ReferenceEquals(_current.Result, seen))
            {
                _current = Task.FromResult(failed);
                return failed;
            }

            return _current.IsCompletedSuccessfully ? _current.Result : failed;
        }
    }

    /// <summary>Stops watching; the server keeps running for the next run.</summary>
    public void Dispose()
    {
        _ending.Cancel();
        _output.Dispose();
        _launcher?.Dispose();
        Client.Dispose();
    }

    private async Task<DevServerOutcome> AttemptAsync(int generation, CancellationToken token)
    {
        try
        {
            if (_settings.Url is { } url)
            {
                return External(url);
            }

            if (await ReuseAsync(generation, token).ConfigureAwait(false) is { } reused)
            {
                return reused;
            }

            return await StartAsync(generation, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return Failed("The app is ending.", external: false, address: null);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or Win32Exception or InvalidOperationException)
        {
            _log.Error("The development server could not be started", exception);
            return Failed($"The development server could not be started: {exception.Message}", external: false, address: null);
        }
    }

    private DevServerOutcome External(string url)
    {
        if (DevServerAddress.Parse(url, out var problem) is not { } address)
        {
            _log.Error($"{_settings.UrlSource}: {problem}");
            return Failed($"{_settings.UrlSource} is not a development server Spar can use: {problem}", external: true, address: null);
        }

        _log.Info($"Development server {address.Origin}, from {_settings.UrlSource}: Spar starts none.");
        return new DevServerOutcome(address, null, External: true, []);
    }

    /// <summary>The recorded server when it answers with its instance; one that does not is ended and forgotten.</summary>
    private async Task<DevServerOutcome?> ReuseAsync(int generation, CancellationToken token)
    {
        if (DevServerState.Read(_settings.StateFile) is not { } recorded)
        {
            return null;
        }

        // A server whose process is gone is not asked: on Windows a refused loopback connection takes about 2 s.
        var checking = Stopwatch.GetTimestamp();
        var alive = DevServerLauncher.IsAlive(recorded.ProcessId, recorded.StartedUnixMilliseconds);
        if (alive
            && DevServerAddress.Parse(recorded.Url, out _) is { } address
            && await AnswersAsync(address, recorded.Instance, token).ConfigureAwait(false))
        {
            _state = recorded;
            _output.Follow(_settings.OutputFile, fromEnd: true);
            _log.Info($"Development server {address.Origin} reused (Vite pid {recorded.ProcessId}), checked in {Stopwatch.GetElapsedTime(checking).TotalMilliseconds:0} ms.");
            Watch(generation, recorded);
            return new DevServerOutcome(address, null, External: false, []);
        }

        _log.Info(alive
            ? $"The development server recorded at {recorded.Url} does not answer: it is ended, and a new one starts."
            : $"The development server recorded at {recorded.Url} has ended: a new one starts.");
        DevServerLauncher.Stop(recorded, null);
        TryDelete(_settings.StateFile);
        return null;
    }

    private async Task<DevServerOutcome> StartAsync(int generation, CancellationToken token)
    {
        var started = Stopwatch.GetTimestamp();
        Process? previous;
        lock (_gate)
        {
            previous = _launcher;
            _launcher = null;
        }

        DevServerLauncher.Stop(_state, previous);
        previous?.Dispose();

        var port = DevServerLauncher.FreePort();
        var instance = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8));
        Process launcher;
        using (var output = File.OpenHandle(_settings.OutputFile, FileMode.Create, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete))
        {
            _output.Follow(_settings.OutputFile, fromEnd: false);
            launcher = DevServerLauncher.Start(_settings, port, instance, output, IdleSeconds);
        }

        lock (_gate)
        {
            _launcher = launcher;
        }

        _log.Info($"npm run dev started in {_settings.FrontendDirectory} (pid {launcher.Id}), port {port}.");
        while (true)
        {
            token.ThrowIfCancellationRequested();
            if (DevServerState.Read(_settings.StateFile) is { } state
                && state.Instance == instance
                && DevServerAddress.Parse(state.Url, out _) is { } address
                && await AnswersAsync(address, instance, token).ConfigureAwait(false))
            {
                _state = state;
                _log.Info($"Development server ready at {address.Origin} in {Stopwatch.GetElapsedTime(started).TotalMilliseconds:0} ms (Vite pid {state.ProcessId}).");
                Watch(generation, state);
                return new DevServerOutcome(address, null, External: false, []);
            }

            if (launcher.HasExited)
            {
                await _output.CatchUpAsync().ConfigureAwait(false);
                return Failed($"npm run dev ended with exit code {launcher.ExitCode} before the development server was ready.", external: false, address: null);
            }

            if (Stopwatch.GetElapsedTime(started).TotalMilliseconds > StartLimitMilliseconds)
            {
                DevServerLauncher.Stop(null, launcher);
                await _output.CatchUpAsync().ConfigureAwait(false);
                return Failed($"The development server did not record its address within {StartLimitMilliseconds / 1000} s, so it was ended. Is spar() in the plugins of vite.config.ts?", external: false, address: null);
            }

            await Task.Delay(50, token).ConfigureAwait(false);
        }
    }

    /// <summary>Whether the server at <paramref name="address"/> answers its identity with <paramref name="instance"/>.</summary>
    private async Task<bool> AnswersAsync(DevServerAddress address, string instance, CancellationToken token)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(token);
        limit.CancelAfter(TimeSpan.FromSeconds(2));
        try
        {
            using var response = await Client.GetAsync(address.Target(IdentityPath), limit.Token).ConfigureAwait(false);
            var text = await response.Content.ReadAsStringAsync(limit.Token).ConfigureAwait(false);
            return response.StatusCode == HttpStatusCode.OK && text.Trim() == instance;
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException && !token.IsCancellationRequested)
        {
            return false;
        }
    }

    /// <summary>Watches a ready server's process, off every UI thread, until it ends or another attempt replaces it.</summary>
    private void Watch(int generation, DevServerState state)
    {
        _ = Task.Run(async () =>
        {
            while (!_ending.IsCancellationRequested && generation == Volatile.Read(ref _generation))
            {
                if (!DevServerLauncher.IsAlive(state.ProcessId, state.StartedUnixMilliseconds))
                {
                    await StoppedAsync(generation, state).ConfigureAwait(false);
                    return;
                }

                await Task.Delay(1_000, _ending.Token).ConfigureAwait(false);
            }
        }, CancellationToken.None).ContinueWith(static _ => { }, TaskScheduler.Default);
    }

    private async Task StoppedAsync(int generation, DevServerState state)
    {
        await _output.CatchUpAsync().ConfigureAwait(false);
        lock (_gate)
        {
            if (generation != _generation || !_current.IsCompletedSuccessfully || !_current.Result.IsReady)
            {
                return;
            }

            _current = Task.FromResult(Failed($"The development server stopped (Vite pid {state.ProcessId}).", external: false, address: null));
        }

        _log.Warn($"The development server stopped (Vite pid {state.ProcessId}); the page shows the error page until Retry.");
        Stopped?.Invoke();
    }

    private DevServerOutcome Failed(string problem, bool external, DevServerAddress? address) =>
        new(address, problem, external, _output.Last);

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The plugin removes it when its server closes; a stale one is checked before use.
        }
    }
}
