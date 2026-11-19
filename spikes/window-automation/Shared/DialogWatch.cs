namespace Spar.WindowAutomation;

/// <summary>Watches for a native dialog of this process or the browser (never touched beyond WM_CLOSE).</summary>
internal sealed class DialogWatch
{
    private readonly CancellationTokenSource _stop = new();
    private readonly Task<int> _task;

    private DialogWatch(View view)
    {
        var ids = new HashSet<int> { Environment.ProcessId, (int)view.BrowserProcessId };
        _task = Task.Run(async () =>
        {
            var seen = new HashSet<nint>();
            while (!_stop.IsCancellationRequested)
            {
                foreach (var window in Native.VisibleWindowsOf(ids).Where(w => w.ClassName == "#32770"))
                {
                    if (seen.Add(window.Handle))
                    {
                        Log.Write("DIALOG", $"class={window.ClassName} title=\"{window.Title}\" pid={window.ProcessId}; closing with WM_CLOSE");
                        Native.PostMessageW(window.Handle, Native.WmClose, 0, 0);
                    }
                }

                await Task.Delay(20);
            }

            return seen.Count;
        });
    }

    public static DialogWatch Start(View view) => new(view);

    public int Stop()
    {
        _stop.Cancel();
        return _task.GetAwaiter().GetResult();
    }
}

/// <summary>Records any new visible top-level window of this process or the browser while it runs (looked at, never touched).</summary>
internal sealed class WindowWatch
{
    private readonly CancellationTokenSource _stop = new();
    private readonly Task<List<string>> _task;

    private WindowWatch(View view)
    {
        var ids = new HashSet<int> { Environment.ProcessId, (int)view.BrowserProcessId };
        var before = Native.VisibleWindowsOf(ids).Select(w => w.Handle).ToHashSet();
        _task = Task.Run(async () =>
        {
            var seen = new Dictionary<nint, string>();
            while (!_stop.IsCancellationRequested)
            {
                foreach (var window in Native.VisibleWindowsOf(ids).Where(w => !before.Contains(w.Handle) && !seen.ContainsKey(w.Handle)))
                {
                    Native.GetWindowRect(window.Handle, out var r);
                    seen[window.Handle] = $"class={window.ClassName} title=\"{window.Title}\" pid={window.ProcessId} rect=({r.Left},{r.Top},{r.Right},{r.Bottom})";
                }

                await Task.Delay(20);
            }

            return seen.Values.ToList();
        });
    }

    public static WindowWatch Start(View view) => new(view);

    public string Stop()
    {
        _stop.Cancel();
        return string.Join("; ", _task.GetAwaiter().GetResult());
    }
}
