using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;

namespace Spar.Generator;

/// <summary>A valid scene (decision "M2c's decide-first questions", item 1): its name, its method, and a test method's name.</summary>
internal sealed record SceneModel(string Name, string Method, string TestName);

/// <summary>
/// The scenes of a test project, read the same way by the generator (which registers the valid ones) and the analyzer
/// (which reports the others, SPAR110 to SPAR112): a <c>[SparScene("name")]</c> method is <c>static</c>, returns
/// <c>Task</c>, takes one <c>SparScene</c>, is not generic, and is reachable from the project's own code (not private,
/// nor in a private type); its name is lower case letters, digits and single hyphens.
/// </summary>
internal static class SceneAnalysis
{
    public const string Attribute = "Spar.Testing.SparSceneAttribute";
    public const string SceneType = "Spar.Testing.SparScene";
    public const string Registry = "Spar.Testing.SparScenes";

    private static readonly Regex Name = new("^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.CultureInvariant);

    /// <summary>The scene's name from its attribute, or null when the attribute has no constant string.</summary>
    public static string? NameOf(AttributeData attribute) =>
        attribute.ConstructorArguments.Length == 1 && attribute.ConstructorArguments[0].Value is string name ? name : null;

    /// <summary>Why the method cannot be a scene, or null when it can.</summary>
    public static string? ShapeProblem(IMethodSymbol method)
    {
        if (!method.IsStatic)
        {
            return "it is not static";
        }

        if (method.IsGenericMethod)
        {
            return "it is generic";
        }

        if (method.ReturnType.ToDisplayString() != "System.Threading.Tasks.Task")
        {
            return $"it returns {method.ReturnType.ToDisplayString()}, not Task";
        }

        if (method.Parameters.Length != 1 || method.Parameters[0].Type.ToDisplayString() != SceneType || method.Parameters[0].RefKind != RefKind.None)
        {
            return "it does not take exactly one SparScene";
        }

        for (ISymbol? symbol = method; symbol is not null and not INamespaceSymbol; symbol = symbol.ContainingType)
        {
            if (symbol.DeclaredAccessibility is Accessibility.Private or Accessibility.Protected or Accessibility.ProtectedAndInternal)
            {
                return $"{symbol.Name} is {symbol.DeclaredAccessibility.ToString().ToLowerInvariant()}, so the project's generated code cannot call it";
            }
        }

        return null;
    }

    public static bool IsValidName(string name) => Name.IsMatch(name);

    /// <summary>The scene, or null when its name or its method's shape is wrong (the analyzer says why).</summary>
    public static SceneModel? Analyze(IMethodSymbol method, AttributeData attribute)
    {
        if (NameOf(attribute) is not { } name || !IsValidName(name) || ShapeProblem(method) is not null)
        {
            return null;
        }

        var target = method.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + "." + method.Name;
        return new SceneModel(name, target, "Scene_" + name.Replace('-', '_'));
    }
}
