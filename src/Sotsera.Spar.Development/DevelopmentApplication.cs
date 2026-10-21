using System.IO.Pipes;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using System.Threading.Channels;
using Sotsera.Spar.Bridge;
using Sotsera.Spar.Windows;

namespace Sotsera.Spar.Development;

/// <summary>Debug-only application adapter. Reference this assembly only in a development configuration.</summary>
public sealed partial class DevelopmentApplication : IDisposable
{
    private readonly SessionConnection _connection;
    private readonly Channel<SessionMessage> _outbound = Channel.CreateBounded<SessionMessage>(64);
    private readonly Task _writer;
    private readonly Task _reader;
    private readonly string _contract;
    private readonly int _generation;
    private readonly Action<long, Type[]?> _updated;
    private SparWindow? _window;
    private string? _document;
    private Exception? _failure;
    private int _stopRequested;
    private volatile bool _ending;

    public SparLoopbackContent Content
    {
        get;
    }

    public string UserDataFolder
    {
        get;
    }

    public bool InspectionEnabled { get; }

    private DevelopmentApplication(SessionConnection connection, SessionMessage configuration, string contract,
        HashSet<Type> contractTypes)
    {
        _connection = connection;
        _contract = contract;
        _generation = configuration.Generation;
        Content = new SparLoopbackContent(new Uri(configuration.Url!), configuration.Credential!);
        UserDataFolder = configuration.Profile!;
        InspectionEnabled = configuration.InspectionEnabled;
        _updated = (revision, types) =>
        {
            var contractChanged = types is null || types.Any(contractTypes.Contains);
            Send(new SessionMessage(contractChanged ? "app-contract-update" : "app-update", Contract: _contract,
                Generation: _generation, UpdateRevision: revision));
        };
        DevelopmentUpdates.Subscribe(_updated);
        _writer = WriteLoopAsync();
        _reader = ReadLoopAsync();
    }

    public static DevelopmentApplication Connect(string contract, Type contractType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contract);
        var contractTypes = GetContractTypes(contractType);
        if (Environment.GetEnvironmentVariable("SPAR_DEV_BACKEND_OWNER") == "debugger")
        {
            foreach (var assembly in contractTypes.Select(type => type.Assembly).Distinct())
            {
                RequireImmutableDebuggerContract(assembly.Location);
            }
        }
        var compiledContract = DevelopmentContract.GetFingerprint(contractType);
        var pipeName = Environment.GetEnvironmentVariable("SPAR_DEV_APP_PIPE")
            ?? throw new InvalidOperationException("Start the development application through Spar.Dev.");
        var token = Environment.GetEnvironmentVariable("SPAR_DEV_TOKEN");
        var session = Environment.GetEnvironmentVariable("SPAR_DEV_SESSION");
        var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        var connection = new SessionConnection(pipe);
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            pipe.ConnectAsync(timeout.Token).GetAwaiter().GetResult();
            connection.WriteAsync(new SessionMessage("app-hello", token, session, Environment.ProcessId, Contract: compiledContract), timeout.Token)
                .GetAwaiter().GetResult();
            var configuration = connection.ReadAsync(timeout.Token).GetAwaiter().GetResult();
            if (configuration?.Kind != "configure" || configuration.Token != token || configuration.Session != session
                || configuration.Contract != contract || compiledContract != contract
                || configuration.Url is null || configuration.Credential is null
                || configuration.Profile is null)
            {
                throw new InvalidDataException("The owning session did not supply a matching application configuration.");
            }

            return new DevelopmentApplication(connection, configuration, contract, contractTypes);
        }
        catch
        {
            connection.DisposeAsync().AsTask().GetAwaiter().GetResult();
            throw;
        }
    }

    public void Attach(SparWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        _window = window;
        Send(new SessionMessage("app-window", Window: window.Handle.ToInt64(), Generation: _generation));
        if (Volatile.Read(ref _stopRequested) != 0)
        {
            window.Close();
        }
    }

    public void ObserveBridge(SparBridgeEvent value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Stage == "connected")
        {
            Volatile.Write(ref _document, value.Document);
            Send(new SessionMessage("app-bridge", Contract: _contract, Document: value.Document, Generation: _generation));
        }
        else if (value.Stage == "development-ready" && value.Document == Volatile.Read(ref _document))
        {
            Send(new SessionMessage("app-ready", Contract: _contract, Document: value.Document, Generation: _generation));
        }
        else if (value.Code is not null)
        {
            Send(new SessionMessage("app-diagnostic", Document: value.Document, Detail: value.Code, Generation: _generation));
        }
    }

    /// <summary>Consumer-owned readiness receipt; it does not add exports or an inspector to the runtime.</summary>
    public void Report(string stage, string detail)
    {
        if (stage != "ui")
        {
            return;
        }
        using var parsed = JsonDocument.Parse(detail, new JsonDocumentOptions { MaxDepth = 8 });
        var document = parsed.RootElement.GetProperty("document").GetString();
        var contract = parsed.RootElement.GetProperty("contract").GetString();
        if (document != Volatile.Read(ref _document) || contract != _contract)
        {
            throw new InvalidDataException("Readiness came from a retired or mismatched document.");
        }

        Send(new SessionMessage("app-ui", Contract: contract, Document: document, Detail: detail, Generation: _generation));
    }

    public void Dispose()
    {
        DevelopmentUpdates.Unsubscribe(_updated);
        Task? inspection;
        lock (_inspectionGate)
        {
            _ending = true;
            inspection = _inspectionTask;
        }
        inspection?.GetAwaiter().GetResult();
        _outbound.Writer.TryComplete();
        _writer.GetAwaiter().GetResult();
        _connection.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _reader.GetAwaiter().GetResult();
        if (_failure is not null)
        {
            throw new InvalidOperationException("The owning development session disconnected.", _failure);
        }
    }

    public void ReportActivity(string stage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stage);
        if (stage.Length > 256)
        {
            throw new ArgumentException("An activity label cannot exceed 256 characters.", nameof(stage));
        }

        Send(new SessionMessage("app-activity", Detail: stage, Generation: _generation));
    }

    internal static void RequireImmutableDebuggerContract(string assemblyPath)
    {
        using var stream = File.OpenRead(assemblyPath);
        using var executable = new PEReader(stream);
        if (executable.ReadDebugDirectory().Any(entry => entry.Type is DebugDirectoryEntryType.CodeView
            or DebugDirectoryEntryType.EmbeddedPortablePdb))
        {
            throw new InvalidOperationException("Debugger sessions require separately compiled contract assemblies "
                + "with DebugType=none. Contract edits must rebuild and establish a new bridge handshake.");
        }
    }

    private void Send(SessionMessage message)
    {
        if (!_outbound.Writer.TryWrite(message))
        {
            Interlocked.CompareExchange(ref _failure, new InvalidOperationException("The bounded development event queue is full."), null);
            Stop();
        }
    }

    private async Task WriteLoopAsync()
    {
        try
        {
            await foreach (var message in _outbound.Reader.ReadAllAsync().ConfigureAwait(false))
            {
                await _connection.WriteAsync(message).ConfigureAwait(false);
            }
        }
        catch (Exception error) when (error is IOException or ObjectDisposedException or JsonException or System.Text.DecoderFallbackException)
        {
            if (!_ending)
            {
                Interlocked.CompareExchange(ref _failure, error, null);
                Stop();
            }
        }
    }

    private async Task ReadLoopAsync()
    {
        try
        {
            while (await _connection.ReadAsync().ConfigureAwait(false) is { } message)
            {
                if (message.Kind == "stop")
                {
                    Stop();
                }
                else if (message.Kind == "inspect" && InspectionEnabled && message.Inspection is { } request)
                {
                    DispatchInspection(request);
                }
            }

            if (!_ending)
            {
                Stop();
            }
        }
        catch (Exception error) when (error is IOException or ObjectDisposedException or JsonException or System.Text.DecoderFallbackException)
        {
            if (!_ending)
            {
                Interlocked.CompareExchange(ref _failure, error, null);
                Stop();
            }
        }
    }

    private void Stop()
    {
        Interlocked.Exchange(ref _stopRequested, 1);
        _window?.Close();
    }

    private static HashSet<Type> GetContractTypes(Type contractType)
    {
        ArgumentNullException.ThrowIfNull(contractType);
        var methods = contractType.GetMethods();
        if (!contractType.IsInterface || methods.Length is < 1 or > 32)
        {
            throw new ArgumentException("Supply the compiled Spar export interface.", nameof(contractType));
        }

        var result = new HashSet<Type> { contractType };
        foreach (var method in methods)
        {
            var parameters = method.GetParameters();
            if (parameters.Length != 2 || parameters[1].ParameterType != typeof(CancellationToken)
                || !method.ReturnType.IsGenericType || method.ReturnType.GetGenericTypeDefinition() != typeof(Task<>))
            {
                throw new ArgumentException("The export interface must match the generated flat Task-based contract.", nameof(contractType));
            }

            result.Add(parameters[0].ParameterType);
            result.Add(method.ReturnType.GenericTypeArguments[0]);
        }

        return result;
    }
}
