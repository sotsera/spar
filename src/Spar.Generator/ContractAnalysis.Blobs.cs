using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace Spar.Generator;

/// <summary>
/// Binary data from the backend (decision "M3b's decide-first questions", item 2): <c>SparBlob</c> handles in unary
/// results only (SPAR026), and <c>[SparResource]</c> commands, whose small request travels in an address (SPAR027).
/// </summary>
internal sealed partial class ContractAnalysis
{
    private static bool IsSparBlob(ITypeSymbol type) =>
        type is INamedTypeSymbol { Name: "SparBlob", TypeKind: TypeKind.Class } named
        && named.ContainingNamespace.ToDisplayString() == "Spar";

    /// <summary>Whether a value of this type can hold a blob: itself, in a list, nullable, or in a record at any depth.</summary>
    private bool HoldsBlob(TypeRef? type) => type is not null && HoldsBlob(type, new HashSet<string>(System.StringComparer.Ordinal));

    private bool HoldsBlob(TypeRef type, HashSet<string> seen)
    {
        switch (type.Kind)
        {
            case WireKind.Blob:
                return true;
            case WireKind.List or WireKind.Nullable:
                return HoldsBlob(type.Element!, seen);
            case WireKind.Record:
                var record = _records.Values.FirstOrDefault(r => r.Key == type.Key);
                return record is not null && seen.Add(record.Key) && record.Properties.Any(p => HoldsBlob(p.Type, seen));
            default:
                return false;
        }
    }

    /// <summary>Whether an error type's cases can hold a blob.</summary>
    private bool ErrorHoldsBlob(string? errorKey)
    {
        var error = errorKey is null ? null : _errors.Values.FirstOrDefault(e => e.Key == errorKey);
        return error is not null && error.Cases.Any(c => c.Properties.Any(p => HoldsBlob(p.Type)));
    }

    /// <summary>
    /// SPAR026 for a blob where the decision does not allow one: a request, a stream's items, a declared error. False
    /// when the command is left out.
    /// </summary>
    private bool BlobsInPlace(IMethodSymbol method, TypeRef? request, ReturnShape shape, TypeRef? value, string? errorKey)
    {
        string? where = null;
        if (HoldsBlob(request))
        {
            where = "in its request";
        }
        else if (shape is ReturnShape.Items or ReturnShape.ResultItems && HoldsBlob(value))
        {
            where = "in its stream's items";
        }
        else if (ErrorHoldsBlob(errorKey))
        {
            where = "in its declared error";
        }

        if (where is null)
        {
            return true;
        }

        Report(Descriptors.BlobOutOfPlace, method, $"Command '{method.Name}'", where);
        return false;
    }

    /// <summary>
    /// Whether the command is a <c>[SparResource]</c>, and it is a valid one: a unary command returning a blob or null,
    /// whose request is a scalar or a record of scalars. False, with SPAR027, when it is not; the command is left out.
    /// </summary>
    private bool Resource(IMethodSymbol method, TypeRef? request, ReturnShape shape, TypeRef? value, out bool isResource)
    {
        isResource = HasAttribute(method, "SparResourceAttribute");
        if (!isResource)
        {
            return true;
        }

        var blob = value is { Kind: WireKind.Blob } || value is { Kind: WireKind.Nullable, Element.Kind: WireKind.Blob };
        if (shape != ReturnShape.Value || !blob)
        {
            Report(Descriptors.InvalidResource, method, method.Name, Descriptors.ResourceReturn);
            return false;
        }

        if (request is not null && !IsAddressValue(request) && !IsRecordOfAddressValues(request))
        {
            Report(Descriptors.InvalidResource, method, method.Name, Descriptors.ResourceRequest);
            return false;
        }

        return true;
    }

    /// <summary>A scalar that reads well in an address: no list, record, blob or <c>byte[]</c>; nullable is fine.</summary>
    private static bool IsAddressValue(TypeRef type) => type.Kind switch
    {
        WireKind.Nullable => IsAddressValue(type.Element!),
        WireKind.List or WireKind.Record or WireKind.Blob or WireKind.Bytes => false,
        _ => true,
    };

    private bool IsRecordOfAddressValues(TypeRef type)
    {
        var record = type.Kind == WireKind.Record ? _records.Values.FirstOrDefault(r => r.Key == type.Key) : null;
        return record is not null && record.Properties.All(p => IsAddressValue(p.Type));
    }
}
