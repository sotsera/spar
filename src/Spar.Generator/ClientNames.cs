using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Spar.Generator;

/// <summary>
/// The names the generated TypeScript uses (plan section 2, "Names" and "Generated TypeScript"), in one place: the
/// analyzer checks contracts against them as you type, and the client step writes its modules with them, so a name the
/// modules start using is checked without a second change.
/// </summary>
internal static class ClientNames
{
    /// <summary>
    /// The SDK types the declaration files may import: each imports those its commands use. A contract type may take
    /// none of these names (SPAR004), so it can never shadow an import.
    /// </summary>
    public static readonly IReadOnlyList<string> SdkImports = ["Call", "CallOptions", "LatestOnlyEvents", "Result", "SubscribeOptions", "Subscription"];

    /// <summary>Subpaths the package uses itself; contract names become subpaths and folders, so case is ignored.</summary>
    public static readonly HashSet<string> ReservedSubpaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "sdk", "vite", "windows", "telemetry", "shared", "spar", "testing",
    };

    /// <summary>The names every contract module declares besides its contract object, its readers, writers and decoders.</summary>
    public static readonly HashSet<string> ModuleNames = new(StringComparer.Ordinal)
    {
        "invoke", "unknownErrorCase", "wire", "noValue", "noErrors",
    };

    /// <summary>Reserved words and strict-mode restrictions of ECMAScript modules, and TypeScript's own type keywords.</summary>
    private static readonly HashSet<string> Reserved = new(StringComparer.Ordinal)
    {
        "await", "break", "case", "catch", "class", "const", "continue", "debugger", "default", "delete", "do", "else",
        "enum", "export", "extends", "false", "finally", "for", "function", "if", "implements", "import", "in",
        "instanceof", "interface", "let", "new", "null", "package", "private", "protected", "public", "return",
        "static", "super", "switch", "this", "throw", "true", "try", "typeof", "var", "void", "while", "with", "yield",
        "arguments", "eval", "undefined", "NaN", "Infinity", "any", "boolean", "never", "number", "object", "string",
        "symbol", "unknown", "bigint",
    };

    private static readonly Regex LowerName = new("^[a-z][A-Za-z0-9]*$", RegexOptions.CultureInvariant);

    private static readonly Regex TypeName = new("^[A-Za-z][A-Za-z0-9]*$", RegexOptions.CultureInvariant);

    public static bool IsReserved(string name) => Reserved.Contains(name);

    /// <summary>A contract name, command name or error code: camelCase ASCII.</summary>
    public static bool IsValidLowerName(string name) => LowerName.IsMatch(name);

    /// <summary>A TypeScript type name: ASCII letters and digits, starting with a letter.</summary>
    public static bool IsValidTypeName(string name) => TypeName.IsMatch(name);

    /// <summary>The function that reads a record or an error type from the wire.</summary>
    public static string Reader(string typeName) => "read" + typeName;

    /// <summary>The function that writes a record to the wire.</summary>
    public static string Writer(string typeName) => "write" + typeName;

    /// <summary>The value and error decoders of one command.</summary>
    public static string Decoder(string commandName) => "decode" + Naming.Pascal(commandName);
}
