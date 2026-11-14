using System.Collections.Generic;
using System.Linq;
using System.Reflection.Metadata;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Spar.Generator;

/// <summary>
/// Detects C# 15 <c>closed</c> types without Roslyn's experimental API. <c>ITypeSymbol.IsClosed</c> and
/// <c>GetClosedDerivedTypeInfo</c> exist in Roslyn 5.9 but are marked experimental (RSEXPERIMENTAL006), and
/// <c>GetAttributes()</c> hides the <c>IsClosedTypeAttribute</c> the compiler emits. So:
/// <list type="bullet">
/// <item>a type with syntax (this compilation, or a project reference in the IDE) is closed when a declaration has the
/// <c>closed</c> modifier;</item>
/// <item>a type from a metadata reference is closed when its type definition carries
/// <c>System.Runtime.CompilerServices.IsClosedTypeAttribute</c>, read with System.Reflection.Metadata.</item>
/// </list>
/// </summary>
public static class ClosedTypes
{
    private const string ClosedAttributeNamespace = "System.Runtime.CompilerServices";
    private const string ClosedAttributeName = "IsClosedTypeAttribute";

    public static bool IsClosed(INamedTypeSymbol type, Compilation compilation, CancellationToken cancellationToken)
    {
        var declarations = type.DeclaringSyntaxReferences;
        if (declarations.Length > 0)
        {
            // Compare the token text, not SyntaxKind.ClosedKeyword: the enum value is compiled into this assembly and
            // the IDE or SDK may load a newer Roslyn.
            return declarations.Any(d => d.GetSyntax(cancellationToken) is TypeDeclarationSyntax declaration
                && declaration.Modifiers.Any(m => m.ValueText == "closed"));
        }

        return HasClosedAttributeInMetadata(type, compilation);
    }

    /// <summary>
    /// Every type derived directly from <paramref name="baseType"/>. A closed type can only be derived from in its own
    /// assembly, so walking that assembly finds them all.
    /// </summary>
    public static List<INamedTypeSymbol> FindDerived(INamedTypeSymbol baseType, CancellationToken cancellationToken)
    {
        var derived = new List<INamedTypeSymbol>();
        var pending = new Stack<INamespaceOrTypeSymbol>();
        pending.Push(baseType.ContainingAssembly.GlobalNamespace);
        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var member in pending.Pop().GetMembers())
            {
                if (member is INamespaceOrTypeSymbol container)
                {
                    pending.Push(container);
                }

                if (member is INamedTypeSymbol type && SymbolEqualityComparer.Default.Equals(type.BaseType, baseType))
                {
                    derived.Add(type);
                }
            }
        }

        return derived;
    }

    private static bool HasClosedAttributeInMetadata(INamedTypeSymbol type, Compilation compilation)
    {
        if (compilation.GetMetadataReference(type.ContainingAssembly) is not PortableExecutableReference reference
            || reference.GetMetadata() is not AssemblyMetadata metadata)
        {
            return false;
        }

        foreach (var module in metadata.GetModules())
        {
            var reader = module.GetMetadataReader();
            foreach (var handle in reader.TypeDefinitions)
            {
                var definition = reader.GetTypeDefinition(handle);
                if (Matches(reader, definition, type))
                {
                    return definition.GetCustomAttributes().Any(a => IsClosedAttribute(reader, reader.GetCustomAttribute(a)));
                }
            }
        }

        return false;
    }

    private static bool Matches(MetadataReader reader, TypeDefinition definition, INamedTypeSymbol type)
    {
        if (!reader.StringComparer.Equals(definition.Name, type.MetadataName))
        {
            return false;
        }

        var declaring = definition.GetDeclaringType();
        if (type.ContainingType is { } outer)
        {
            return !declaring.IsNil && Matches(reader, reader.GetTypeDefinition(declaring), outer);
        }

        var ns = type.ContainingNamespace.IsGlobalNamespace ? string.Empty : type.ContainingNamespace.ToDisplayString();
        return declaring.IsNil && reader.StringComparer.Equals(definition.Namespace, ns);
    }

    private static bool IsClosedAttribute(MetadataReader reader, CustomAttribute attribute)
    {
        switch (attribute.Constructor.Kind)
        {
            case HandleKind.MemberReference:
                var parent = reader.GetMemberReference((MemberReferenceHandle)attribute.Constructor).Parent;
                if (parent.Kind == HandleKind.TypeReference)
                {
                    var typeReference = reader.GetTypeReference((TypeReferenceHandle)parent);
                    return reader.StringComparer.Equals(typeReference.Name, ClosedAttributeName)
                        && reader.StringComparer.Equals(typeReference.Namespace, ClosedAttributeNamespace);
                }

                return false;
            case HandleKind.MethodDefinition:
                // The attribute embedded in the assembly itself (older target frameworks).
                var declaring = reader.GetTypeDefinition(reader.GetMethodDefinition((MethodDefinitionHandle)attribute.Constructor).GetDeclaringType());
                return reader.StringComparer.Equals(declaring.Name, ClosedAttributeName)
                    && reader.StringComparer.Equals(declaring.Namespace, ClosedAttributeNamespace);
            default:
                return false;
        }
    }
}
