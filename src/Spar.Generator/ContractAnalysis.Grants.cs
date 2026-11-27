using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace Spar.Generator;

/// <summary>
/// File and folder grants (decision "M3c's decide-first questions", item 2): <c>FileGrant</c> and <c>FolderGrant</c>
/// travel both ways, in requests, results, stream items and their records; a request's grants are resolved to the
/// call's window, so its handler reads them in a grant scope. A settings record cannot hold one (SPAR028): a grant ends
/// with its window, and a settings file outlives it.
/// </summary>
internal sealed partial class ContractAnalysis
{
    private static WireKind? GrantKind(ITypeSymbol type)
    {
        if (type is not INamedTypeSymbol { TypeKind: TypeKind.Class } named || named.ContainingNamespace.ToDisplayString() != "Spar")
        {
            return null;
        }

        return named.Name switch
        {
            "FileGrant" => WireKind.FileGrant,
            "FolderGrant" => WireKind.FolderGrant,
            _ => null,
        };
    }

    /// <summary>Whether a value of this type can hold a grant: itself, in a list, nullable, or in a record at any depth.</summary>
    private bool HoldsGrant(TypeRef? type) => type is not null && HoldsGrant(type, new HashSet<string>(System.StringComparer.Ordinal));

    private bool HoldsGrant(TypeRef type, HashSet<string> seen)
    {
        switch (type.Kind)
        {
            case WireKind.FileGrant or WireKind.FolderGrant:
                return true;
            case WireKind.List or WireKind.Nullable:
                return HoldsGrant(type.Element!, seen);
            case WireKind.Record:
                var record = _records.Values.FirstOrDefault(r => r.Key == type.Key);
                return record is not null && seen.Add(record.Key) && record.Properties.Any(p => HoldsGrant(p.Type, seen));
            default:
                return false;
        }
    }
}
