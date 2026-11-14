// Case: A22 | clearing a buffer slot with default to release references
// Kind: by-design
// Why: a ring buffer that clears a slot on dequeue stores a default Result there. Store Result? (null) instead.
namespace Corpus.Arrays;

public sealed class A22Ring
{
    private readonly PlanResult[] _items = [Values.Ok, Values.Ok];
    private int _head;

    public PlanResult Dequeue()
    {
        var item = _items[_head];
        _items[_head] = default; //~ SPAR050
        _head = (_head + 1) % _items.Length;
        return item;
    }
}
