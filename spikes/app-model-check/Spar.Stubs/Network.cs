namespace Spar;

/// <summary>Whether a network is available, and whether it is metered (Windows only).</summary>
public sealed record NetworkStatus(bool IsAvailable, bool IsMetered);

/// <summary>Network availability, built on .NET's NetworkChange; injectable. The page uses navigator.onLine instead.</summary>
public interface INetworkStatus
{
    NetworkStatus Current { get; }

    /// <summary>Yields only real changes, debounced by about a second.</summary>
    IAsyncEnumerable<NetworkStatus> Watch(CancellationToken cancellationToken);
}
