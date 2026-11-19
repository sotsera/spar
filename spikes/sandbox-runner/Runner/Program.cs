using System.Diagnostics;
using SandboxRunner;

// Usage, from the repository root:
//   dotnet run --project spikes/sandbox-runner/Runner -- session --name <name> [--prestage] [--connect auto|always|never]
//       --job "<payload folder>|<exe>|user|system|<deadline s>|<arguments>|xunit|plain" [--job ...]
//   dotnet run --project spikes/sandbox-runner/Runner -- two --name <name>
//   dotnet run --project spikes/sandbox-runner/Runner -- list
var root = Path.GetFullPath(Path.Combine("artifacts", "sandbox-runner"));
Directory.CreateDirectory(root);

switch (args.FirstOrDefault())
{
    case "session":
    {
        var jobs = new List<Job>();
        string? name = null;
        var prestage = false;
        var connect = "auto";
        var allowRunning = false;
        for (var i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--name": name = args[++i]; break;
                case "--job": jobs.Add(Job.Parse(args[++i])); break;
                case "--prestage": prestage = true; break;
                case "--connect": connect = args[++i]; break;
                case "--allow-running": allowRunning = true; break;
                default: throw new ArgumentException("unknown argument " + args[i]);
            }
        }

        return await new Session(new SessionOptions(name ?? "session", root, jobs, prestage, connect, allowRunning)).RunAsync();
    }

    case "two":
        return await TwoAsync(root, args.Length > 2 ? args[2] : "two");

    case "list":
    {
        var (ids, raw) = await Wsb.ListAsync();
        Console.WriteLine($"{ids.Count} running: {string.Join(",", ids)} ({raw.OneLine})");
        foreach (var window in ClientWindows.List())
        {
            Console.WriteLine($"client window {window.Handle} pid={window.ProcessId} '{window.Title}' visible={window.Visible} minimized={window.Minimized}");
        }

        return 0;
    }

    default:
        Console.Error.WriteLine("usage: session|two|list");
        return 64;
}

// Two sandboxes at once: start one, wait until it runs commands, then start a second and record what wsb says.
static async Task<int> TwoAsync(string root, string name)
{
    var folder = Path.Combine(root, "sessions", name);
    Directory.CreateDirectory(folder);
    using var timeline = new Timeline(Path.Combine(folder, "timeline.jsonl"));
    var (before, _) = await Wsb.ListAsync();
    if (before.Count > 0)
    {
        timeline.Write("refused", null, ("reason", "a sandbox is already running"));
        return 2;
    }

    const string Config = "<Configuration><Networking>Disable</Networking><ClipboardRedirection>Disable</ClipboardRedirection><PrinterRedirection>Disable</PrinterRedirection><VideoInput>Disable</VideoInput><AudioInput>Disable</AudioInput></Configuration>";
    var first = Guid.NewGuid().ToString();
    var second = Guid.NewGuid().ToString();
    string Redact(string text) => text.Replace(first, "<first>", StringComparison.Ordinal).Replace(second, "<second>", StringComparison.Ordinal);
    var clock = Stopwatch.StartNew();
    try
    {
        var a = await Wsb.RunAsync(TimeSpan.FromMinutes(5), "start", "--id", first, "--config", Config, "--raw");
        timeline.Write("start-first", a.Elapsed, ("raw", Redact(a.OneLine)));
        while (clock.Elapsed < TimeSpan.FromMinutes(3))
        {
            var probe = await Wsb.ExecAsync(first, "System", "cmd /c exit 0", TimeSpan.FromSeconds(60));
            if (probe.GuestExitCode == 0)
            {
                timeline.Write("first-exec-ready", clock.Elapsed);
                break;
            }

            await Task.Delay(250);
        }

        var b = await Wsb.RunAsync(TimeSpan.FromMinutes(5), "start", "--id", second, "--config", Config, "--raw");
        timeline.Write("start-second", b.Elapsed, ("raw", Redact(b.OneLine)));
        var (during, list) = await Wsb.ListAsync();
        timeline.Write("list-during", list.Elapsed, ("ids", Redact(string.Join(",", during))));

        // The same id twice, while it runs.
        var again = await Wsb.RunAsync(TimeSpan.FromMinutes(5), "start", "--id", first, "--config", Config, "--raw");
        timeline.Write("start-same-id", again.Elapsed, ("raw", Redact(again.OneLine)));
        var (during2, _) = await Wsb.ListAsync();
        timeline.Write("list-during-2", null, ("ids", Redact(string.Join(",", during2))));
    }
    finally
    {
        var (running, _) = await Wsb.ListAsync();
        foreach (var id in running.Where(id => id == first || id == second))
        {
            var stop = await Wsb.RunAsync(TimeSpan.FromMinutes(2), "stop", "--id", id, "--raw");
            timeline.Write("stop", stop.Elapsed, ("which", Redact(id)), ("raw", Redact(stop.OneLine)));
        }

        var (after, _) = await Wsb.ListAsync();
        timeline.Write("list-after", null, ("ids", Redact(string.Join(",", after))), ("clientWindows", ClientWindows.List().Count));
    }

    return 0;
}
