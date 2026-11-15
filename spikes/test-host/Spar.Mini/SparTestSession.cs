using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Spar;

/// <summary>
/// The test host's hook. A test sets <see cref="Current"/> (an AsyncLocal) and calls the app's entry point on a thread
/// of its own; <c>Create</c> captures the session and <c>Run</c> builds the app with the test's folders, log and
/// service overrides instead of opening the app's window. Production builds set the feature switch off, so the trimmer
/// removes this type and every branch that reaches it.
/// </summary>
internal sealed class SparTestSession
{
    /// <summary>The boundary check searches published binaries for this text.</summary>
    internal const string Marker = "SPAR_TEST_HOST_PRESENT_7f3c";

    private static readonly AsyncLocal<SparTestSession?> CurrentSession = new();

    private readonly TaskCompletionSource<SparRuntime> _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly ManualResetEventSlim _stop = new();

    [FeatureSwitchDefinition("Spar.TestHost.IsSupported")]
    internal static bool IsSupported { get; } = AppContext.TryGetSwitch("Spar.TestHost.IsSupported", out var on) && on;

    internal static SparTestSession? Current
    {
        get => CurrentSession.Value;
        set => CurrentSession.Value = value;
    }

    public required SparFolders Folders { get; init; }

    public required MemorySparLog Log { get; init; }

    public Action<IServiceCollection>? ConfigureServices { get; init; }

    public required IReadOnlySet<Type> AllowedExternals { get; init; }

    public Task<SparRuntime> Started => _started.Task;

    public void Stop() => _stop.Set();

    internal int Run(SparBuilder builder)
    {
        var services = builder.Services;
        // Outward services default to fakes in a test host, so a test never touches the real clipboard by accident.
        // They are registered here, behind the feature switch, so Production builds do not even contain them.
        services.TryAddSingleton<FakeClipboard>();
        services.TryAddSingleton<IClipboard>(provider => provider.GetRequiredService<FakeClipboard>());
        SparApp.AddSparServices(services, Folders, Log);

        // The test's overrides run last, after the app's registrations and Spar's defaults.
        ConfigureServices?.Invoke(services);
        SparExternalGuard.Apply(services, AllowedExternals);

        SparRuntime runtime;
        try
        {
            runtime = SparRuntime.Build(builder, services, Folders, Log);
        }
        catch (Exception exception)
        {
            _started.TrySetException(exception);
            return 70;
        }

        Log.Write("spar", Marker);
        _started.TrySetResult(runtime);

        // This is the app's own thread (the test host started it for the entry point), so waiting here blocks nothing else.
        _stop.Wait();
        runtime.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _stop.Dispose();
        return 0;
    }
}
