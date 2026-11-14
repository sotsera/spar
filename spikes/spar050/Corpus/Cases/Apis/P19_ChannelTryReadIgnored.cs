// Case: P19 | ChannelReader<PlanResult>.TryRead(out var r) with the bool ignored
// Kind: detect
// Why: the same [MaybeNullWhen(false)] contract as TryGetValue.
using System.Threading.Channels;

namespace Corpus.Apis;

public static class P19
{
    public static PlanResult Next(ChannelReader<PlanResult> reader)
    {
        reader.TryRead(out var result); //~ SPAR051
        return result;
    }
}
