using Spar;

namespace SqlTool.Monitoring;

public interface IMonitor
{
    void TogglePause();
}

/// <summary>Polls the monitored instances; the tray item "Pause monitoring" toggles it.</summary>
public sealed class InstanceMonitor(ITray tray) : IMonitor
{
    private bool _paused;

    public void TogglePause()
    {
        _paused = !_paused;
        tray.SetChecked("pause", _paused);
        tray.SetTooltip(_paused ? "SQL Tool: monitoring paused" : "SQL Tool");
    }
}
