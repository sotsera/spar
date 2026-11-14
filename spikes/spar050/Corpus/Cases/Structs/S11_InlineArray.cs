// Case: S11 | an [InlineArray] buffer of Results
// Kind: detect
// Why: new Buffer() zeroes every element. The element field itself is not reported (no constructor to check).
using System.Runtime.CompilerServices;

namespace Corpus.Structs;

[InlineArray(4)]
public struct S11Buffer
{
    private PlanResult _element;
}

public static class S11
{
    public static S11Buffer Make() => new S11Buffer(); //~ SPAR050
}
