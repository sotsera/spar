namespace Spar.Linux.Testing;

/// <summary>
/// The test process's one colour scheme at a time (loop thread; <see cref="TestWindowProcess.EnterSchemeAsync"/>): GTK's
/// dark preference, which WebKitGTK's <c>prefers-color-scheme</c> follows, is the default display's, so the process's
/// windows show one scheme at a time (measured: a view on a second display with its own dark preference stayed light).
/// A window of the shown scheme enters while none waits; a window of the other scheme waits until the open ones closed,
/// and windows asking after it wait behind it, so neither scheme waits for ever. One exception keeps a test from
/// waiting for itself: a window whose holder (the test that opens it, or its app) already has a window of the shown
/// scheme open enters at once, ahead of the queue. Without it, a test that holds a light window and opens a second one
/// while a dark window is queued waited for the dark window, which waited for the test's first window to close (found
/// in the CI container: window tests timing out at 30 s in about half the runs).
/// </summary>
internal sealed class SchemeGate(Action<bool> apply)
{
    private readonly Queue<(bool Dark, object Holder, TaskCompletionSource Turn)> _waiting = new();
    private readonly Dictionary<object, int> _openBy = [];
    private int _open;
    private bool _dark;
    private bool? _applied;

    /// <summary>How many windows are counted in.</summary>
    public int Open => _open;

    /// <summary>How many windows wait for their scheme's turn.</summary>
    public int Waiting => _waiting.Count;

    /// <summary>Completes when a window of <paramref name="dark"/>'s scheme, held by <paramref name="holder"/>, may open, and counts it in.</summary>
    public Task EnterAsync(bool dark, object holder)
    {
        var shown = _open > 0 && _dark == dark;
        if ((_waiting.Count == 0 && (_open == 0 || shown)) || (shown && _openBy.GetValueOrDefault(holder) > 0))
        {
            Use(dark, holder);
            return Task.CompletedTask;
        }

        var turn = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _waiting.Enqueue((dark, holder, turn));
        return turn.Task;
    }

    /// <summary>A window counted in by <see cref="EnterAsync"/> is gone: the next ones whose scheme may show now enter.</summary>
    public void Leave(object holder)
    {
        _open--;
        if (_openBy.TryGetValue(holder, out var count))
        {
            if (count <= 1)
            {
                _openBy.Remove(holder);
            }
            else
            {
                _openBy[holder] = count - 1;
            }
        }

        while (_waiting.TryPeek(out var next) && (_open == 0 || _dark == next.Dark))
        {
            _waiting.Dequeue();
            Use(next.Dark, next.Holder);
            next.Turn.TrySetResult();
        }
    }

    private void Use(bool dark, object holder)
    {
        _open++;
        _openBy[holder] = _openBy.GetValueOrDefault(holder) + 1;
        _dark = dark;
        if (_applied != dark)
        {
            _applied = dark;
            apply(dark);
        }
    }
}
