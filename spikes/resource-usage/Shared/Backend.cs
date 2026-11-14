using System.Diagnostics;

namespace Spar.ResourceUsage;

/// <summary>The backend's own memory: every candidate .NET counter, its value and the cost of one read.</summary>
internal static class Backend
{
    /// <summary>Logs each candidate's value (MB) and cost. <paramref name="osCounters"/> adds the OS-specific reads.</summary>
    public static void Report(string label, int count, IEnumerable<(string Name, Func<long> Read)> osCounters)
    {
        Values(label, osCounters);
        GC.Collect();
        Values(label + "-after-gc", osCounters);
        long sink = 0;
        Log.Line("BACKEND-COST", $"label={label} read=Environment.WorkingSet {Stats.Batched(count, () => sink += Environment.WorkingSet)}");
        Log.Line("BACKEND-COST", $"label={label} read=GC.GetGCMemoryInfo {Stats.Batched(count, () => sink += GC.GetGCMemoryInfo().TotalCommittedBytes)}");
        Log.Line("BACKEND-COST", $"label={label} read=GC.GetTotalMemory(false) {Stats.Batched(count, () => sink += GC.GetTotalMemory(false))}");
        Log.Line("BACKEND-COST", $"label={label} read=GC.GetTotalAllocatedBytes {Stats.Batched(count, () => sink += GC.GetTotalAllocatedBytes(false))}");
        Log.Line("BACKEND-COST", $"label={label} read=Process.GetCurrentProcess().PrivateMemorySize64 {Stats.Time(200, () =>
        {
            using var process = Process.GetCurrentProcess();
            sink += process.PrivateMemorySize64;
        }, "us")}");
        using var cached = Process.GetCurrentProcess();
        Log.Line("BACKEND-COST", $"label={label} read=cachedProcess.Refresh+PrivateMemorySize64 {Stats.Time(200, () =>
        {
            cached.Refresh();
            sink += cached.PrivateMemorySize64;
        }, "us")}");
        foreach (var (name, read) in osCounters)
        {
            Log.Line("BACKEND-COST", $"label={label} read={name} {Stats.Batched(count, () => sink += read())}");
        }

        GC.KeepAlive(sink);
    }

    private static void Values(string label, IEnumerable<(string Name, Func<long> Read)> osCounters)
    {
        var info = GC.GetGCMemoryInfo();
        using var self = Process.GetCurrentProcess();
        Log.Line("BACKEND-VALUES",
            $"label={label} workingSetMB={Log.Mb(Environment.WorkingSet)} gcHeapMB={Log.Mb(info.HeapSizeBytes)} gcCommittedMB={Log.Mb(info.TotalCommittedBytes)} " +
            $"gcIndex={info.Index} gcTotalMemoryMB={Log.Mb(GC.GetTotalMemory(false))} gcFragmentedMB={Log.Mb(info.FragmentedBytes)} totalAllocatedMB={Log.Mb(GC.GetTotalAllocatedBytes(false))} " +
            $"processPrivateMB={Log.Mb(self.PrivateMemorySize64)} processWorkingSetMB={Log.Mb(self.WorkingSet64)} " +
            string.Join(' ', osCounters.Select(c => $"{c.Name}MB={Log.Mb(c.Read())}")));
    }
}
