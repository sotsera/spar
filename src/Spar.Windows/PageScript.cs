namespace Spar.Windows;

/// <summary>
/// The script the host adds to every document (<c>PageScript.js</c>, embedded): the heartbeat's answer and the
/// Content Security Policy listener (plan section 3b). Read once at start, off the UI thread, with the listener's
/// settings and flood limits written in.
/// </summary>
internal static class PageScript
{
    /// <summary>The most often the listener posts a batch.</summary>
    public const int ReportIntervalMilliseconds = 1_000;

    /// <summary>How long the listener collects a burst before its first batch.</summary>
    public const int ReportFirstDelayMilliseconds = 100;

    /// <summary>The most distinct groups the listener reports per document; the host keeps its own bound too.</summary>
    public const int ReportMaxGroups = 100;

    /// <summary>The most violations the listener counts per document before it stops listening.</summary>
    public const int ReportMaxViolations = 1_000;

    private const string ResourceName = "Spar.Windows.PageScript.js";

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
            .Replace("__SPAR_REPORT_MAX_VIOLATIONS__", Number(ReportMaxViolations), StringComparison.Ordinal);
    }

    private static string Number(int value) => value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
