using Microsoft.CodeAnalysis;

namespace Spar.Analyzers;

/// <summary>The SPAR05x rules. A default <c>Result</c> is neither a value nor an error, and every member throws on it.</summary>
public static class Descriptors
{
    public const string Category = "Spar.Results";

    /// <summary>An expression that definitely creates a default Result, or a value that holds one.</summary>
    public static readonly DiagnosticDescriptor DefaultResult = new(
        id: "SPAR050",
        title: "default(Result) is neither a value nor an error",
        messageFormat: "'{0}' creates {1}, which is neither a value nor an error. Use a value, an error case, or Result.Success.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>An API that returns, or leaves in an out argument, a default Result when it finds nothing.</summary>
    public static readonly DiagnosticDescriptor MayReturnDefault = new(
        id: "SPAR051",
        title: "This API may return a default Result",
        messageFormat: "'{0}' may give a default {1} ({2}), which is neither a value nor an error",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    /// <summary>An array, buffer or list slot whose Result elements start as default.</summary>
    public static readonly DiagnosticDescriptor DefaultElements = new(
        id: "SPAR052",
        title: "Result elements start as default",
        messageFormat: "'{0}' leaves {1} elements default until each is assigned. Build the collection from values, or assign every element before reading it.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    /// <summary>A field or auto-property holding a Result that can be read before anything assigns it.</summary>
    public static readonly DiagnosticDescriptor UnassignedMember = new(
        id: "SPAR053",
        title: "A Result field can be read before it is assigned",
        messageFormat: "'{0}' {1}, so it can be read as a default {2}. Assign it in every constructor, give it an initializer, make it required, or declare it as '{2}?'.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    /// <summary>
    /// A generic method in source creates default(T), and this call binds T to a Result. Reported when the compilation
    /// ends, because the call and the method body are analysed separately.
    /// </summary>
    public static readonly DiagnosticDescriptor GenericDefault = new(
        id: "SPAR054",
        title: "A generic method creates default(T) for a Result",
        messageFormat: "'{0}' creates default({1}) in its body, and {1} is '{2}' here",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        customTags: WellKnownDiagnosticTags.CompilationEnd);
}
