using System.Globalization;
using System.Text;

namespace Spar.ResourceUsage;

/// <summary>
/// /proc readers for the app process and its descendants. Only reads; nothing here signals a process. smaps_rollup
/// parsing follows spikes/linux-memory (PSS from smaps_rollup; USS = Private_Clean + Private_Dirty).
/// </summary>
internal static class ProcMem
{
    [ThreadStatic]
    private static byte[]? t_buffer;

    /// <summary>smaps_rollup fields, in kB.</summary>
    public readonly record struct Rollup(long Rss, long Pss, long PssAnon, long PssFile, long PssShmem, long SharedClean, long SharedDirty, long PrivateClean, long PrivateDirty, long Swap, long SwapPss)
    {
        public long Uss => PrivateClean + PrivateDirty;
    }

    public readonly record struct Child(int Pid, int Parent, string Name);

    private static ReadOnlySpan<byte> ReadAll(string path)
    {
        var buffer = t_buffer ??= new byte[16384];
        using var handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read);
        var total = 0;
        while (total < buffer.Length)
        {
            var read = RandomAccess.Read(handle, buffer.AsSpan(total), total);
            if (read == 0)
            {
                break;
            }

            total += read;
        }

        return buffer.AsSpan(0, total);
    }

    /// <summary>Parses "Key:   123 kB" lines; returns the value of each wanted key in kB.</summary>
    public static Rollup? ReadRollup(int pid)
    {
        try
        {
            var text = ReadAll(pid == 0 ? "/proc/self/smaps_rollup" : $"/proc/{pid}/smaps_rollup");
            long rss = 0, pss = 0, pssAnon = 0, pssFile = 0, pssShmem = 0, sharedClean = 0, sharedDirty = 0, privateClean = 0, privateDirty = 0, swap = 0, swapPss = 0;
            foreach (var range in text.Split((byte)'\n'))
            {
                var line = text[range];
                var colon = line.IndexOf((byte)':');
                if (colon <= 0)
                {
                    continue;
                }

                var key = line[..colon];
                var value = Number(line[(colon + 1)..]);
                if (key.SequenceEqual("Rss"u8)) rss = value;
                else if (key.SequenceEqual("Pss"u8)) pss = value;
                else if (key.SequenceEqual("Pss_Anon"u8)) pssAnon = value;
                else if (key.SequenceEqual("Pss_File"u8)) pssFile = value;
                else if (key.SequenceEqual("Pss_Shmem"u8)) pssShmem = value;
                else if (key.SequenceEqual("Shared_Clean"u8)) sharedClean = value;
                else if (key.SequenceEqual("Shared_Dirty"u8)) sharedDirty = value;
                else if (key.SequenceEqual("Private_Clean"u8)) privateClean = value;
                else if (key.SequenceEqual("Private_Dirty"u8)) privateDirty = value;
                else if (key.SequenceEqual("Swap"u8)) swap = value;
                else if (key.SequenceEqual("SwapPss"u8)) swapPss = value;
            }

            return new Rollup(rss, pss, pssAnon, pssFile, pssShmem, sharedClean, sharedDirty, privateClean, privateDirty, swap, swapPss);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>statm: resident and shared pages, in kB (4 kB pages).</summary>
    public static (long ResidentKb, long SharedKb)? ReadStatm(int pid)
    {
        try
        {
            var text = ReadAll($"/proc/{pid}/statm");
            Span<long> fields = stackalloc long[3];
            var index = 0;
            foreach (var range in text.Split((byte)' '))
            {
                if (index >= 3)
                {
                    break;
                }

                fields[index++] = Number(text[range]);
            }

            return (fields[1] * 4, fields[2] * 4);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>The NSpid line of /proc/PID/status: more than one number means a nested PID namespace (bwrap).</summary>
    public static string NsPid(int pid)
    {
        try
        {
            foreach (var line in File.ReadLines($"/proc/{pid}/status"))
            {
                if (line.StartsWith("NSpid:", StringComparison.Ordinal))
                {
                    return string.Join('/', line[6..].Split((char[])['\t', ' '], StringSplitOptions.RemoveEmptyEntries));
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return "error:" + exception.GetType().Name;
        }

        return "-";
    }

    private static long Number(ReadOnlySpan<byte> text)
    {
        long value = 0;
        var seen = false;
        foreach (var b in text)
        {
            if (b is >= (byte)'0' and <= (byte)'9')
            {
                value = value * 10 + (b - '0');
                seen = true;
            }
            else if (seen)
            {
                break;
            }
        }

        return value;
    }

    /// <summary>Descendants by scanning every /proc/PID/stat (works on any kernel; cost grows with all processes).</summary>
    public static List<Child> DescendantsByScan(int root)
    {
        var table = new List<(int Pid, int Parent, string Name)>();
        foreach (var directory in Directory.EnumerateDirectories("/proc"))
        {
            if (!int.TryParse(Path.GetFileName(directory.AsSpan()), NumberStyles.None, CultureInfo.InvariantCulture, out var pid))
            {
                continue;
            }

            try
            {
                var stat = Encoding.ASCII.GetString(ReadAll($"/proc/{pid}/stat"));
                var open = stat.IndexOf('(', StringComparison.Ordinal);
                var close = stat.LastIndexOf(')');
                var rest = stat[(close + 2)..].Split(' ');
                if (rest[0] != "Z")
                {
                    table.Add((pid, int.Parse(rest[1], CultureInfo.InvariantCulture), stat[(open + 1)..close]));
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or FormatException)
            {
            }
        }

        var found = new List<Child>();
        var frontier = new Queue<int>([root]);
        while (frontier.TryDequeue(out var parent))
        {
            foreach (var (pid, parentPid, name) in table)
            {
                if (parentPid == parent)
                {
                    found.Add(new Child(pid, parentPid, name));
                    frontier.Enqueue(pid);
                }
            }
        }

        return found;
    }

    /// <summary>
    /// Descendants through /proc/PID/task/TID/children (each thread lists the children it forked), recursively. Reads
    /// only the app's own tree.
    /// </summary>
    public static List<Child> DescendantsByChildren(int root)
    {
        var found = new List<Child>();
        var frontier = new Queue<int>([root]);
        while (frontier.TryDequeue(out var parent))
        {
            IEnumerable<string> tasks;
            try
            {
                tasks = Directory.EnumerateDirectories($"/proc/{parent}/task");
                foreach (var task in tasks)
                {
                    ReadOnlySpan<byte> text;
                    try
                    {
                        text = ReadAll(task + "/children");
                    }
                    catch (IOException)
                    {
                        continue;
                    }

                    foreach (var range in text.Split((byte)' '))
                    {
                        var number = text[range];
                        if (number.Length == 0 || number[0] is (byte)'\n')
                        {
                            continue;
                        }

                        var pid = (int)Number(number);
                        found.Add(new Child(pid, parent, ""));
                        frontier.Enqueue(pid);
                    }
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
        }

        return found;
    }

    public static string Name(int pid)
    {
        try
        {
            return File.ReadAllText($"/proc/{pid}/comm").Trim();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return "?";
        }
    }
}

/// <summary>
/// The engine's processes with one open /proc/PID/statm and smaps_rollup handle each, re-read with pread at offset 0
/// (seq_file regenerates the text on each read from 0). Refresh finds the tree again; Read allocates nothing.
/// </summary>
internal sealed class LinuxEngine(int root) : IDisposable
{
    private readonly byte[] _buffer = new byte[4096];
    private (int Pid, Microsoft.Win32.SafeHandles.SafeFileHandle Statm, Microsoft.Win32.SafeHandles.SafeFileHandle Rollup)[] _entries = [];

    public int Count => _entries.Length;

    public void Refresh()
    {
        Dispose();
        var children = ProcMem.DescendantsByChildren(root);
        var entries = new List<(int, Microsoft.Win32.SafeHandles.SafeFileHandle, Microsoft.Win32.SafeHandles.SafeFileHandle)>(children.Count);
        foreach (var child in children)
        {
            try
            {
                entries.Add((child.Pid, File.OpenHandle($"/proc/{child.Pid}/statm"), File.OpenHandle($"/proc/{child.Pid}/smaps_rollup")));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
        }

        _entries = [.. entries];
    }

    /// <summary>Anonymous resident memory (statm resident - shared) and resident memory, summed, in bytes.</summary>
    public MemoryReading ReadStatm()
    {
        long anonymous = 0, resident = 0;
        var count = 0;
        foreach (var (_, statm, _) in _entries)
        {
            try
            {
                var read = RandomAccess.Read(statm, _buffer, 0);
                var text = _buffer.AsSpan(0, read);
                var first = text.IndexOf((byte)' ');
                var rest = text[(first + 1)..];
                var second = rest.IndexOf((byte)' ');
                var residentPages = Parse(rest[..second]);
                var sharedPages = Parse(rest[(second + 1)..]);
                anonymous += (residentPages - sharedPages) * 4096;
                resident += residentPages * 4096;
                count++;
            }
            catch (IOException)
            {
                // The process exited; the next refresh drops it.
            }
        }

        return new MemoryReading(anonymous, resident, count, System.Diagnostics.Stopwatch.GetTimestamp());
    }

    /// <summary>PSS and Private_Dirty summed, in bytes, from the kept smaps_rollup handles.</summary>
    public (long Pss, long PrivateDirty) ReadRollup()
    {
        long pss = 0, privateDirty = 0;
        foreach (var (_, _, rollup) in _entries)
        {
            try
            {
                var read = RandomAccess.Read(rollup, _buffer, 0);
                var text = _buffer.AsSpan(0, read);
                pss += Field(text, "\nPss:"u8) * 1024;
                privateDirty += Field(text, "\nPrivate_Dirty:"u8) * 1024;
            }
            catch (IOException)
            {
            }
        }

        return (pss, privateDirty);
    }

    private static long Field(ReadOnlySpan<byte> text, ReadOnlySpan<byte> key)
    {
        var index = text.IndexOf(key);
        return index < 0 ? 0 : Parse(text[(index + key.Length)..]);
    }

    private static long Parse(ReadOnlySpan<byte> text)
    {
        long value = 0;
        var seen = false;
        foreach (var b in text)
        {
            if (b is >= (byte)'0' and <= (byte)'9')
            {
                value = value * 10 + (b - '0');
                seen = true;
            }
            else if (seen)
            {
                break;
            }
        }

        return value;
    }

    public void Dispose()
    {
        foreach (var (_, statm, rollup) in _entries)
        {
            statm.Dispose();
            rollup.Dispose();
        }

        _entries = [];
    }
}
