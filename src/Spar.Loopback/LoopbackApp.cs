using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Connections;
using Spar.DevServer;
using Spar.Hosting;
using Spar.HotReload;
using Spar.Logging;
using Spar.Runtime;

namespace Spar.Loopback;

/// <summary>
/// One run of a Development build on loopback (plan section 4; decision "M1d step 2's shape: loopback in the development
/// loop"): the app's backend, and its page served to an ordinary browser on 127.0.0.1 with the bridge over HTTP. There is
/// no window and no UI thread. In order:
/// <list type="number">
/// <item>The folders and the log file, as a desktop run of the same build has them (its <c>.Development</c> folder).</item>
/// <item>The page's source: the development server (Node checked first, exit 3 without it), or the packed page of a build
/// with <c>SparDevServer=false</c>.</item>
/// <item>The address (<see cref="LoopbackSession"/>): the dev session's port and token, or an orchestrator's.</item>
/// <item>The services, built on the thread pool while the address is resolved; then the host listens, the address with
/// its token is written to standard output and the log, and the first run of a dev session opens the browser.</item>
/// <item>The run lasts until Ctrl+C or a termination signal; the page's channels then close, and an open page waits for
/// the next run at the same address and token (<c>src/Spar.Client/src/restart.ts</c>).</item>
/// </list>
/// Everything here runs on the process's main thread or the thread pool; nothing waits on a thread that serves pages.
/// </summary>
internal sealed class LoopbackApp
{
    /// <summary>The app's own start failure, the same code as the desktop hosts' (70).</summary>
    public const int StartFailedExitCode = 70;

    /// <summary>The run's address record, a runtime configuration value of Development builds (Spar.Sdk).</summary>
    private const string SessionFileData = "Spar.Loopback.SessionFile";

    /// <summary>Why the devtools' page tools do not work here: they need the app's own window.</summary>
    private const string PagesInBrowser =
        "The page runs in a browser (the Loopback profile): the page tools need the app's own window. contracts, call, calls and logs work.";

    /// <summary>How long a run waits for its recorded port, which the previous run may still be releasing.</summary>
    private static readonly TimeSpan PortWait = TimeSpan.FromSeconds(3);

    /// <summary>How often a running run marks the record as held (<see cref="LoopbackSession.Touch"/>).</summary>
    private static readonly TimeSpan TouchInterval = TimeSpan.FromMinutes(1);

    private readonly SparApp _app;
    private readonly HostLog _log = new();
    private readonly bool _automated = SparDevelopment.IsAutomatedFromEnvironment();
    private readonly TaskCompletionSource _stop = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly string? _sessionFile = AppContext.GetData(SessionFileData) as string is { Length: > 0 } file ? file : null;
    private Spar.Devtools.DevtoolsHost? _devtools;

    public LoopbackApp(SparApp app) => _app = app;

    public int Run()
    {
        // The process's main thread, which has nothing else to do until the run ends.
        try
        {
            return RunAsync().GetAwaiter().GetResult();
        }
        finally
        {
            _log.Close();
        }
    }

    private async Task<int> RunAsync()
    {
        var builder = _app.Builder;
        var folders = builder.ResolveFolders();
        if (SparFolders.DataRootProblem(folders.DataRoot) is { } networkRoot)
        {
            return Fail(networkRoot);
        }

        var logging = builder.LoggingOptions;
        var levels = SparLogging.Levels(logging, folders.DataRoot, SparDevelopment.IsSupported, out var levelProblem);
        if (logging.WritesFile)
        {
            _log.UseFile(folders.Logs, levels);
        }

        _log.Info($"Host started on loopback (no window); {BuildIdentity.Describe(_app.Catalog.BuildIdentity.Span)}; app id {SparFolders.ChannelId(_app.AppId, development: true)}, folder {folders.DataRoot}");
        if (levelProblem is not null)
        {
            _log.Warn(levelProblem);
        }

        // A WinExe app on Windows has no console: attached to its parent's (dotnet run's, under dotnet watch), it gets the
        // Ctrl+C that asks it to stop, and stops as for its own signals (decision "Step 3's two follow-ups"). First, before
        // anything registers a signal handler: one registered while the process had no console never saw the Ctrl+C.
        ConsoleInterrupt.TryAttach(_log);

        DevServerSession? devServer = null;
        SparFrontend? frontend = null;
        if (DevServerSettings.IsSupported && DevServerSettings.Read() is { } settings)
        {
            if (settings.Url is null && DevServerLauncher.CheckNode() is { } noNode)
            {
                return Report(noNode);
            }

            devServer = DevServerSession.Start(settings, _log);
        }
        else
        {
            frontend = _app.Catalog.AppAssembly is { } assembly ? SparFrontend.Open(assembly) : null;
            if (frontend is null)
            {
                return Fail(PageResponder.NoPage);
            }
        }

        var built = Task.Run(() => Build(folders, levels));
        var address = LoopbackSession.Resolve(_sessionFile, Environment.GetEnvironmentVariable, DateTimeOffset.UtcNow, out var addressProblem);
        if (address is null)
        {
            devServer?.Dispose();
            return Fail(addressProblem!);
        }

        SparRunningApp running;
        try
        {
            running = await built.ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException or AggregateException)
        {
            devServer?.Dispose();
            _log.Error("The app's services could not be built", exception);
            return StartFailedExitCode;
        }

        return await ServeAsync(running, address, frontend, devServer).ConfigureAwait(false);
    }

    private async Task<int> ServeAsync(SparRunningApp running, LoopbackAddress address, SparFrontend? frontend, DevServerSession? devServer)
    {
        var security = PageSecuritySetup.Resolve(_app, _log.Logger);
        var development = SparDevelopment.IsSupported;
        var violations = PagePolicy.Reports(security.Endpoint, development)
            ? new CspViolationLog(_log.Logger, TimeProvider.System, development ? CspAdvice.For : null)
            : null;
        if (frontend is not null)
        {
            // As a desktop host does at start: decode the compressed files off every thread that serves them.
            await Task.Run(frontend.DecodeAll).ConfigureAwait(false);
        }

        var runId = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8));
        LoopbackOptions Options(LoopbackAddress at) => new()
        {
            Dispatcher = running.Dispatcher,
            Fingerprint = running.Catalog.Fingerprint,
            Page = frontend is null ? null : new PackedPage(frontend),
            DevServer = devServer,
            HostLog = _log,
            IncludeDetails = development,
            Security = security,
            ScriptHashes = frontend is null ? [] : PageHtml.ImportMapHashes(frontend),
            StyleHashes = frontend is null ? [] : PageHtml.StyleHashes(frontend),
            DataRoot = running.Folders.DataRoot,
            Port = at.Port,
            Token = at.Token,
            RunId = runId,
            Reports = violations is null ? null : violations.PostReport,
            Log = _log.Warn,
        };

        var (host, used) = await ListenAsync(address, Options).ConfigureAwait(false);
        if (host is null)
        {
            await StopAsync(null, violations, devServer, running).ConfigureAwait(false);
            return StartFailedExitCode;
        }

        host.DocumentReady += _ => _log.Phase("ui-ready");
        _log.Phase("loopback listening");
        Announce(host, used);

        // The Hot Reload signal (plan section 4): every open document gets each applied update.
        using var updates = BackendUpdates.IsSupported ? BackendUpdates.Listen(update => SendBackendUpdate(host, update)) : null;

        using var signals = ListenForStop();
        var touching = KeepHeldAsync(used);
        await _stop.Task.ConfigureAwait(false);

        _log.Phase("teardown");
        await StopAsync(host, violations, devServer, running).ConfigureAwait(false);
        await touching.ConfigureAwait(false);
        if (!used.Orchestrated && _sessionFile is not null)
        {
            LoopbackSession.Touch(_sessionFile, DateTimeOffset.UtcNow);
        }

        _log.Step("exit 0");
        return 0;
    }

    /// <summary>A Hot Reload update to every open document, on the runtime's Hot Reload thread: the sessions only queue it.</summary>
    private void SendBackendUpdate(LoopbackHost host, BackendUpdate update)
    {
        var documents = host.SendEvent(BackendUpdates.EventName, update.Payload);
        _log.Info($"Hot Reload applied revision {update.Revision} ({update.TypeList}); sent to {documents} document(s).");
    }

    /// <summary>
    /// Listens at the session's port, waiting a moment for the previous run to release it. A port held by this app's
    /// own running copy stops the start; one held by another program gives the run a new port and a new dev session.
    /// </summary>
    private async Task<(LoopbackHost? Host, LoopbackAddress Used)> ListenAsync(LoopbackAddress address, Func<LoopbackAddress, LoopbackOptions> options)
    {
        var waited = Stopwatch.StartNew();
        while (true)
        {
            try
            {
                return (await LoopbackHost.StartAsync(options(address), CancellationToken.None).ConfigureAwait(false), address);
            }
            catch (IOException exception) when (exception.InnerException is AddressInUseException && address.Port != 0)
            {
                if (waited.Elapsed < PortWait)
                {
                    await Task.Delay(100).ConfigureAwait(false);
                    continue;
                }

                if (address.Orchestrated)
                {
                    _log.Error($"Port {address.Port} of 127.0.0.1, from {LoopbackSession.PortVariable}, is in use by another program.");
                    return (null, address);
                }

                if (await AnswersAsync(address).ConfigureAwait(false))
                {
                    _log.Error($"The app already runs on loopback at http://127.0.0.1:{address.Port}/ (another dotnet run?): stop it first.");
                    return (null, address);
                }

                _log.Warn($"Port {address.Port} of 127.0.0.1 is in use by another program: this run takes a free port and starts a new dev session.");
                address = new LoopbackAddress(0, LoopbackSession.NewToken(), NewSession: true, Orchestrated: false);
            }
        }
    }

    /// <summary>Whether a run of this app answers at the address with its token.</summary>
    private static async Task<bool> AnswersAsync(LoopbackAddress address)
    {
        using var client = new HttpClient(new SocketsHttpHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(3) };
        using var request = new HttpRequestMessage(HttpMethod.Post, $"http://127.0.0.1:{address.Port}/_spar/session");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", address.Token);
        try
        {
            using var response = await client.SendAsync(request).ConfigureAwait(false);
            return response.StatusCode == HttpStatusCode.OK;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            return false;
        }
    }

    /// <summary>The address on standard output and in the log, the record written, and the browser opened once per session.</summary>
    private void Announce(LoopbackHost host, LoopbackAddress used)
    {
        var url = host.PageUrl();
        var recorded = !used.Orchestrated && _sessionFile is not null
            && LoopbackSession.Write(_sessionFile, host.Origin.Port, host.Token);
        if (!used.Orchestrated && _sessionFile is not null && !recorded)
        {
            _log.Warn($"The dev session could not be recorded in {_sessionFile}: the next run takes a new address.");
        }

        var how = used switch
        {
            { Orchestrated: true } => "set by the environment",
            { NewSession: true } => "a new dev session",
            _ => "the dev session's address: an open page reloads by itself",
        };
        Console.Out.WriteLine($"Spar: the page is at {url} ({how}). Ctrl+C stops the app.");
        Console.Out.Flush();
        _log.Info($"Loopback page at {url} ({how}).");

        if (used.NewSession && !used.Orchestrated)
        {
            _ = BrowserOpener.OpenAsync(url, Environment.GetEnvironmentVariable(BrowserOpener.Variable), _log);
        }
    }

    /// <summary>Ctrl+C and the termination signals end the run cleanly, once.</summary>
    private IDisposable ListenForStop()
    {
        void Stop(PosixSignalContext context)
        {
            context.Cancel = true;
            _log.Info($"{context.Signal} received: the app stops.");
            _stop.TrySetResult();
        }

        return new Registrations(
            PosixSignalRegistration.Create(PosixSignal.SIGINT, Stop),
            PosixSignalRegistration.Create(PosixSignal.SIGTERM, Stop),
            PosixSignalRegistration.Create(PosixSignal.SIGQUIT, Stop));
    }

    /// <summary>Marks the record as held every minute, so a long run's session never looks over (<see cref="LoopbackSession"/>).</summary>
    private async Task KeepHeldAsync(LoopbackAddress used)
    {
        if (used.Orchestrated || _sessionFile is null)
        {
            return;
        }

        using var timer = new PeriodicTimer(TouchInterval);
        while (await Task.WhenAny(timer.WaitForNextTickAsync().AsTask(), _stop.Task).ConfigureAwait(false) != _stop.Task)
        {
            LoopbackSession.Touch(_sessionFile, DateTimeOffset.UtcNow);
        }
    }

    private SparRunningApp Build(SparFolders folders, LogLevels levels)
    {
        var builder = _app.Builder;
        Func<LogFile>? file = _log.File is { } opened ? () => opened : null;
        SparLogging.Add(builder.Services, levels, file, ownsFile: false);
        SparRunningApp.AddSparServices(builder.Services, _app.Catalog, folders, builder.BridgeOptions.Freeze());
        if (Spar.Devtools.SparDevtools.IsSupported)
        {
            _devtools = Spar.Devtools.DevtoolsHost.Prepare(builder.Services, "loopback", PagesInBrowser);
            if (_devtools is { } devtools)
            {
                devtools.EndSession = () =>
                {
                    _log.Info("The devtools asked the app to stop.");
                    return _stop.TrySetResult();
                };
            }
        }

        var running = SparRunningApp.Build(builder.Services, _app.Catalog, folders);
        _log.UseAppLogging(running.Services);
        _log.Phase("services built");
        if (Spar.Devtools.SparDevtools.IsSupported)
        {
            _devtools?.Start(running, _log.RunId, _log.Logger);
        }

        return running;
    }

    /// <summary>The pages' channels first, then what reads into the log, then the app's services.</summary>
    private async Task StopAsync(LoopbackHost? host, CspViolationLog? violations, DevServerSession? devServer, SparRunningApp running)
    {
        try
        {
            if (Spar.Devtools.SparDevtools.IsSupported && _devtools is { } devtools)
            {
                await devtools.StopAsync().ConfigureAwait(false);
            }

            if (host is not null)
            {
                await host.DisposeAsync().ConfigureAwait(false);
            }

            if (violations is not null)
            {
                await violations.DisposeAsync().ConfigureAwait(false);
            }

            devServer?.Dispose();
            await running.DisposeAsync().ConfigureAwait(false);
            _log.Step("services disposed");
        }
        catch (Exception exception)
        {
            _log.Error("The app's services did not stop cleanly", exception);
        }
    }

    private int Fail(string message)
    {
        _log.Error($"The app cannot start on loopback: {message}");
        return StartFailedExitCode;
    }

    /// <summary>A start-up problem: on standard error and in the log, then the app's hook; the exit code is the problem's.</summary>
    private int Report(StartupProblem problem)
    {
        _log.Error(StartupReport.Describe(problem));
        if (!_automated)
        {
            StartupReport.AppShowedItsOwnMessage(_app.Builder.StartupProblemHandler, problem, (message, exception) => _log.Error(message, exception));
        }

        return problem.ExitCode;
    }

    private sealed class Registrations(params PosixSignalRegistration[] registrations) : IDisposable
    {
        public void Dispose()
        {
            foreach (var registration in registrations)
            {
                registration.Dispose();
            }
        }
    }
}
