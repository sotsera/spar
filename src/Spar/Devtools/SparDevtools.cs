using System.Diagnostics.CodeAnalysis;

namespace Spar.Devtools;

/// <summary>
/// The devtools (plan section 6; decisions "M2's decide-first questions" and "M2a's decide-first questions"): tools that
/// let an agent or a script look at and drive a running Development build, over a local socket only its user can open.
/// Spar.Sdk turns the switch on in the Development variant unless the app sets <c>SparDevtools=false</c>, and off, with
/// <c>Trim="true"</c>, everywhere else, so no other build carries any of it (the production boundary test checks it).
/// </summary>
internal static class SparDevtools
{
    public const string Switch = "Spar.Devtools.IsSupported";

    /// <summary>The runtime configuration value with the discovery file's path, the project's <c>obj/spar-devtools.json</c>.</summary>
    public const string FileData = "Spar.Devtools.File";

    /// <summary>The embedded resource with the contract snapshot, in Development builds.</summary>
    public const string ContractsResource = "Spar.Devtools.Contracts";

    [FeatureSwitchDefinition(Switch)]
    internal static bool IsSupported { get; } = AppContext.TryGetSwitch(Switch, out var on) && on;

    /// <summary>Where this build writes its discovery file, or null when the build named none (a library, a test).</summary>
    public static string? DiscoveryFile(Func<string, object?> data) =>
        data(FileData) is string { Length: > 0 } file && Path.IsPathFullyQualified(file) ? file : null;
}
