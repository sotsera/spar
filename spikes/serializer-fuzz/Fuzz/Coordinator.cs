using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace Fuzz;

/// <summary>
/// Splits the case range into batches, runs each batch in a child worker process, and turns a worker that dies or
/// goes silent into a CRASH or HANG incident for the case after the last one it finished. The remaining cases of
/// that batch are rerun in new workers. Then it minimises representatives and writes a Markdown report.
/// </summary>
internal sealed class Coordinator
{
    private static readonly string[] ClassOrder = ["EQ", "EQ-src", "NE", "RJ", "XR-YA", "XA-YR", "X-nonjson", "X-unwritable", "PASS", "PASS-both-throw", "FAIL", "SKIPPED"];

    private readonly Stats _stats = new();
    private readonly List<string> _incidents = [];
    private readonly Options _options;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private long _casesRun;

    private Coordinator(Options options) => _options = options;

    public static async Task<int> RunAsync(string[] args)
    {
        var coordinator = new Coordinator(Options.Parse(args));
        await coordinator.RunAllAsync();
        return 0;
    }

    private async Task RunAllAsync()
    {
        var o = _options;
        var batches = new Queue<(long From, long To)>();
        for (var from = 0L; from < o.Cases; from += o.Batch)
        {
            batches.Enqueue((from, Math.Min(o.Cases, from + o.Batch)));
        }

        var workers = Enumerable.Range(0, o.Workers).Select(_ => Task.Run(async () =>
        {
            while (_clock.Elapsed < o.Budget)
            {
                (long From, long To) batch;
                lock (batches)
                {
                    if (!batches.TryDequeue(out batch))
                    {
                        return;
                    }
                }

                await RunRangeAsync(batch.From, batch.To);
                Console.Error.Write(FormattableString.Invariant($"\r{Interlocked.Read(ref _casesRun):N0} cases, {_clock.Elapsed.TotalSeconds:F0} s   "));
            }
        })).ToArray();
        await Task.WhenAll(workers);
        var fuzzTime = _clock.Elapsed;
        Console.Error.WriteLine();

        var minimised = await MinimiseAsync();
        var described = await DescribeAsync();
        var report = Report(fuzzTime, minimised, described);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(o.Out))!);
        await File.WriteAllTextAsync(o.Out, report);
        await File.WriteAllLinesAsync(
            Path.ChangeExtension(o.Out, ".groups.tsv"),
            _stats.Groups.OrderBy(g => g.Key.Cmp, StringComparer.Ordinal).ThenBy(g => g.Key.Class, StringComparer.Ordinal)
                .ThenByDescending(g => g.Value.Count)
                .Select(g => FormattableString.Invariant($"{g.Key.Cmp}\t{g.Key.Class}\t{g.Value.Count}\t{g.Value.RepId}\t{g.Key.Key}")));
        Console.WriteLine(report);
    }

    private async Task RunRangeAsync(long from, long to)
    {
        if (from >= to)
        {
            return;
        }

        var args = new List<string> { "worker", _options.Seed.ToString(CultureInfo.InvariantCulture), Str(from), Str(to) };
        if (_options.InjectCrash >= 0)
        {
            args.AddRange(["--inject-crash", Str(_options.InjectCrash)]);
        }

        if (_options.InjectHang >= 0)
        {
            args.AddRange(["--inject-hang", Str(_options.InjectHang)]);
        }

        var (lines, last, done, exitCode, stderr, hung) = await RunChildAsync(args, TimeSpan.FromSeconds(60), from);
        if (done)
        {
            lock (_stats)
            {
                foreach (var line in lines)
                {
                    _stats.Merge(line);
                }
            }

            Interlocked.Add(ref _casesRun, to - from);
            return;
        }

        var failed = last + 1;
        var c = Cases.Create(_options.Seed, failed);
        var firstError = stderr.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? string.Empty;
        lock (_stats)
        {
            _incidents.Add(FormattableString.Invariant(
                $"| {(hung ? "HANG" : "CRASH")} | {failed} | {c.Stream} | {c.Target.Name} | {c.Ops} | {(hung ? "killed after 60 s without progress" : "exit 0x" + exitCode.ToString("X8", CultureInfo.InvariantCulture))} | {firstError.Replace('|', '/')} |"));
            _stats.Add("ALL", c.Stream, hung ? "HANG" : "CRASH", c.Ops, failed, c.Input?.Length ?? 0);
        }

        Interlocked.Increment(ref _casesRun);
        await RunRangeAsync(from, failed);
        await RunRangeAsync(failed + 1, to);
    }

    private static async Task<(List<string> Lines, long Last, bool Done, int ExitCode, string Stderr, bool Hung)> RunChildAsync(
        List<string> args, TimeSpan silence, long first = 0)
    {
        var psi = new ProcessStartInfo
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardOutputEncoding = new UTF8Encoding(false),
        };
        var self = Environment.ProcessPath!;
        if (Path.GetFileNameWithoutExtension(self).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
        {
            psi.FileName = self;
            psi.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "Fuzz.dll"));
        }
        else
        {
            psi.FileName = self;
        }

        foreach (var arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        using var process = Process.Start(psi)!;
        var stderr = new StringBuilder();
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                lock (stderr)
                {
                    stderr.AppendLine(e.Data);
                }
            }
        };
        process.BeginErrorReadLine();

        var lines = new List<string>();
        var last = first - 1;
        var done = false;
        var hung = false;
        while (true)
        {
            string? line;
            try
            {
                line = await process.StandardOutput.ReadLineAsync().WaitAsync(silence);
            }
            catch (TimeoutException)
            {
                hung = true;
                process.Kill(entireProcessTree: true);
                break;
            }

            if (line is null)
            {
                break;
            }

            if (line.StartsWith("P ", StringComparison.Ordinal))
            {
                last = long.Parse(line.AsSpan(2), CultureInfo.InvariantCulture);
            }
            else if (line == "DONE")
            {
                done = true;
            }
            else
            {
                lines.Add(line);
            }
        }

        await process.WaitForExitAsync();
        lock (stderr)
        {
            return (lines, last, done && process.ExitCode == 0, process.ExitCode, stderr.ToString(), hung);
        }
    }

    private List<(string Cmp, string Class, string Key, Group Group)> Representatives()
    {
        var picked = new List<(string, string, string, Group)>();
        foreach (var cmp in new[] { "A~B", "F~B", "A~S", "F~S" })
        {
            foreach (var cls in new[] { "NE", "EQ-src", "XR-YA", "XA-YR", "X-nonjson", "X-unwritable", "RJ" })
            {
                var perClass = cmp is "A~B" or "F~B" ? (cls == "RJ" ? 4 : 14) : (cls == "RJ" ? 0 : 8);
                picked.AddRange(_stats.Groups
                    .Where(g => g.Key.Cmp == cmp && g.Key.Class == cls)
                    .OrderByDescending(g => g.Value.Count)
                    .Take(perClass)
                    .Select(g => (cmp, cls, g.Key.Key, g.Value)));
            }
        }

        return picked;
    }

    private async Task<Dictionary<(string Cmp, long Id), (int Original, byte[] Minimal)>> MinimiseAsync()
    {
        var result = new Dictionary<(string, long), (int, byte[])>();
        if (!_options.Minimise)
        {
            return result;
        }

        var jobs = Representatives()
            .Where(r => r.Class != "EQ-src")
            .GroupBy(r => r.Cmp)
            .SelectMany(g => g.Select(r => r.Group.RepId).Distinct().Chunk(6).Select(ids => (Cmp: g.Key, Ids: ids)))
            .ToList();
        var gate = new SemaphoreSlim(_options.Workers);
        await Task.WhenAll(jobs.Select(async job =>
        {
            await gate.WaitAsync();
            try
            {
                var args = new List<string> { "minimise", Str((long)_options.Seed), job.Cmp };
                args.AddRange(job.Ids.Select(Str));
                var (lines, _, _, _, _, _) = await RunChildAsync(args, TimeSpan.FromSeconds(120));
                lock (result)
                {
                    foreach (var line in lines.Where(l => l.StartsWith("M\t", StringComparison.Ordinal)))
                    {
                        var parts = line.Split('\t');
                        result[(parts[1], long.Parse(parts[2], CultureInfo.InvariantCulture))] =
                            (int.Parse(parts[3], CultureInfo.InvariantCulture), Convert.FromBase64String(parts[4]));
                    }
                }
            }
            finally
            {
                gate.Release();
            }
        }));
        return result;
    }

    private async Task<Dictionary<long, string>> DescribeAsync()
    {
        var ids = _stats.Groups.Where(g => g.Key.Cmp is "W" or "JS" && g.Key.Class != "PASS")
            .Select(g => g.Value.RepId).Distinct().ToList();
        var result = new Dictionary<long, string>();
        foreach (var chunk in ids.Chunk(20))
        {
            var args = new List<string> { "describe", Str((long)_options.Seed) };
            args.AddRange(chunk.Select(Str));
            var (lines, _, _, _, _, _) = await RunChildAsync(args, TimeSpan.FromSeconds(120));
            foreach (var line in lines.Where(l => l.StartsWith("D\t", StringComparison.Ordinal)))
            {
                var parts = line.Split('\t');
                result[long.Parse(parts[1], CultureInfo.InvariantCulture)] = Encoding.UTF8.GetString(Convert.FromBase64String(parts[2]));
            }
        }

        return result;
    }

    private string Report(TimeSpan fuzzTime, Dictionary<(string, long), (int Original, byte[] Minimal)> minimised, Dictionary<long, string> described)
    {
        var o = _options;
        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"# Fuzz run `{o.Label}`\n\n");
        sb.Append(CultureInfo.InvariantCulture, $"- seed {o.Seed}; cases requested {o.Cases:N0}; cases run {_casesRun:N0}; batch {o.Batch:N0}; workers {o.Workers}\n");
        sb.Append(CultureInfo.InvariantCulture, $"- fuzzing time {fuzzTime.TotalSeconds:F1} s (budget {o.Budget.TotalSeconds:F0} s); total with minimisation {_clock.Elapsed.TotalSeconds:F1} s\n");
        sb.Append("- runtime: ").Append(System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription)
            .Append(System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported ? " (JIT)" : " (NativeAOT)").Append("\n\n");

        sb.Append("Comparisons: `A~B` original generator vs System.Text.Json (bench options); `F~B` fixed generator vs B; ");
        sb.Append("`A~S`/`F~S` against the strict reference S. Classes: EQ both accept, equal; EQ-src equal to each other but not to the source value; ");
        sb.Append("NE both accept, values differ; RJ both reject; XR-YA left rejects, right accepts; XA-YR left accepts, right rejects; ");
        sb.Append("X-nonjson left throws something other than JsonException; X-unwritable left accepts a value that cannot be written back as JSON (an infinity).\n\n");

        sb.Append("## Case counts\n\n| Comparison | Stream | ");
        var streams = _stats.Counts.Keys.Select(k => (k.Cmp, k.Stream)).Distinct().OrderBy(k => k.Cmp, StringComparer.Ordinal).ThenBy(k => k.Stream, StringComparer.Ordinal).ToList();
        var classes = ClassOrder.Concat(new[] { "CRASH", "HANG" }).Where(c => _stats.Counts.Keys.Any(k => k.Class == c)).ToList();
        sb.Append(string.Join(" | ", classes)).Append(" | total |\n|---|---|").Append(string.Concat(Enumerable.Repeat("---:|", classes.Count + 1))).Append('\n');
        foreach (var (cmp, stream) in streams)
        {
            sb.Append(CultureInfo.InvariantCulture, $"| {cmp} | {stream} |");
            long total = 0;
            foreach (var cls in classes)
            {
                var n = _stats.Counts.GetValueOrDefault((cmp, stream, cls));
                total += n;
                sb.Append(CultureInfo.InvariantCulture, $" {n:N0} |");
            }

            sb.Append(CultureInfo.InvariantCulture, $" {total:N0} |\n");
        }

        sb.Append("\n## Incidents (crash or hang)\n\n");
        if (_incidents.Count == 0)
        {
            sb.Append("None.\n");
        }
        else
        {
            sb.Append("| Kind | Case | Stream | Target | Ops | Exit | First stderr line |\n|---|---:|---|---|---|---|---|\n");
            foreach (var incident in _incidents)
            {
                sb.Append(incident).Append('\n');
            }
        }

        sb.Append("\n## Representatives\n\nKey: left outcome | right outcome (normalised message). The example is the shortest case in the group, minimised with ddmin while class and keys stay the same.\n");
        foreach (var group in Representatives().GroupBy(r => (r.Cmp, r.Class)))
        {
            var total = _stats.Groups.Where(g => g.Key.Cmp == group.Key.Cmp && g.Key.Class == group.Key.Class).ToList();
            sb.Append(CultureInfo.InvariantCulture, $"\n### {group.Key.Cmp} {group.Key.Class}: {total.Sum(g => g.Value.Count):N0} cases in {total.Count:N0} groups\n\n");
            sb.Append("| Count | Target | Key | Example (minimised) | Ops of the representative |\n|---:|---|---|---|---|\n");
            foreach (var r in group)
            {
                var c = Cases.Create(o.Seed, r.Group.RepId);
                var example = minimised.TryGetValue((r.Cmp, r.Group.RepId), out var m)
                    ? $"`{Text.Display(m.Minimal, 160)}` (from {m.Original:N0} B)"
                    : $"`{Text.Display(c.Input, 160)}`";
                sb.Append(CultureInfo.InvariantCulture, $"| {r.Group.Count:N0} | {c.Target.Name} | {Cell(r.Key)} | {example} | {Cell(c.Ops)} |\n");
            }
        }

        foreach (var cmp in new[] { "W", "JS" })
        {
            sb.Append(CultureInfo.InvariantCulture, $"\n## {(cmp == "W" ? "Writer and round-trip checks" : "JavaScript JSON.parse checks")} (value stream)\n\n");
            sb.Append("| Class | Count | Key | Representative |\n|---|---:|---|---|\n");
            foreach (var g in _stats.Groups.Where(g => g.Key.Cmp == cmp).OrderBy(g => g.Key.Class, StringComparer.Ordinal).ThenByDescending(g => g.Value.Count))
            {
                var c = Cases.Create(o.Seed, g.Value.RepId);
                var description = described.TryGetValue(g.Value.RepId, out var d) ? d : string.Empty;
                sb.Append(CultureInfo.InvariantCulture, $"| {g.Key.Class} | {g.Value.Count:N0} | {Cell(g.Key.Key)} | case {g.Value.RepId} {c.Target.Name} {c.Ops}: {Cell(description)} |\n");
            }
        }

        return sb.ToString();
    }

    private static string Cell(string text)
    {
        text = text.Replace("|", "\\|", StringComparison.Ordinal).Replace('`', '\'');
        return text.Length > 300 ? text[..300] + "…" : text;
    }

    private static string Str(long value) => value.ToString(CultureInfo.InvariantCulture);
}

internal sealed record Options(
    ulong Seed, long Cases, long Batch, int Workers, TimeSpan Budget, string Label, string Out, bool Minimise, long InjectCrash, long InjectHang)
{
    public static Options Parse(string[] args)
    {
        string Get(string name, string fallback)
        {
            var at = Array.IndexOf(args, name);
            return at >= 0 && at + 1 < args.Length ? args[at + 1] : fallback;
        }

        var label = Get("--label", "run");
        return new Options(
            ulong.Parse(Get("--seed", "1"), CultureInfo.InvariantCulture),
            long.Parse(Get("--cases", "200000"), CultureInfo.InvariantCulture),
            long.Parse(Get("--batch", "4000"), CultureInfo.InvariantCulture),
            int.Parse(Get("--workers", "6"), CultureInfo.InvariantCulture),
            TimeSpan.FromSeconds(double.Parse(Get("--budget", "420"), CultureInfo.InvariantCulture)),
            label,
            Get("--out", Path.Combine("results", label + ".md")),
            Get("--minimise", "true") == "true",
            long.Parse(Get("--inject-crash", "-1"), CultureInfo.InvariantCulture),
            long.Parse(Get("--inject-hang", "-1"), CultureInfo.InvariantCulture));
    }
}
