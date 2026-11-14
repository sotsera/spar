// Case: P21 | while (queue.TryDequeue(out var r)) { … }
// Kind: clean
// Why: the out variable is scoped to the loop, which runs only on success.
namespace Corpus.Apis;

public static class P21
{
    public static int Drain(Queue<PlanResult> queue)
    {
        var errors = 0;
        while (queue.TryDequeue(out var result))
        {
            errors += result.IsError ? 1 : 0;
        }

        return errors;
    }
}
