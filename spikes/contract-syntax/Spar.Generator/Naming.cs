using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace Spar.Generator;

/// <summary>Wire and TypeScript names: derivation rules and validation.</summary>
public static class Naming
{
    // Contract names become TypeScript constants and package folders, so they must not be JavaScript reserved words
    // or the names of Spar's own packages.
    private static readonly HashSet<string> ReservedContractNames =
    [
        "await", "break", "case", "catch", "class", "const", "continue", "debugger", "default", "delete", "do", "else",
        "enum", "export", "extends", "false", "finally", "for", "function", "if", "implements", "import", "in",
        "instanceof", "interface", "let", "new", "null", "package", "private", "protected", "public", "return",
        "static", "super", "switch", "this", "throw", "true", "try", "typeof", "var", "void", "while", "with", "yield",
        "sdk", "shared", "spar",
    ];

    /// <summary>Lower-cases a leading acronym as a unit: <c>URLValue</c> becomes <c>urlValue</c>, <c>Id</c> becomes <c>id</c>.</summary>
    public static string ToCamelCase(string name)
    {
        if (name.Length == 0 || !char.IsUpper(name[0]))
        {
            return name;
        }

        var chars = name.ToCharArray();
        for (var i = 0; i < chars.Length && char.IsUpper(chars[i]); i++)
        {
            // Keep the capital that starts the next word: "URLValue" -> "urlValue".
            if (i > 0 && i + 1 < chars.Length && char.IsLower(chars[i + 1]))
            {
                break;
            }

            chars[i] = char.ToLowerInvariant(chars[i]);
        }

        return new string(chars);
    }

    /// <summary><c>CopyJobsService</c> and <c>ICopyJobsService</c> become <c>copyJobs</c>.</summary>
    public static string ContractName(INamedTypeSymbol type)
    {
        var name = type.Name;
        if (type.TypeKind == TypeKind.Interface && name.Length > 1 && name[0] == 'I' && char.IsUpper(name[1]))
        {
            name = name.Substring(1);
        }

        const string Suffix = "Service";
        if (name.Length > Suffix.Length && name.EndsWith(Suffix, System.StringComparison.Ordinal))
        {
            name = name.Substring(0, name.Length - Suffix.Length);
        }

        return ToCamelCase(name);
    }

    /// <summary><c>GetPlanAsync</c> becomes <c>getPlan</c>.</summary>
    public static string CommandName(string methodName)
    {
        const string Suffix = "Async";
        var name = methodName.Length > Suffix.Length && methodName.EndsWith(Suffix, System.StringComparison.Ordinal)
            ? methodName.Substring(0, methodName.Length - Suffix.Length)
            : methodName;
        return ToCamelCase(name);
    }

    /// <summary>A contract or command wire name: a camelCase identifier of ASCII letters and digits.</summary>
    public static bool IsWireName(string name)
    {
        if (name.Length == 0 || name[0] < 'a' || name[0] > 'z')
        {
            return false;
        }

        foreach (var c in name)
        {
            if (!IsAsciiLetterOrDigit(c))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>A TypeScript type name or error code: an ASCII identifier.</summary>
    public static bool IsTypeScriptName(string name)
    {
        if (name.Length == 0 || char.IsDigit(name[0]))
        {
            return false;
        }

        foreach (var c in name)
        {
            if (!IsAsciiLetterOrDigit(c) && c != '_')
            {
                return false;
            }
        }

        return true;
    }

    public static bool IsReservedContractName(string name) => ReservedContractNames.Contains(name);

    private static bool IsAsciiLetterOrDigit(char c) => c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9');
}
