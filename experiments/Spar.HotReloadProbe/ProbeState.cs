using System.Diagnostics.CodeAnalysis;

namespace Sotsera.Spar.HotReloadProbe;

// These bases provide a deliberate compilable edit that changes type inheritance.
// They are probe fixtures, not proposed framework abstractions.
[SuppressMessage("Performance", "CA1852", Justification = "Base type deliberately changes during the reload experiment.")]
internal class InitialProbeBase;

[SuppressMessage("Performance", "CA1852", Justification = "Becomes the base type during the reload experiment.")]
internal class ReplacementProbeBase;

internal sealed class ProbeState : InitialProbeBase
{
    private int _counter;

    public Guid InstanceId { get; } = Guid.NewGuid();

    public int NextCounter()
    {
        _counter++;
        return _counter;
    }
}
