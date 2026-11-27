using System.Linq;
using Microsoft.CodeAnalysis;

namespace Spar.Generator;

/// <summary>
/// A settings record (decision "M3b's decide-first questions", item 4): <c>spar.Settings(defaults)</c> names it, and it
/// follows the wire rules, so the generator writes its reader and writer as for a contract's records.
/// </summary>
internal sealed partial class ContractAnalysis
{
    public static SettingsModel? AnalyzeSettings(ITypeSymbol type) => new ContractAnalysis(null, null).Settings(type, type);

    /// <summary>Reads a settings record, reporting each problem at <paramref name="location"/>'s symbol.</summary>
    public static SettingsModel? AnalyzeSettings(ITypeSymbol type, ISymbol location, System.Action<ISymbol, Diagnostic> report) =>
        new ContractAnalysis(report, null).Settings(type, location);

    private SettingsModel? Settings(ITypeSymbol type, ISymbol location)
    {
        var reference = Wire(type, location);
        if (reference is null)
        {
            return null;
        }

        if (reference.Kind != WireKind.Record)
        {
            Report(Descriptors.UnsupportedWireType, location, type.ToDisplayString());
            return null;
        }

        if (HoldsBlob(reference))
        {
            Report(Descriptors.BlobOutOfPlace, location, $"Settings record '{type.ToDisplayString()}'", "in its properties");
            return null;
        }

        return new SettingsModel(
            reference.CSharp,
            reference.Key,
            _types.Values.OrderBy(t => t.Key, System.StringComparer.Ordinal).ToEquatable(),
            _records.Values.OrderBy(r => r.Key, System.StringComparer.Ordinal).ToEquatable(),
            _enums.Values.OrderBy(e => e.Key, System.StringComparer.Ordinal).ToEquatable());
    }
}
