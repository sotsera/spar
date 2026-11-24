// The code-annotation attributes that ReSharper and Rider read by their full names, declared here, internal, so an app
// needs no annotations package. Spar's own attributes carry them: a contract class is created from dependency injection
// and a command is called by the generated dispatch, neither of which those tools see, so without them every contract and
// command shows as "never used". The shapes follow JetBrains' published annotations; only what Spar uses is declared.
namespace JetBrains.Annotations;

/// <summary>Code marked with the attribute this marks is used, though no reference to it can be found.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.GenericParameter | AttributeTargets.Parameter)]
internal sealed class MeansImplicitUseAttribute(
    ImplicitUseKindFlags useKindFlags = ImplicitUseKindFlags.Default,
    ImplicitUseTargetFlags targetFlags = ImplicitUseTargetFlags.Default) : Attribute
{
    public ImplicitUseKindFlags UseKindFlags { get; } = useKindFlags;

    public ImplicitUseTargetFlags TargetFlags { get; } = targetFlags;
}

/// <summary>How the marked code is used.</summary>
[Flags]
internal enum ImplicitUseKindFlags
{
    Default = Access | Assign | InstantiatedWithFixedConstructorSignature,

    /// <summary>A member is read or called.</summary>
    Access = 1,

    /// <summary>A member is written.</summary>
    Assign = 2,

    /// <summary>A type is created through a constructor of a known signature.</summary>
    InstantiatedWithFixedConstructorSignature = 4,

    /// <summary>A type is created through any of its constructors.</summary>
    InstantiatedNoFixedConstructorSignature = 8,
}

/// <summary>Which code the use applies to.</summary>
[Flags]
internal enum ImplicitUseTargetFlags
{
    Default = Itself,

    /// <summary>The marked type or member.</summary>
    Itself = 1,

    /// <summary>The members of the marked type.</summary>
    Members = 2,

    /// <summary>The types that derive from the marked one.</summary>
    WithInheritors = 4,

    /// <summary>The marked type and its members.</summary>
    WithMembers = Itself | Members,
}
