using Microsoft.Extensions.Logging;
using Spar.Hosting;

namespace Spar;

/// <summary>
/// What Spar records for diagnosing the app on a user's machine (<c>docs/app-model.md</c> section 3.13), in
/// <c>spar.Diagnostics(d => …)</c>. Crash records and <c>diagnostics.collect()</c> join it in M3.
/// </summary>
public sealed class DiagnosticsOptions
{
    internal DiagnosticsOptions()
    {
    }

    /// <summary>Creates the stall probe kept in a Production build; null unless the app called <see cref="KeepStallProbe"/>.</summary>
    internal Func<ILogger, IUiThreadWatch>? KeptStallProbe { get; private set; }

    /// <summary>
    /// Keeps the UI-thread stall probe in a Production build: the window's thread is probed every few tens of
    /// milliseconds, and each delay over 50 ms is logged as a warning with the Spar callback that was running, in the
    /// per-user log. It only logs. Development and Test builds always have it. Without this call a Production build
    /// contains no probe at all: this call is what keeps its code in the executable.
    /// </summary>
    public DiagnosticsOptions KeepStallProbe()
    {
        KeptStallProbe = static logger => new UiStallProbe(logger, TimeProvider.System);
        return this;
    }

    /// <summary>
    /// The probe for this run of the app: always in Development and Test builds, where the feature switch keeps it, and
    /// in Production only when kept. The switch is off and trimmed in Production, so only <see cref="KeepStallProbe"/>
    /// can root the probe's code there.
    /// </summary>
    internal IUiThreadWatch? CreateStallProbe(ILogger logger) =>
        SparDevelopment.IsSupported ? new UiStallProbe(logger, TimeProvider.System) : KeptStallProbe?.Invoke(logger);
}
