using System.Diagnostics;
using System.Globalization;
using Spar.AssetPacking;

// Usage: Pack.Bench <startup|eager|lazy|decode> [repeat]
//   startup  open + decode everything, then exit (time the whole process from outside)
//   eager    open, decode everything, report time and memory
//   lazy     open, then request every asset twice (decode + cache hit), report time and memory
//   decode   per asset: the first decode, then the median of <repeat> uncached decodes
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
var mode = args.Length > 0 ? args[0] : "eager";
var repeat = args.Length > 1 ? int.Parse(args[1], CultureInfo.InvariantCulture) : 30;
var clock = Stopwatch.StartNew();

var assets = FrontendAssets.Open(typeof(FrontendAssets).Assembly);
var openMs = clock.Elapsed.TotalMilliseconds;
var entries = assets.Entries.OrderBy(entry => entry.Path, StringComparer.Ordinal).ToList();
long original = entries.Sum(entry => (long)entry.OriginalLength);
long stored = entries.Sum(entry => (long)entry.StoredLength);

switch (mode)
{
    case "startup":
        assets.DecodeAll();
        return 0;

    case "eager":
        {
            var start = Stopwatch.GetTimestamp();
            assets.DecodeAll();
            var decodeMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            Console.WriteLine($"BENCH format={FrontendAssets.Format} mode=eager files={entries.Count} original={original} stored={stored} open-ms={openMs:F3} decode-all-ms={decodeMs:F3} in-place={assets.InPlace} cached={assets.CachedBytes} {Memory()}");
            break;
        }

    case "lazy":
        {
            double first = 0, second = 0;
            foreach (var entry in entries)
            {
                var start = Stopwatch.GetTimestamp();
                assets.TryGet(entry.Path, out _, out var content);
                first += Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                start = Stopwatch.GetTimestamp();
                assets.TryGet(entry.Path, out _, out var again);
                second += Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                if (content.Length != entry.OriginalLength || again.Length != entry.OriginalLength)
                {
                    throw new InvalidOperationException($"Wrong length for {entry.Path}.");
                }
            }

            Console.WriteLine($"BENCH format={FrontendAssets.Format} mode=lazy files={entries.Count} original={original} stored={stored} open-ms={openMs:F3} first-requests-ms={first:F3} cached-requests-ms={second:F3} cached={assets.CachedBytes} {Memory()}");
            break;
        }

    case "decode":
        {
            Console.WriteLine($"BENCH format={FrontendAssets.Format} mode=decode files={entries.Count} original={original} stored={stored} open-ms={openMs:F3}");
            double totalMedian = 0;
            foreach (var entry in entries)
            {
                var start = Stopwatch.GetTimestamp();
                var bytes = assets.Decode(entry);
                var firstMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                var samples = new double[repeat];
                for (var i = 0; i < repeat; i++)
                {
                    start = Stopwatch.GetTimestamp();
                    bytes = assets.Decode(entry);
                    samples[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                }

                Array.Sort(samples);
                var median = samples[repeat / 2];
                totalMedian += median;
                Console.WriteLine($"ASSET {entry.Path} enc={entry.Encoding} original={entry.OriginalLength} stored={entry.StoredLength} first-ms={firstMs:F3} median-ms={median:F3} MBps={entry.OriginalLength / 1e6 / (median / 1000):F0} ok={bytes.Length == entry.OriginalLength}");
            }

            Console.WriteLine($"BENCH format={FrontendAssets.Format} total-median-ms={totalMedian:F3}");
            break;
        }

    default:
        Console.Error.WriteLine("Unknown mode " + mode);
        return 2;
}

return 0;

static string Memory()
{
    using var self = Process.GetCurrentProcess();
    var hwm = OperatingSystem.IsLinux() ? LinuxStatus("VmHWM:") : self.PeakWorkingSet64 / 1024;
    return $"peak-ws-kb={hwm} ws-kb={self.WorkingSet64 / 1024} private-kb={self.PrivateMemorySize64 / 1024} " +
        $"allocated-kb={GC.GetTotalAllocatedBytes(precise: true) / 1024} heap-kb={GC.GetGCMemoryInfo().HeapSizeBytes / 1024}";
}

static long LinuxStatus(string key)
{
    foreach (var line in File.ReadLines("/proc/self/status"))
    {
        if (line.StartsWith(key, StringComparison.Ordinal))
        {
            return long.Parse(line[key.Length..].Trim().Split(' ')[0], CultureInfo.InvariantCulture);
        }
    }

    return -1;
}
