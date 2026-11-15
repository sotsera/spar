using Spar;

namespace SqlTool.Monitoring;

public sealed class NetworkAwareMonitor(INetworkStatus network, IMonitor monitor)
{
    /// <summary>Pauses monitoring while no network is available.</summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var paused = !network.Current.IsAvailable;
        await foreach (var status in network.Watch(cancellationToken))
        {
            if (status.IsAvailable == paused)
            {
                monitor.TogglePause();
                paused = !status.IsAvailable;
            }
        }
    }
}
