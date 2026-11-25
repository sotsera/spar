namespace Spar.Tests;

/// <summary>
/// Spar's contract attributes carry the code annotation that ReSharper and Rider read by its full name, so a contract
/// class and its commands, reached only through dependency injection and the generated dispatch, do not show as unused.
/// </summary>
public sealed class ImplicitUseTests
{
    private const string MeansImplicitUse = "JetBrains.Annotations.MeansImplicitUseAttribute";

    [Fact]
    public void A_contract_is_created_through_any_constructor_and_a_command_is_called()
    {
        // ImplicitUseKindFlags: Access = 1, InstantiatedNoFixedConstructorSignature = 8; ImplicitUseTargetFlags.Itself = 1.
        Assert.Equal((8, 1), Annotation(typeof(SparContractAttribute)));
        Assert.Equal((1, 1), Annotation(typeof(SparCommandAttribute)));
    }

    private static (int Kind, int Target) Annotation(Type attribute)
    {
        var data = Assert.Single(attribute.GetCustomAttributesData(), a => a.AttributeType.FullName == MeansImplicitUse);
        Assert.False(data.AttributeType.IsPublic, "The annotation types are internal to Spar.");
        var arguments = data.ConstructorArguments;
        return ((int)arguments[0].Value!, (int)arguments[1].Value!);
    }
}
