namespace Spar.Generator;

/// <summary>
/// The libraries whose contracts an app serves (decision "Contracts from referenced libraries: the app opts in"): its
/// <c>&lt;SparContracts Include="Library" /&gt;</c> items, by assembly name, which <c>Spar.Sdk</c> passes the compiler
/// as one property, separated by semicolons.
/// </summary>
internal static class ContractLibraries
{
    /// <summary>The analyzer-config key the build writes the list under.</summary>
    public const string Property = "build_property.SparContractAssemblies";
}
