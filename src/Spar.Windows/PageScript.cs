using Spar.Hosting;

namespace Spar.Windows;

/// <summary>
/// The script the host adds to every document (<c>PageScript.js</c>, embedded): the heartbeat's answer, the Content
/// Security Policy listener (plan section 3b) and the page's error reports (plan M1b, build step 7). Read once at start,
/// off the UI thread, with the listener's settings and both reports' flood limits written in.
/// </summary>
internal static class PageScript
{
    /// <summary>The most often each report posts a batch.</summary>
    public const int ReportIntervalMilliseconds = CspViolationLog.ListenerIntervalMilliseconds;

    /// <summary>How long a report collects a burst before its first batch.</summary>
    public const int ReportFirstDelayMilliseconds = CspViolationLog.ListenerFirstDelayMilliseconds;

    /// <summary>The most distinct groups the listener reports per document; the host keeps its own bound too.</summary>
    public const int ReportMaxGroups = CspViolationLog.ListenerMaxGroups;

    /// <summary>The most violations the listener counts per document before it stops listening.</summary>
    public const int ReportMaxViolations = CspViolationLog.ListenerMaxViolations;

    /// <summary>The most distinct groups of errors the script reports per document; the host keeps its own bound too.</summary>
    public const int ErrorMaxGroups = 100;

    /// <summary>The most errors the script counts per document before it stops reporting them.</summary>
    public const int ErrorMaxErrors = 1_000;

    private const string ResourceName = "Spar.Windows.PageScript.js";

    /// <summary>
    /// Whether this build reads the page's console errors from the in-process DevTools Protocol instead of the script's
    /// wrapper (decision "Logging's follow-up choices"): a Development build, where devtools are on and their links
    /// should point at the page's code; not a Test build, nor Production, where the switch is off and this is trimmed.
    /// </summary>
    public static bool ConsoleFromDevTools => SparDevelopment.IsSupported && !SparTestSession.IsSupported;

    /// <param name="reportViolations">Whether the listener reports (<c>csp.Reporting(listener: …)</c> for this build).</param>
    public static string Text(bool reportViolations)
    {
        using var stream = typeof(PageScript).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"The resource {ResourceName} is missing from Spar.Windows.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd()
            .Replace("__SPAR_REPORT_VIOLATIONS__", reportViolations ? "true" : "false", StringComparison.Ordinal)
            .Replace("__SPAR_REPORT_INTERVAL_MS__", Number(ReportIntervalMilliseconds), StringComparison.Ordinal)
            .Replace("__SPAR_REPORT_FIRST_DELAY_MS__", Number(ReportFirstDelayMilliseconds), StringComparison.Ordinal)
            .Replace("__SPAR_REPORT_MAX_GROUPS__", Number(ReportMaxGroups), StringComparison.Ordinal)
            .Replace("__SPAR_REPORT_MAX_VIOLATIONS__", Number(ReportMaxViolations), StringComparison.Ordinal)
            .Replace("__SPAR_ERROR_MAX_GROUPS__", Number(ErrorMaxGroups), StringComparison.Ordinal)
            .Replace("__SPAR_ERROR_MAX_ERRORS__", Number(ErrorMaxErrors), StringComparison.Ordinal)
            .Replace("__SPAR_WRAP_CONSOLE__", ConsoleFromDevTools ? "false" : "true", StringComparison.Ordinal);
    }

    private static string Number(int value) => value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
