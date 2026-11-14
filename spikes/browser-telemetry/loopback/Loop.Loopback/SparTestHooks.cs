using Microsoft.Extensions.DependencyInjection;

namespace Loop.Loopback;

/// <summary>What an in-process loopback host reports once it listens.</summary>
public sealed record LoopbackStarted(string Url, string Token, string Build, IServiceProvider? Services);

/// <summary>
/// Spike stand-in for an in-process test factory's hook. It lives in the development-only loopback library, so
/// Release builds contain none of it; the framework would use compile-time interception in a test build instead.
/// It is an AsyncLocal, so factories running concurrently in one test process each configure their own app.
/// </summary>
public static class SparTestHooks
{
    private static readonly AsyncLocal<Hooks?> CurrentHooks = new();

    public static Hooks? Current
    {
        get => CurrentHooks.Value;
        set => CurrentHooks.Value = value;
    }

    public sealed class Hooks
    {
        /// <summary>Runs after the application's own registrations and before the provider is built.</summary>
        public Action<IServiceCollection>? ConfigureServices { get; init; }

        public Action<LoopbackStarted>? Started { get; init; }

        public CancellationToken Stop { get; init; }

        /// <summary>Set by the application once its provider is built, so the test can inspect services.</summary>
        public IServiceProvider? Services { get; set; }
    }
}
