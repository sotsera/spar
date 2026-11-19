using System.Diagnostics;
using System.Globalization;
using System.Runtime.Versioning;
using System.Text;
using System.Text.RegularExpressions;
using InvokeDriver;
using Spar.Testing;

// The Invoke spike's driver: see ../RESULTS.md. Usage:
//   run <variant>[,<variant>...] <invokes per launch> <launches>
// Variants (each on the hidden desktop):
//   mta | mta-nofocus | mta-timeouts | mta-legacy | mta-legacy-nofocus | mta-setfocus | sta-pump | sta-pump-nofocus
//   mta-foreground (SetForegroundWindow and GetForegroundWindow from a thread on the hidden desktop)
// One CSV row per action, one summary line per variant; the page's frame and timer rates and visibility per launch.
// SPIKE_TRACE=1 prints every page event per action; SPIKE_WATCHER=same configures the dialog watcher like the variant.
//   settings   whether IUIAutomation2's settings belong to one client object or to the process
[assembly: SupportedOSPlatform("windows10.0.19045")]

var verb = args.ElementAtOrDefault(0) ?? "run";
if (verb == "settings")
{
    // Whether IUIAutomation2's settings belong to one client object or to the process: set them on one, read another.
    var probeThread = new Thread(SettingsProbe.Run);
    probeThread.SetApartmentState(ApartmentState.MTA);
    probeThread.Start();
    probeThread.Join();
    return 0;
}

if (verb != "run")
{
    return 2;
}

var variants = (args.ElementAtOrDefault(1) ?? "mta,mta-nofocus").Split(',');
var invokes = int.Parse(args.ElementAtOrDefault(2) ?? "10", CultureInfo.InvariantCulture);
var launches = int.Parse(args.ElementAtOrDefault(3) ?? "1", CultureInfo.InvariantCulture);
var spike = new Spike();
Console.WriteLine("variant,launch,action,index,call_start_ms,call_ms,first_page_event,first_page_event_ms,click_page_ms,click_backend_ms,focusin_page_ms,trusted,active_at_click,dialog_seen_ms,foreground_hits");
var summaries = new List<string>();
for (var round = 0; round < launches; round++)
{
    foreach (var variant in variants)
    {
        summaries.Add(await spike.RunAsync(Variant.Parse(variant), invokes));
    }
}

foreach (var line in summaries)
{
    Console.WriteLine(line);
}

return 0;

namespace InvokeDriver
{
    /// <summary>One way of clicking a page button through UI Automation.</summary>
    internal sealed record Variant(string Name, bool Sta, bool? AutoSetFocus, uint? ConnectionTimeout, uint? TransactionTimeout, string Action)
    {
        public static Variant Parse(string name) => name switch
        {
            "mta" => new(name, false, null, null, null, "invoke"),
            "mta-nofocus" => new(name, false, false, null, null, "invoke"),
            "mta-timeouts" => new(name, false, null, 250, 1000, "invoke"),
            "mta-legacy" => new(name, false, null, null, null, "legacy"),
            "mta-legacy-nofocus" => new(name, false, false, null, null, "legacy"),
            "mta-setfocus" => new(name, false, null, null, null, "setfocus"),
            "mta-foreground" => new(name, false, null, null, null, "foreground"),
            "sta-pump" => new(name, true, null, null, null, "invoke"),
            "sta-pump-nofocus" => new(name, true, false, null, null, "invoke"),
            _ => throw new ArgumentException($"unknown variant {name}"),
        };
    }

    [SupportedOSPlatform("windows10.0.19045")]
    internal sealed partial class Spike
    {
        private const string AppId = "com.example.spikes.InvokeProbe";
        private static readonly string Repository = FindRepository();
        private static readonly string Executable = Path.Combine(Repository, @"spikes\blackbox-invoke\InvokeProbe\bin\Production\Release\net11.0\win-x64\publish\InvokeProbe.exe");
        private static readonly string Stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        private readonly HiddenDesktop _desktop = HiddenDesktop.Create($"spar-invoke-spike-{Environment.ProcessId}");
        private int _launch;

        public async Task<string> RunAsync(Variant variant, int invokes)
        {
            var index = Interlocked.Increment(ref _launch);
            var root = Path.Combine(Repository, "artifacts", "blackbox-invoke", Stamp, $"{index:D3}-{variant.Name}");
            using var process = Start(root);
            using var watch = new ForegroundWatch(process);
            using var automation = UiaThread.Start(_desktop, variant);
            // The thread that watches for the dialog: a default client, or (SPIKE_WATCHER=same) one configured as the variant's.
            using var watcher = UiaThread.Start(_desktop, Environment.GetEnvironmentVariable("SPIKE_WATCHER") == "same" ? variant with { Sta = false, Name = variant.Name + "-watcher" } : Variant.Parse("mta"));
            var calls = new List<double>();
            var firstEvents = new List<double>();
            var clicks = new List<double>();
            try
            {
                await WaitForAsync(() => Marks(process, 0).Any(m => m.Text.StartsWith("loaded", StringComparison.Ordinal)), TimeSpan.FromSeconds(30), "the page's loaded mark");
                await WaitForAsync(() => automation.RunAsync(uia => uia.Button(process.ProcessId, "Count") is not null).Result, TimeSpan.FromSeconds(10), "the Count button");
                await Task.Delay(1000);
                for (var number = 1; number <= invokes; number++)
                {
                    var row = await ActAsync(variant, process, automation, watch, "Count", number, expectClick: variant.Action is not ("setfocus" or "foreground"));
                    calls.Add(row.CallMs);
                    if (row.FirstEventMs is { } first)
                    {
                        firstEvents.Add(first);
                    }

                    if (row.ClickPageMs is { } click)
                    {
                        clicks.Add(click);
                    }

                    await Task.Delay(300);
                }

                if (variant.Action is not ("setfocus" or "foreground"))
                {
                    // The Notebook's shape: a button that opens a modal dialog, until another thread sees the dialog's button.
                    var start = Clock.Now;
                    var dialogSeen = Task.Run(async () =>
                    {
                        await WaitForAsync(() => watcher.RunAsync(uia => uia.Button(process.ProcessId, "Close dialog") is not null).Result, TimeSpan.FromSeconds(10), "the dialog");
                        return Clock.Now - start;
                    });
                    await ActAsync(variant, process, automation, watch, "Open dialog", 1, expectClick: true, dialogSeen);
                    await automation.RunAsync(uia => uia.Act(process.ProcessId, "Close dialog", "invoke"));
                }

                await automation.RunAsync(uia => uia.Close(process.ProcessId));
                var exit = await process.Exited.WaitAsync(TimeSpan.FromSeconds(15));
                await process.WaitForTreeAsync(TimeSpan.FromSeconds(10));
                var stats = Marks(process, 0).Where(m => m.Text.StartsWith("stats", StringComparison.Ordinal)).Select(m => m.Text).ToList();
                var visibility = string.Join(" ", Marks(process, 0).Where(m => m.Text.StartsWith("visibility", StringComparison.Ordinal) || m.Text.StartsWith("window-", StringComparison.Ordinal)).Select(m => m.Text.Split(' ')[0] + ":" + VisibilityOf(m.Text)));
                return $"# {variant.Name} launch {index}: exit {exit}; call {Describe(calls)}; first page event after start {Describe(firstEvents)}; click {Describe(clicks)}; " +
                    $"foreground hits {watch.Hits}; stats {StatsSummary(stats)}; visibility events [{visibility}]";
            }
            catch (Exception exception)
            {
                var windows = string.Join(" | ", await automation.RunAsync(uia => TopWindows.Of(process.ProcessId).Select(w => $"{w.ClassName} owner={w.Owner != 0}").ToList()));
                var texts = string.Join(" | ", await automation.RunAsync(uia => TopWindows.Of(process.ProcessId).Where(w => w.IsDialog).Select(w => uia.Uia.FromHandle(w.Handle)?.Name ?? "?").ToList()));
                var appLog = Path.Combine(root, "LocalAppData", AppId, "logs", "app.log");
                if (File.Exists(appLog))
                {
                    using var stream = new FileStream(appLog, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    var lines = new StreamReader(stream).ReadToEnd().Split('\n');
                    Console.WriteLine("  app.log tail:\n    " + string.Join("\n    ", lines.TakeLast(15)));
                }

                Console.WriteLine($"  at failure: top windows [{windows}]; dialogs [{texts}]; exited {process.ExitCode?.ToString() ?? "no"}");
                process.Kill();
                await process.WaitForTreeAsync(TimeSpan.FromSeconds(10));
                return $"# {variant.Name} launch {index}: FAILED {exception.GetType().Name}: {exception.Message}; stderr tail: {Tail(process.StandardError)}";
            }
            finally
            {
                for (var attempt = 0; attempt < 50 && Directory.Exists(root); attempt++)
                {
                    try
                    {
                        Directory.Delete(root, recursive: true);
                    }
                    catch (IOException)
                    {
                        await Task.Delay(100);
                    }
                    catch (UnauthorizedAccessException)
                    {
                        await Task.Delay(100);
                    }
                }
            }
        }

        private static async Task<Row> ActAsync(Variant variant, AppProcess process, UiaThread automation, ForegroundWatch watch, string button, int number, bool expectClick, Task<double>? dialogSeen = null)
        {
            var offset = process.StandardError.Length;
            var hitsBefore = watch.Hits;
            var start = Clock.Now;
            var (callStart, callEnd) = await automation.RunAsync(uia =>
            {
                var before = Clock.Now;
                uia.Act(process.ProcessId, button, variant.Action);
                return (before, Clock.Now);
            });
            var wanted = expectClick ? "click" : variant.Action == "foreground" ? "window-focus" : "focusin";
            await WaitForAsync(() => Marks(process, offset).Any(m => m.Kind == wanted && (wanted == "window-focus" || m.Target != "BODY")), TimeSpan.FromSeconds(10), $"the page's {wanted}");
            var seen = dialogSeen is null ? (double?)null : await dialogSeen;
            await Task.Delay(50);
            var marks = Marks(process, offset).Where(m => m.Kind is "pointerdown" or "mousedown" or "pointerup" or "mouseup" or "focusin" or "click" or "keydown").ToList();
            var first = marks.OrderBy(m => m.PageMs).FirstOrDefault();
            var click = marks.FirstOrDefault(m => m.Kind == "click");
            var focus = marks.FirstOrDefault(m => m.Kind == "focusin");
            var row = new Row(callEnd - callStart, first is null ? null : first.PageMs - callStart, click?.PageMs - callStart);
            if (Environment.GetEnvironmentVariable("SPIKE_TRACE") == "1")
            {
                foreach (var each in Marks(process, offset).Where(m => m.Kind != "stats"))
                {
                    Console.WriteLine(FormattableString.Invariant($"  trace page {each.PageMs - callStart:0.0} backend {each.BackendMs - callStart:0.0}: {each.Text}"));
                }

                Console.WriteLine(FormattableString.Invariant($"  trace call returned {callEnd - callStart:0.0}"));
            }

            Console.WriteLine(string.Join(',',
                variant.Name, process.ProcessId, variant.Action + ":" + button, number,
                F(callStart - start), F(row.CallMs), first?.Kind ?? "", F(row.FirstEventMs), F(row.ClickPageMs), F(click?.BackendMs - callStart),
                F(focus?.PageMs - callStart), click?.Trusted ?? "", click?.Active ?? "", F(seen), watch.Hits - hitsBefore));
            return row;
        }

        private static string Describe(List<double> values)
        {
            if (values.Count == 0)
            {
                return "n/a";
            }

            var sorted = values.Order().ToList();
            return FormattableString.Invariant($"median {sorted[sorted.Count / 2]:0} ms ({sorted[0]:0}–{sorted[^1]:0}, n={sorted.Count})");
        }

        private static string StatsSummary(List<string> stats)
        {
            var frames = stats.Select(s => Field(s, "frames")).Where(v => v >= 0).ToList();
            var ticks = stats.Select(s => Field(s, "ticks")).Where(v => v >= 0).ToList();
            var elapsed = stats.Select(s => Field(s, "elapsed")).Where(v => v >= 0).ToList();
            var visible = stats.Count(s => s.Contains("vis=visible", StringComparison.Ordinal));
            var focused = stats.Count(s => s.Contains("focus=true", StringComparison.Ordinal));
            return $"{stats.Count} reports, frames/s {Range(frames)}, 100 ms ticks/s {Range(ticks)}, report interval ms {Range(elapsed)}, visible in {visible}, focused in {focused}";
        }

        private static string Range(List<int> values) => values.Count == 0 ? "n/a" : FormattableString.Invariant($"{values.Order().ElementAt(values.Count / 2)} ({values.Min()}–{values.Max()})");

        private static int Field(string text, string name)
        {
            var match = Regex.Match(text, $@"\b{name}=(\d+)");
            return match.Success ? int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) : -1;
        }

        private static string VisibilityOf(string text)
        {
            var match = Regex.Match(text, @"vis=(\w+) focus=(\w+)");
            return match.Success ? match.Groups[1].Value + "/" + match.Groups[2].Value : "?";
        }

        private static string F(double? value) => value is { } v ? v.ToString("0", CultureInfo.InvariantCulture) : "";

        private static string Tail(string text) => text.Length > 400 ? text[^400..].Replace('\n', ' ') : text.Replace('\n', ' ');

        private AppProcess Start(string root)
        {
            string[] folders = ["LocalAppData", "AppData", "Temp"];
            foreach (var folder in folders)
            {
                Directory.CreateDirectory(Path.Combine(root, folder));
            }

            var environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
            {
                var name = (string)entry.Key;
                if (!name.StartsWith("WEBVIEW2_", StringComparison.OrdinalIgnoreCase) && !name.StartsWith("SPAR_", StringComparison.OrdinalIgnoreCase))
                {
                    environment[name] = (string)entry.Value!;
                }
            }

            environment["LOCALAPPDATA"] = Path.Combine(root, "LocalAppData");
            environment["APPDATA"] = Path.Combine(root, "AppData");
            environment["TEMP"] = Path.Combine(root, "Temp");
            environment["TMP"] = Path.Combine(root, "Temp");
            return AppProcess.Start(Executable, [], environment, root, _desktop.Name, null);
        }

        private static List<Mark> Marks(AppProcess process, int offset)
        {
            var text = process.StandardError;
            var list = new List<Mark>();
            foreach (Match match in MarkLine().Matches(text[Math.Min(offset, text.Length)..]))
            {
                var rest = match.Groups[3].Value.TrimEnd('\r');
                var words = rest.Split(' ');
                list.Add(new Mark(
                    Clock.FromUnix(double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture)),
                    Clock.FromUnix(double.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture)),
                    rest,
                    words[0],
                    words.ElementAtOrDefault(1) ?? "",
                    Regex.Match(rest, @"trusted=(\w+)").Groups[1].Value,
                    Regex.Match(rest, @"active=(\S+)").Groups[1].Value));
            }

            return list;
        }

        private static async Task WaitForAsync(Func<bool> condition, TimeSpan limit, string what)
        {
            var clock = Stopwatch.StartNew();
            while (clock.Elapsed < limit)
            {
                try
                {
                    if (condition())
                    {
                        return;
                    }
                }
                catch (Exception exception) when (exception is System.Runtime.InteropServices.COMException or AggregateException)
                {
                    // An element went away between finding and reading: look again.
                }

                await Task.Delay(10);
            }

            throw new TimeoutException($"{what} not seen within {limit.TotalSeconds:0} s");
        }

        private static string FindRepository()
        {
            for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, "Spar.slnx")))
                {
                    return directory.FullName;
                }
            }

            throw new InvalidOperationException("run from inside the repository");
        }

        [GeneratedRegex(@"^mark ([0-9.]+) ([0-9.]+) (.*)$", RegexOptions.Multiline)]
        private static partial Regex MarkLine();

        private sealed record Row(double CallMs, double? FirstEventMs, double? ClickPageMs);

        private sealed record Mark(double BackendMs, double PageMs, string Text, string Kind, string Target, string Trusted, string Active);
    }

    /// <summary>Milliseconds on the system's precise wall clock, the clock the page's timeOrigin and the backend use.</summary>
    internal static class Clock
    {
        public static double Now => (DateTime.UtcNow - DateTime.UnixEpoch).TotalMilliseconds;

        public static double FromUnix(double milliseconds) => milliseconds;
    }
}
