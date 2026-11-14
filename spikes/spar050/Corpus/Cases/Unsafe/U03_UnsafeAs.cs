// Case: U03 | Unsafe.As<TFrom, PlanResult> reinterpreting other memory
// Kind: detect
// Why: the bits may be anything, including zero (SPAR051, a warning: unsafe code is the author's call).
namespace Corpus.Unsafe;

public struct U03Shadow
{
    public CopyPlan? Value;
    public PlanError? Error;
    public byte State;
    public bool HasValue;
}

public static class U03
{
    public static PlanResult Reinterpret(ref U03Shadow shadow) =>
        System.Runtime.CompilerServices.Unsafe.As<U03Shadow, PlanResult>(ref shadow); //~ SPAR051
}
