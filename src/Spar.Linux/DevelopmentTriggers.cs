using System.Collections.Concurrent;
using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Spar.Hosting;

namespace Spar.Linux;

/// <summary>
/// Development-only variables that force the hang paths on a healthy app, so they can be run and measured
/// (<c>docs/evidence/m1c-hangs.md</c>), the same as the Windows host's, and the prompts' answers, which no test may
/// give with real input on a user's desktop. Each is read only when <see cref="SparDevelopment.IsSupported"/>, which
/// is off and trimmed in Production, so a shipped app contains none of them, nor their names.
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

    /// <summary>Stalls the main loop for that many milliseconds once, at the first <c>ui-ready</c>, in a named callback.</summary>
    public const string UiStallVariable = "SPAR_DEV_UI_STALL_MS";

    /// <summary>Adds a service whose disposal takes that many milliseconds, so the shutdown runs long.</summary>
    public const string SlowDisposeVariable = "SPAR_DEV_SLOW_DISPOSE_MS";

    /// <summary>
    /// The answers to the hung-page dialogs and the shutdown prompt, in the order they are shown, comma-separated:
    /// <c>first</c> (Wait, Keep waiting) or <c>second</c> (Reload page, Close anyway, Exit now), each optionally
    /// <c>@&lt;ms&gt;</c> after the prompt shows (500 ms by default), or <c>none</c> to leave a prompt to the person.
    /// The prompt is shown as usual and taken down when the answer is given.
    /// </summary>
    public const string PromptAnswersVariable = "SPAR_DEV_PROMPT_ANSWERS";

    private const int DefaultAnswerDelayMilliseconds = 500;

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

    /// <summary>The run's scripted answers to its prompts, in order; empty when the variable is not set (one queue per run).</summary>
    public static ConcurrentQueue<string> PromptAnswers()
    {
        var answers = SparDevelopment.IsSupported ? Environment.GetEnvironmentVariable(PromptAnswersVariable) : null;
        return new ConcurrentQueue<string>(
            string.IsNullOrEmpty(answers) ? [] : answers.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    /// <summary>
    /// The next of the run's scripted <paramref name="answers"/>, if there is one: the answer and after how long to give
    /// it; false when the person answers (no answer left, or <c>none</c>).
    /// </summary>
    public static bool NextAnswer(ConcurrentQueue<string> answers, HostLog log, out PromptAnswer answer, out int delayMilliseconds)
    {
        answer = PromptAnswer.Dismissed;
        delayMilliseconds = DefaultAnswerDelayMilliseconds;
        if (!SparDevelopment.IsSupported || !answers.TryDequeue(out var next))
        {
            return false;
        }

        var parts = next.Split('@', 2);
        if (parts.Length == 2 && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var delay))
        {
            delayMilliseconds = delay;
        }

        switch (parts[0])
        {
            case "first":
                answer = PromptAnswer.First;
                return true;
            case "second":
                answer = PromptAnswer.Second;
                return true;
            case "none":
                return false;
            default:
                log.Warn($"Development trigger: {PromptAnswersVariable} lists '{next}'; each answer is first, second or none.");
                return false;
        }
    }

    /// <summary>Arms the page and main-loop triggers for the first <c>ui-ready</c> (loop thread).</summary>
    public static void Watch(MainLoop ui, ScriptBridge bridge, BrowserView view, HostLog log)
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

    private static void StallOnce(MainLoop ui, int milliseconds, HostLog log)
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

        // Continues on the loop, whose context the trigger was armed from.
        await Task.Delay(delay);
        var finite = parts.Length > 1;
        var script = finite
            ? $"setTimeout(() => {{ const end = performance.now() + {parts[1]}; while (performance.now() < end) {{ }} }}, 0); 'busy'"
            : "setTimeout(() => { for (;;) { } }, 0); 'hang'";
        view.RunPageScript(script);
        log.Warn(finite ? $"Development trigger: the page is busy for {parts[1]} ms from now." : "Development trigger: the page hangs from now.");
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
