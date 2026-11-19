using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace WebView2Flags;

/// <summary>
/// Runs a plan: rounds × variants, each a child process on a desktop of the driver's own (CreateDesktop, then
/// STARTUPINFO.lpDesktop = "&lt;station&gt;\&lt;desktop&gt;"), so no window reaches the user's desktop. Variants rotate
/// in each round so drift spreads evenly. Results go to <c>spikes/webview2-flags/results/&lt;suite&gt;/</c>, profiles to
/// <c>artifacts/webview2-profiles/webview2-flags/</c>, jobs, net logs and captures to <c>artifacts/webview2-flags/</c>.
/// </summary>
internal static unsafe class Driver
{
    public static int Run(string planPath)
    {
        var plan = JsonNode.Parse(File.ReadAllText(planPath))!.AsObject();
        var suite = (string)plan["suite"]!;
        var root = RepositoryRoot();
        var results = Path.Combine(root, "spikes", "webview2-flags", "results", suite);
        var work = Path.Combine(root, "artifacts", "webview2-flags", suite);
        Directory.CreateDirectory(results);
        Directory.CreateDirectory(work);
        var runsFile = Path.Combine(results, "runs.jsonl");
        File.WriteAllText(runsFile, "");

        var station = Native.ObjectName(Native.GetProcessWindowStation());
        var desktopName = $"webview2-flags-{Environment.ProcessId}";
        var desktop = Native.CreateDesktopW(desktopName, 0, 0, 0, Native.GENERIC_ALL, 0);
        if (desktop == 0)
        {
            Console.Error.WriteLine($"CreateDesktop failed: {Marshal.GetLastPInvokeError()}");
            return 2;
        }

        Console.WriteLine($"desktop {station}\\{desktopName}");
        using var listener = (bool?)plan["listener"] == true ? new Listener() : null;
        var variants = plan["variants"]!.AsArray();
        var rounds = (int)plan["rounds"]!;
        var all = new List<JsonObject>();
        try
        {
            for (var round = 0; round < rounds; round++)
            {
                for (var k = 0; k < variants.Count; k++)
                {
                    var variant = variants[((bool?)plan["rotate"] == false ? k : round + k) % variants.Count]!.AsObject();
                    var run = RunOne(plan, variant, round, root, work, $"{station}\\{desktopName}", listener);
                    all.Add(run);
                    File.AppendAllText(runsFile, Redact(Slim(run).ToJsonString()) + "\n");
                    Console.WriteLine(Line(run));
                }
            }
        }
        finally
        {
            Native.CloseDesktop(desktop);
        }

        if (listener is not null)
        {
            var connections = new JsonArray([.. listener.All().Select(c => (JsonNode)c.DeepClone())]);
            File.WriteAllText(Path.Combine(results, "listener.json"), Redact(connections.ToJsonString(new JsonSerializerOptions { WriteIndented = true })));
        }

        var warmup = (int?)plan["warmup"] ?? 0;
        var summary = Summary.Write(suite, variants, [.. all.Where(r => (int)r["job"]!["round"]! >= warmup)])
            + $"{Environment.NewLine}(rounds below {warmup} are warm-up runs, left out above){Environment.NewLine}";
        File.WriteAllText(Path.Combine(results, "summary.txt"), Redact(summary));
        Console.WriteLine(summary);
        return 0;
    }

    private static JsonObject RunOne(JsonObject plan, JsonObject variant, int round, string root, string work, string desktop, Listener? listener)
    {
        var suite = (string)plan["suite"]!;
        var name = (string)variant["name"]!;
        var args = (string?)variant["args"] ?? "";
        if ((bool?)variant["netLog"] == true)
        {
            var log = Path.Combine(work, $"netlog-{name}-{round}.json");
            args = (args + $" --log-net-log=\"{log}\" --net-log-capture-mode={(string?)plan["netLogMode"] ?? "Everything"}").Trim();
        }

        var job = new JsonObject
        {
            ["suite"] = suite,
            ["variant"] = name,
            ["round"] = round,
            ["scenario"] = (string)plan["scenario"]!,
            ["args"] = args.Length == 0 ? null : args,
            ["userDataFolder"] = Path.Combine(root, "artifacts", "webview2-profiles", "webview2-flags", $"{suite}-{(string?)variant["profile"] ?? name}{((bool?)plan["freshProfiles"] == true ? $"-r{round}" : "")}"),
            ["browserFolder"] = variant["browserFolder"] is { } folder ? Path.Combine(root, (string)folder!) : null,
            ["wasmEval"] = (bool?)variant["wasmEval"] ?? false,
            ["reputation"] = (bool?)variant["reputation"] ?? false,
            ["navigations"] = (int?)plan["navigations"] ?? 3,
            ["stayMs"] = (int?)variant["stayMs"] ?? (int?)plan["stayMs"] ?? 0,
            ["outDir"] = Path.Combine(work, $"{name}-{round}"),
        };
        var jobFile = Path.Combine(work, $"job-{name}-{round}.json");
        var resultFile = Path.Combine(work, $"result-{name}-{round}.json");
        File.WriteAllText(jobFile, job.ToJsonString());
        File.Delete(resultFile);

        var start = DateTime.UtcNow;
        var watch = Stopwatch.StartNew();
        var exit = Launch($"\"{Environment.ProcessPath}\" host \"{jobFile}\" \"{resultFile}\"", desktop, (int?)plan["timeoutMs"] ?? 120000);
        var end = DateTime.UtcNow;
        if ((bool?)plan["deleteProfiles"] == true)
        {
            // The browser has exited (the child waited for BrowserProcessExited); a fresh profile per run would fill the disk.
            try
            {
                Directory.Delete((string)job["userDataFolder"]!, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
        var run = File.Exists(resultFile) ? JsonNode.Parse(File.ReadAllText(resultFile))!.AsObject() : new JsonObject { ["job"] = job, ["error"] = "no result" };
        run["childExit"] = exit;
        run["childWallMs"] = watch.Elapsed.TotalMilliseconds;
        if (listener is not null)
        {
            var pids = new HashSet<uint>();
            if (run["processes"] is JsonArray processes)
            {
                foreach (var p in processes)
                {
                    pids.Add((uint)(int)p!["pid"]!);
                }
            }

            if (run["browserPid"] is { } browserPid)
            {
                pids.Add((uint)browserPid);
            }

            var window = listener.Between(start, end.AddMilliseconds(500));
            var mine = window.Where(c => c["pid"] is { } pid && pids.Contains((uint)pid)).ToList();
            run["loopback"] = new JsonArray([.. mine.Select(c => (JsonNode)c.DeepClone())]);
            run["loopbackOthers"] = window.Count - mine.Count;
        }

        return run;
    }

    private static string Line(JsonObject run)
    {
        var job = run["job"]!;
        var nav = run["navigations"] is JsonArray { Count: > 0 } n && n[0] is JsonObject first ? $" ready0={(double)first["readyMs"]!:F0}" : "";
        var loop = run["loopback"] is JsonArray l ? $" loopback={l.Count}" : "";
        return $"{job["variant"]} r{job["round"]} exit={run["childExit"]} env={(double?)run["envMs"]:F0} ctl={(double?)run["controllerMs"]:F0}{nav}{loop} {(run["error"] is { } e ? "ERROR " + ((string)e!).Split('\n')[0] : "")}";
    }

    private static int Launch(string commandLine, string desktop, int timeoutMs)
    {
        var si = new Native.STARTUPINFOW { cb = (uint)sizeof(Native.STARTUPINFOW) };
        fixed (char* desk = desktop)
        fixed (char* cmd = commandLine + "\0")
        {
            si.lpDesktop = desk;
            if (!Native.CreateProcessW(null, cmd, 0, 0, false, Native.CREATE_NO_WINDOW, 0, null, si, out var pi))
            {
                throw new InvalidOperationException($"CreateProcess failed: {Marshal.GetLastPInvokeError()}");
            }

            Native.CloseHandle(pi.hThread);
            var waited = Native.WaitForSingleObject(pi.hProcess, (uint)timeoutMs);
            if (waited != 0)
            {
                Native.TerminateProcess(pi.hProcess, 99);
                Native.WaitForSingleObject(pi.hProcess, 5000);
            }

            Native.GetExitCodeProcess(pi.hProcess, out var code);
            Native.CloseHandle(pi.hProcess);
            return (int)code;
        }
    }

    /// <summary>A copy of a run for the committed log: switches instead of whole command lines, request paths counted.</summary>
    public static JsonObject Slim(JsonObject run)
    {
        var copy = run.DeepClone().AsObject();
        if (copy["browserCommandLine"] is { } browser)
        {
            copy["browserCommandLine"] = Summary.Interesting((string?)browser);
        }

        if (copy["processes"] is JsonArray processes)
        {
            foreach (var p in processes)
            {
                p!.AsObject().Remove("commandLine");
            }
        }

        if (copy["requests"] is JsonArray requests)
        {
            copy["firstRequestAt"] = requests.Count > 0 ? (double?)requests[0]!["at"] : null;
            var counted = new JsonObject();
            foreach (var g in requests.GroupBy(r => $"{r!["path"]} {r["source"]}"))
            {
                counted[g.Key] = g.Count();
            }

            copy["requests"] = counted;
        }

        return copy;
    }

    public static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "global.json")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("repository root not found");
    }

    /// <summary>Replaces the user profile path, if any, so committed files carry no user name.</summary>
    public static string Redact(string text)
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return string.IsNullOrEmpty(profile)
            ? text
            : text.Replace(profile, @"C:\Users\<user>", StringComparison.OrdinalIgnoreCase)
                .Replace(profile.Replace(@"\", @"\\"), @"C:\\Users\\<user>", StringComparison.OrdinalIgnoreCase);
    }
}
