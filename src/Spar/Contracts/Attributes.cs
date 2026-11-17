namespace Spar;

/// <summary>Marks a class or interface as a contract. Without a name, the wire name is derived from the type name.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface, Inherited = false)]
public sealed class SparContractAttribute(string? name = null) : Attribute
{
    public string? Name { get; } = name;
}

/// <summary>Marks a command. Required on class contracts; on interface contracts it only overrides the name.</summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
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
