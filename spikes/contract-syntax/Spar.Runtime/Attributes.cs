namespace Spar;

/// <summary>
/// Exports a contract: a sealed class (one instance per call, from the call's DI scope) or an interface whose
/// implementation is registered in DI. Every public method is a command. The wire name is derived from the type name
/// (<c>PlanningService</c> and <c>IPlanning</c> become <c>planning</c>) unless <paramref name="name"/> is given.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface, Inherited = false)]
public sealed class SparContractAttribute : Attribute
{
    public SparContractAttribute()
    {
    }

    public SparContractAttribute(string name)
    {
        Name = name;
    }

    public string? Name { get; }
}

/// <summary>Overrides a command's wire name (by default the method name without <c>Async</c>, in camelCase).</summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class SparCommandAttribute(string name) : Attribute
{
    public string Name { get; } = name;
}

/// <summary>
/// Overrides a record's TypeScript name, or an error case's <c>code</c>. Needed when two different types with the
/// same name are used by one contract.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class SparNameAttribute(string name) : Attribute
{
    public string Name { get; } = name;
}
