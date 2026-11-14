// Case: L04 | an unassigned local passed by ref
// Kind: impossible
// Why: CS0165.
namespace Corpus.Flow;

public static class L04
{
    public static PlanResult Get()
    {
        PlanResult result;
        Touch(ref result); //~ CS0165
        return result;
    }

    private static void Touch(ref PlanResult result) => result = result.IsSuccess ? result : Values.Ok;
}
