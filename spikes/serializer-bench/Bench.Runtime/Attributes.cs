namespace Bench;

/// <summary>Exports a class's <see cref="SparCommandAttribute"/> methods under a wire name.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class SparContractAttribute(string name) : Attribute
{
    public string Name { get; } = name;
}

/// <summary>Marks a method the frontend may call.</summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class SparCommandAttribute : Attribute;
