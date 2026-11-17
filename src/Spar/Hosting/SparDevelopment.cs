using System.Diagnostics.CodeAnalysis;

namespace Spar.Hosting;

/// <summary>
/// Whether this build carries Spar's development facilities: failure details for the page, and the automated mode's
/// environment variable (<c>SPAR_AUTOMATED=1</c>, decision "Two-stage close"). Spar.Sdk sets the switch on in the
/// Development and Test variants and off, with <c>Trim="true"</c>, in Production, so the trimmer removes every branch
/// it guards from a shipped app. Without the switch (a plain library build) it is off.
/// </summary>
internal static class SparDevelopment
{
    /// <summary>The variable that turns the automated mode on outside tests, in Development and Test builds only.</summary>
    public const string AutomatedVariable = "SPAR_AUTOMATED";

    [FeatureSwitchDefinition("Spar.Development.IsSupported")]
    internal static bool IsSupported { get; } = AppContext.TryGetSwitch("Spar.Development.IsSupported", out var on) && on;

    /// <summary>The automated mode from the environment; a Production build never honours it.</summary>
    public static bool IsAutomatedFromEnvironment() =>
        IsSupported && Environment.GetEnvironmentVariable(AutomatedVariable) == "1";
}
