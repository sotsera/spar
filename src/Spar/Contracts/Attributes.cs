using JetBrains.Annotations;

namespace Spar;

/// <summary>Marks a class or interface as a contract. Without a name, the wire name is derived from the type name.</summary>
/// <remarks>A contract class is created from dependency injection, through any of its constructors.</remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface, Inherited = false)]
[MeansImplicitUse(ImplicitUseKindFlags.InstantiatedNoFixedConstructorSignature)]
public sealed class SparContractAttribute(string? name = null) : Attribute
{
    public string? Name { get; } = name;
}

/// <summary>Marks a command. Required on class contracts; on interface contracts it only overrides the name.</summary>
/// <remarks>
/// ReSharper and Rider count a method with this attribute as used. On an interface contract, add it to each method
/// those tools would otherwise report as never used.
/// </remarks>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
[MeansImplicitUse(ImplicitUseKindFlags.Access)]
public sealed class SparCommandAttribute(string? name = null) : Attribute
{
    public string? Name { get; } = name;

    /// <summary>
    /// An optional backend time limit, in seconds, folded into the call's cancellation token (decision "M1a names").
    /// There is no default: some commands wait on a person or run long.
    /// </summary>
    public int TimeoutSeconds { get; set; }
}

/// <summary>Gives a wire type or an error case an explicit name.</summary>
[AttributeUsage(
    AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface | AttributeTargets.Enum,
    Inherited = false)]
public sealed class SparNameAttribute(string name) : Attribute
{
    public string Name { get; } = name;
}
