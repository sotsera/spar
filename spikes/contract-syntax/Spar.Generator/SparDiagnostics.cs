using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace Spar.Generator;

/// <summary>Every contract diagnostic. Messages are formatted by the reader; descriptors only carry id and title.</summary>
public static class SparDiagnostics
{
    public const string ContractKind = "SPAR001";
    public const string ContractGeneric = "SPAR002";
    public const string InvalidName = "SPAR003";
    public const string ReservedName = "SPAR004";
    public const string DuplicateContract = "SPAR005";

    public const string ReturnType = "SPAR010";
    public const string TooManyParameters = "SPAR011";
    public const string CancellationPosition = "SPAR012";
    public const string ParameterModifier = "SPAR013";
    public const string GenericCommand = "SPAR014";
    public const string RequestType = "SPAR015";
    public const string DuplicateCommand = "SPAR016";

    public const string UnsupportedType = "SPAR020";
    public const string RecordShape = "SPAR021";
    public const string RecordProperty = "SPAR022";
    public const string DuplicateProperty = "SPAR023";

    public const string ErrorNotClosed = "SPAR030";
    public const string ErrorCaseOutside = "SPAR031";
    public const string ErrorCaseShape = "SPAR032";
    public const string ReservedPayloadName = "SPAR033";
    public const string DuplicateErrorCode = "SPAR034";
    public const string ErrorWithoutCases = "SPAR035";

    public const string TypeScriptNameClash = "SPAR040";

    private static readonly Dictionary<string, string> Titles = new()
    {
        [ContractKind] = "A contract must be a sealed class or an interface",
        [ContractGeneric] = "A contract must not be generic",
        [InvalidName] = "Invalid explicit name",
        [ReservedName] = "Reserved contract name",
        [DuplicateContract] = "Two contracts have the same wire name",
        [ReturnType] = "Unsupported command return type",
        [TooManyParameters] = "A command takes at most one request parameter",
        [CancellationPosition] = "CancellationToken must be the last parameter",
        [ParameterModifier] = "Unsupported parameter modifier",
        [GenericCommand] = "A command must not be generic",
        [RequestType] = "Unsupported request type",
        [DuplicateCommand] = "Two commands have the same wire name",
        [UnsupportedType] = "Unsupported wire type",
        [RecordShape] = "Unsupported record shape",
        [RecordProperty] = "Record parameter without a property",
        [DuplicateProperty] = "Two properties have the same JSON name",
        [ErrorNotClosed] = "The error type must be a closed record",
        [ErrorCaseOutside] = "An error case must be nested in its closed record",
        [ErrorCaseShape] = "An error case must be a nested sealed record",
        [ReservedPayloadName] = "Reserved error payload name",
        [DuplicateErrorCode] = "Two error cases have the same code",
        [ErrorWithoutCases] = "The error type has no cases",
        [TypeScriptNameClash] = "Two types have the same TypeScript name in one contract",
    };

    public static ImmutableArray<DiagnosticDescriptor> All { get; } =
        [.. Titles.OrderBy(t => t.Key, System.StringComparer.Ordinal).Select(t => Create(t.Key, t.Value))];

    private static readonly Dictionary<string, DiagnosticDescriptor> ById = All.ToDictionary(d => d.Id);

    public static DiagnosticDescriptor Get(string id) => ById[id];

    private static DiagnosticDescriptor Create(string id, string title) =>
        new(id, title, "{0}", "Spar.Contracts", DiagnosticSeverity.Error, isEnabledByDefault: true);
}
