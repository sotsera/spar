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
}

/// <summary>Gives a wire type an explicit name, for two types with the same name in one contract.</summary>
[AttributeUsage(
    AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface | AttributeTargets.Enum,
    Inherited = false)]
public sealed class SparNameAttribute(string name) : Attribute
{
    public string Name { get; } = name;
}

/// <summary>Makes a stream command a subscription: bounded buffer, oldest events dropped.</summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class SparSubscriptionAttribute : Attribute
{
    public int Capacity { get; set; }

    public SubscriptionResume Resume { get; set; }
}

/// <summary>Whether a subscription reconnects by itself after a lost connection.</summary>
public enum SubscriptionResume
{
    Automatic,
    Manual,
}
