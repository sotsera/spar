using Microsoft.CodeAnalysis;

namespace Spar.Generator;

/// <summary>
/// The diagnostics this slice implements, with the IDs of the decided map (plan section 2, "Diagnostics";
/// spikes/contract-syntax). The rest of the map comes with the analyzer's full rule set in M1a.
/// </summary>
internal static class Descriptors
{
    private const string Contracts = "Spar.Contracts";
    private const string ProgramShape = "Spar.Program";

    public static readonly DiagnosticDescriptor NotSealedClassOrInterface = Error(
        "SPAR001", "Contract must be a sealed class or an interface", "Contract '{0}' must be a sealed class or an interface", Contracts);

    public static readonly DiagnosticDescriptor GenericContract = Error(
        "SPAR002", "Contract must not be generic", "Contract '{0}' must not be generic", Contracts);

    public static readonly DiagnosticDescriptor UnsupportedReturn = Error(
        "SPAR010", "Unsupported command return type",
        "Command '{0}' returns '{1}'. A command returns Task, Task<T>, Task<Result<T, TError>>, Task<Result<TError>> or their ValueTask forms",
        Contracts);

    public static readonly DiagnosticDescriptor TooManyRequests = Error(
        "SPAR011", "Too many request parameters",
        "Command '{0}' takes at most one request parameter, then a CancellationToken. Put the values in one record", Contracts);

    public static readonly DiagnosticDescriptor TokenNotLast = Error(
        "SPAR012", "CancellationToken must be last", "The CancellationToken of command '{0}' must be its last parameter", Contracts);

    public static readonly DiagnosticDescriptor RefParameter = Error(
        "SPAR013", "Parameter cannot be ref, out, in or params", "Parameter '{0}' of command '{1}' cannot be ref, out, in or params", Contracts);

    public static readonly DiagnosticDescriptor GenericCommand = Error(
        "SPAR014", "Command must not be generic", "Command '{0}' must not be generic", Contracts);

    public static readonly DiagnosticDescriptor UnsupportedWireType = Error(
        "SPAR020", "Unsupported wire type", "Type '{0}' is not a supported wire type", Contracts);

    public static readonly DiagnosticDescriptor NotPositionalRecord = Error(
        "SPAR021", "Record must be positional",
        "Record '{0}' must be a non-abstract, non-generic positional record whose values are its constructor parameters", Contracts);

    public static readonly DiagnosticDescriptor BadErrorType = Error(
        "SPAR030", "Unsupported error type",
        "Error type '{0}' must be a non-generic closed record with nested sealed record cases", Contracts);

    public static readonly DiagnosticDescriptor ReservedPayloadName = Error(
        "SPAR033", "Reserved payload name",
        "Error case '{0}' cannot have a payload named '{1}': 'kind', 'code', 'message' and 'execution' are reserved", Contracts);

    public static readonly DiagnosticDescriptor NoErrorCases = Error(
        "SPAR035", "Error type has no cases", "Error type '{0}' has no cases", Contracts);

    public static readonly DiagnosticDescriptor NotTopLevel = Error(
        "SPAR060", "Program.cs must use top-level statements",
        "Program.cs must be top-level statements with one SparApplication.Create and one Run; the entry point is '{0}'", ProgramShape);

    public static readonly DiagnosticDescriptor NotReturningRun = Error(
        "SPAR061", "Program.cs must return app.Run()",
        "The top-level statements of Program.cs must end with 'return app.Run();' (an int); they return '{0}'", ProgramShape);

    private static DiagnosticDescriptor Error(string id, string title, string message, string category) =>
        new(id, title, message, category, DiagnosticSeverity.Error, isEnabledByDefault: true);
}
