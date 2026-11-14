// Case: F10 | a required property and a [SetsRequiredMembers] constructor that does not assign it
// Kind: detect
// Why: the attribute switches the compiler's required check off for that constructor.
using System.Diagnostics.CodeAnalysis;

namespace Corpus.Fields;

public sealed class F10
{
    [SetsRequiredMembers]
    public F10(int id)
    {
        Id = id;
    }

    public int Id { get; }

    public required PlanResult Last { get; init; } //~ SPAR053
}
