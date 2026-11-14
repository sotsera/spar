// Case: U04 | MemoryMarshal.Cast<byte, PlanResult>
// Kind: runtime
// Why: .NET itself throws ArgumentException for a type that holds references, so no default Result is produced.
using System.Runtime.InteropServices;

namespace Corpus.Unsafe;

public static class U04
{
    public static int Count(Span<byte> bytes) => MemoryMarshal.Cast<byte, PlanResult>(bytes).Length;
}
