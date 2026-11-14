using System.Diagnostics;
using Spar.ResourceUsage.Interop;

namespace Spar.ResourceUsage;

/// <summary>
/// The browser's process list, refreshed on the STA (GetProcessInfos is an STA call) when ProcessInfosChanged fires,
/// with one open handle per process. Any thread can read the memory of the current snapshot; holding the handle keeps
/// the process id from being reused.
/// </summary>
internal sealed class EngineProcesses : IDisposable
{
    private readonly ICoreWebView2Environment8 _environment;
    private Entry[] _snapshot = [];

    public EngineProcesses(ICoreWebView2Environment8 environment) => _environment = environment;

    public readonly record struct Entry(int Pid, int Kind, nint Handle);

    public static string KindName(int kind) => kind switch
    {
        0 => "browser",
        1 => "renderer",
        2 => "utility",
        3 => "sandbox-helper",
        4 => "gpu",
        5 => "ppapi-plugin",
        6 => "ppapi-broker",
        _ => "kind" + kind,
    };

    public Entry[] Snapshot => Volatile.Read(ref _snapshot);

    public int Refreshes { get; private set; }

    /// <summary>The process list from GetProcessInfos. Must run on the STA that created the environment.</summary>
    public static List<(int Pid, int Kind)> List(ICoreWebView2Environment8 environment)
    {
        var hr = environment.GetProcessInfos(out var collection);
        if (hr < 0 || collection is null)
        {
            throw new ComFailureException("GetProcessInfos", hr);
        }

        collection.GetCount(out var count);
        var list = new List<(int, int)>((int)count);
        for (uint i = 0; i < count; i++)
        {
            collection.GetValueAtIndex(i, out var info);
            info!.GetProcessId(out var pid);
            info.GetKind(out var kind);
            list.Add((pid, kind));
        }

        return list;
    }

    /// <summary>Re-reads the list on the STA and opens handles for new processes; closes those of vanished ones.</summary>
    public void Refresh()
    {
        var started = Stopwatch.GetTimestamp();
        var listed = List(_environment);
        var old = _snapshot;
        var next = new Entry[listed.Count];
        var count = 0;
        foreach (var (pid, kind) in listed)
        {
            var existing = Array.FindIndex(old, e => e.Pid == pid);
            if (existing >= 0)
            {
                next[count++] = old[existing];
                continue;
            }

            var handle = Win32.OpenProcess(Win32.ProcessQueryLimitedInformation, 0, (uint)pid);
            if (handle != 0)
            {
                next[count++] = new Entry(pid, kind, handle);
            }
        }

        Array.Resize(ref next, count);
        Volatile.Write(ref _snapshot, next);
        foreach (var entry in old)
        {
            if (Array.FindIndex(next, e => e.Pid == entry.Pid) < 0)
            {
                Win32.CloseHandle(entry.Handle);
            }
        }

        Refreshes++;
        Log.Line("ENGINE-REFRESH", $"processes={count} ms={Log.Ms(started):F2}");
    }

    /// <summary>Sums the current snapshot's counters. Runs on any thread; allocates nothing.</summary>
    public MemoryReading Read()
    {
        long privateWorkingSet = 0, workingSet = 0;
        var processes = 0;
        foreach (var entry in Snapshot)
        {
            if (Win32.Counters(entry.Handle) is { } counters)
            {
                privateWorkingSet += (long)counters.PrivateWorkingSetSize;
                workingSet += (long)counters.WorkingSetSize;
                processes++;
            }
        }

        return new MemoryReading(privateWorkingSet, workingSet, processes, Stopwatch.GetTimestamp());
    }

    public void Dispose()
    {
        foreach (var entry in Interlocked.Exchange(ref _snapshot, []))
        {
            Win32.CloseHandle(entry.Handle);
        }
    }
}
