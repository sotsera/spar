using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Spar.Hosting;

namespace Spar.Windows;

/// <summary>
/// Development-only variables that force the hang paths on a healthy app, so they can be run and measured
/// (<c>docs/evidence/m1b-hangs.md</c>), like the start-up overrides of <see cref="Prerequisites"/>. Each is read only
/// when <see cref="SparDevelopment.IsSupported"/>, which is off and trimmed in Production, so a shipped app contains
/// none of them, nor their names.
/// </summary>
internal static class DevelopmentTriggers
{
    /// <summary>The heartbeat's interval in milliseconds (at least 100), to run the Wait schedule quickly.</summary>
    public const string HeartbeatVariable = "SPAR_DEV_HEARTBEAT_MS";

    /// <summary>
    /// Makes the page hang after its first <c>ui-ready</c>: <c>&lt;delay ms&gt;</c> for an endless loop, or
    /// <c>&lt;delay ms&gt;:&lt;length ms&gt;</c> for finite work of that length.
    /// </summary>
    public const string PageHangVariable = "SPAR_DEV_PAGE_HANG";

    /// <summary>Stalls the UI thread for that many milliseconds once, at the first <c>ui-ready</c>, in a named callback.</summary>
    public const string UiStallVariable = "SPAR_DEV_UI_STALL_MS";

    /// <summary>Adds a service whose disposal takes that many milliseconds, so the shutdown runs long.</summary>
    public const string SlowDisposeVariable = "SPAR_DEV_SLOW_DISPOSE_MS";

    /// <summary>The heartbeat's interval: the fixed 5 s, unless a Development build overrides it.</summary>
    public static int HeartbeatInterval()
    {
        if (SparDevelopment.IsSupported && Milliseconds(HeartbeatVariable) is >= 100 and var interval)
        {
            return interval;
        }

        return PageHeartbeat.IntervalMilliseconds;
    }

    /// <summary>Registers the slow disposal, when asked for; it is resolved once the services exist, so it is disposed.</summary>
    public static void AddSlowDisposal(IServiceCollection services, HostLog log)
    {
        if (SparDevelopment.IsSupported && Milliseconds(SlowDisposeVariable) is > 0 and var milliseconds)
        {
            services.AddSingleton(_ => new SlowDisposal(milliseconds, log));
        }
    }

    /// <summary>Creates the slow disposal registered above, so the container disposes it at shutdown.</summary>
    public static void CreateSlowDisposal(IServiceProvider services)
    {
        if (SparDevelopment.IsSupported)
        {
            _ = services.GetService<SlowDisposal>();
        }
    }

    /// <summary>Arms the page and UI-thread triggers for the first <c>ui-ready</c> (UI thread).</summary>
    public static void Watch(UiThread ui, WebViewBridge bridge, BrowserView view, HostLog log)
    {
        if (!SparDevelopment.IsSupported)
        {
            return;
        }

        var stall = Milliseconds(UiStallVariable);
        var hang = Environment.GetEnvironmentVariable(PageHangVariable);
        if (stall is null && string.IsNullOrEmpty(hang))
        {
            return;
        }

        var fired = false;
        bridge.DocumentReady += () =>
        {
            if (fired)
            {
                return;
            }

            fired = true;
            if (stall is > 0 and var milliseconds)
            {
                StallOnce(ui, milliseconds, log);
            }

            if (!string.IsNullOrEmpty(hang))
            {
                _ = HangPageAsync(view, hang, log);
            }
        };
    }

    private static void StallOnce(UiThread ui, int milliseconds, HostLog log)
    {
        log.Warn($"Development trigger: stalling the UI thread for {milliseconds} ms.");
        using var scope = ui.Enter("development stall trigger");

        // Deliberately blocking: this is the stall the probe must report. Development builds only.
        Thread.Sleep(milliseconds);
    }

    /// <summary>After the delay, starts the page's endless or finite loop from a timer of its own, so the script returns.</summary>
    private static async Task HangPageAsync(BrowserView view, string specification, HostLog log)
    {
        var parts = specification.Split(':');
        if (!int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var delay)
            || (parts.Length > 1 && !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out _)))
        {
            log.Warn($"Development trigger: {PageHangVariable} must be <delay ms> or <delay ms>:<length ms>.");
            return;
        }

        await Task.Delay(delay);
        var finite = parts.Length > 1;
        var script = finite
            ? $"setTimeout(() => {{ const end = performance.now() + {parts[1]}; while (performance.now() < end) {{ }} }}, 0); 'busy'"
            : "setTimeout(() => { for (;;) { } }, 0); 'hang'";
        try
        {
            await view.RunScriptAsync(script);
            log.Warn(finite ? $"Development trigger: the page is busy for {parts[1]} ms from now." : "Development trigger: the page hangs from now.");
        }
        catch (Exception exception)
        {
            log.Error("Development trigger: the page could not be made to hang", exception);
        }
    }

    private static int? Milliseconds(string variable) =>
        int.TryParse(Environment.GetEnvironmentVariable(variable), NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? value : null;

    /// <summary>A service whose disposal blocks, standing in for an app service that does not stop.</summary>
    private sealed class SlowDisposal(int milliseconds, HostLog log) : IDisposable
    {
        public void Dispose()
        {
            log.Warn($"Development trigger: disposing slowly ({milliseconds} ms).");
            Thread.Sleep(milliseconds);
        }
    }
}
