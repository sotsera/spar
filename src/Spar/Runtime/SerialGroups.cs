namespace Spar.Runtime;

/// <summary>
/// The app's serial groups (decision "M3b's decide-first questions", item 1): the calls of one group run one at a time,
/// in arrival order, whoever made them (every window's pages, the typed test clients, the devtools). A call waiting for
/// its turn holds nothing but its place in the group's queue; one cancelled while it waits leaves the queue and never runs.
/// </summary>
internal sealed class SerialGroups
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, Group> _groups = new(StringComparer.Ordinal);

    /// <summary>
    /// Waits for <paramref name="name"/>'s turn and returns what ends it. Cancelled while waiting, it throws an
    /// <see cref="OperationCanceledException"/> on <paramref name="cancellationToken"/> and leaves the queue.
    /// </summary>
    public ValueTask<Turn> EnterAsync(string name, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        TaskCompletionSource waiter;
        Group group;
        lock (_gate)
        {
            if (!_groups.TryGetValue(name, out group!))
            {
                group = new Group();
                _groups[name] = group;
            }

            if (!group.Busy)
            {
                group.Busy = true;
                return ValueTask.FromResult(new Turn(this, name));
            }

            waiter = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            group.Waiting.AddLast(waiter);
        }

        return new ValueTask<Turn>(WaitAsync(name, group, waiter, cancellationToken));
    }

    private async Task<Turn> WaitAsync(string name, Group group, TaskCompletionSource waiter, CancellationToken cancellationToken)
    {
        using (cancellationToken.Register(() => Leave(group, waiter, cancellationToken)))
        {
            await waiter.Task;
        }

        return new Turn(this, name);
    }

    /// <summary>A waiting call was cancelled: it leaves the queue, unless its turn has just come, which it then passes on.</summary>
    private void Leave(Group group, TaskCompletionSource waiter, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (!group.Waiting.Remove(waiter))
            {
                return;
            }
        }

        waiter.TrySetCanceled(cancellationToken);
    }

    private void Release(string name)
    {
        TaskCompletionSource? next = null;
        lock (_gate)
        {
            var group = _groups[name];
            if (group.Waiting.First is { } first)
            {
                group.Waiting.RemoveFirst();
                next = first.Value;
            }
            else
            {
                group.Busy = false;
                _groups.Remove(name);
            }
        }

        // The group stays busy: the turn passes straight to the next call in arrival order.
        next?.TrySetResult();
    }

    /// <summary>A call's turn in its group, given back when the command has ended.</summary>
    public readonly struct Turn(SerialGroups groups, string name) : IDisposable
    {
        public void Dispose() => groups.Release(name);
    }

    private sealed class Group
    {
        public bool Busy { get; set; }

        public LinkedList<TaskCompletionSource> Waiting { get; } = [];
    }
}
