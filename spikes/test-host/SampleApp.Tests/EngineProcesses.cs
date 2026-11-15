using System.Runtime.InteropServices;

namespace SampleApp.Tests;

/// <summary>Web engine processes below this test process (msedgewebview2 on Windows, WebKit*Process on Linux).</summary>
internal static partial class EngineProcesses
{
    public static IReadOnlyList<(int Id, string Name)> Descendants()
    {
        var all = OperatingSystem.IsWindows() ? WindowsProcesses() : LinuxProcesses();
        var found = new List<(int, string)>();
        var parents = new HashSet<int> { Environment.ProcessId };
        bool added;
        do
        {
            added = false;
            foreach (var (id, parent, name) in all)
            {
                if (parents.Contains(parent) && parents.Add(id))
                {
                    found.Add((id, name));
                    added = true;
                }
            }
        }
        while (added);

        return found.Where(p => p.Item2.Contains("webview", StringComparison.OrdinalIgnoreCase) || p.Item2.StartsWith("WebKit", StringComparison.Ordinal)).ToList();
    }

    public static async Task<IReadOnlyList<(int Id, string Name)>> WaitUntilNoneAsync(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            var left = Descendants();
            if (left.Count == 0 || DateTime.UtcNow > deadline)
            {
                return left;
            }

            await Task.Delay(100);
        }
    }

    private static List<(int Id, int Parent, string Name)> LinuxProcesses()
    {
        var result = new List<(int, int, string)>();
        foreach (var directory in Directory.EnumerateDirectories("/proc"))
        {
            if (!int.TryParse(Path.GetFileName(directory), out var id))
            {
                continue;
            }

            try
            {
                // /proc/<pid>/stat: "pid (comm) state ppid ..."; comm may contain spaces, so split after the last ')'.
                var stat = File.ReadAllText(Path.Combine(directory, "stat"));
                var close = stat.LastIndexOf(')');
                var name = stat[(stat.IndexOf('(', StringComparison.Ordinal) + 1)..close];
                var parent = int.Parse(stat[(close + 2)..].Split(' ')[1], System.Globalization.CultureInfo.InvariantCulture);
                result.Add((id, parent, name));
            }
            catch (IOException)
            {
                // The process ended while we read it.
            }
        }

        return result;
    }

    private static unsafe List<(int Id, int Parent, string Name)> WindowsProcesses()
    {
        var result = new List<(int, int, string)>();
        var snapshot = CreateToolhelp32Snapshot(2, 0);
        try
        {
            var entry = new ProcessEntry { Size = (uint)sizeof(ProcessEntry) };
            for (var more = Process32FirstW(snapshot, ref entry); more != 0; more = Process32NextW(snapshot, ref entry))
            {
                result.Add(((int)entry.ProcessId, (int)entry.ParentProcessId, new string(entry.ExeFile)));
            }
        }
        finally
        {
            CloseHandle(snapshot);
        }

        return result;
    }

    [LibraryImport("kernel32")]
    private static partial nint CreateToolhelp32Snapshot(uint flags, uint processId);

    [LibraryImport("kernel32")]
    private static partial int Process32FirstW(nint snapshot, ref ProcessEntry entry);

    [LibraryImport("kernel32")]
    private static partial int Process32NextW(nint snapshot, ref ProcessEntry entry);

    [LibraryImport("kernel32")]
    private static partial int CloseHandle(nint handle);

    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct ProcessEntry
    {
        public uint Size;
        public uint Usage;
        public uint ProcessId;
        public nuint DefaultHeapId;
        public uint ModuleId;
        public uint Threads;
        public uint ParentProcessId;
        public int PriorityClassBase;
        public uint Flags;
        public fixed char ExeFile[260];
    }
}
