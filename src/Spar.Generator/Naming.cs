using System;

namespace Spar.Generator;

/// <summary>Wire names (plan section 2, "Names"): the camelCase of <c>JsonNamingPolicy.CamelCase</c>, acronym-aware.</summary>
internal static class Naming
{
    /// <summary>Classes keep their name, interfaces drop one leading I, both drop a trailing Service.</summary>
    public static string Contract(string typeName, bool isInterface)
    {
        var name = typeName;
        if (isInterface && name.Length > 1 && name[0] == 'I' && char.IsUpper(name[1]))
        {
            name = name.Substring(1);
        }

        if (name.Length > "Service".Length && name.EndsWith("Service", StringComparison.Ordinal))
        {
            name = name.Substring(0, name.Length - "Service".Length);
        }

        return CamelCase(name);
    }

    /// <summary>Commands drop a trailing Async.</summary>
    public static string Command(string methodName)
    {
        var name = methodName.Length > "Async".Length && methodName.EndsWith("Async", StringComparison.Ordinal)
            ? methodName.Substring(0, methodName.Length - "Async".Length)
            : methodName;
        return CamelCase(name);
    }

    /// <summary>The same rule as System.Text.Json's camelCase policy: "IOStats" becomes "ioStats", "URL" becomes "url".</summary>
    public static string CamelCase(string name)
    {
        if (name.Length == 0 || !char.IsUpper(name[0]))
        {
            return name;
        }

        var chars = name.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (i == 1 && !char.IsUpper(chars[i]))
            {
                break;
            }

            var hasNext = i + 1 < chars.Length;
            if (i > 0 && hasNext && !char.IsUpper(chars[i + 1]))
            {
                if (chars[i + 1] == ' ')
                {
                    chars[i] = char.ToLowerInvariant(chars[i]);
                }

                break;
            }

            chars[i] = char.ToLowerInvariant(chars[i]);
        }

        return new string(chars);
    }

    public static string Pascal(string camel) => camel.Length == 0 ? camel : char.ToUpperInvariant(camel[0]) + camel.Substring(1);

    /// <summary>A C# identifier fragment from a type's full name, for generated method names.</summary>
    public static string Identifier(string text)
    {
        var chars = text.Replace("global::", "").ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (!char.IsLetterOrDigit(chars[i]))
            {
                chars[i] = '_';
            }
        }

        return new string(chars);
    }
}
