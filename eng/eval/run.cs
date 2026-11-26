#:package YamlDotNet
#:property PublishAot=false

// The evaluation of Spar's agent surfaces (plan, M2e; decision "M2e's decide-first questions", items 5 to 8; usage in
// eng/eval/README.md). From the repository's root:
//   dotnet run eng/eval/run.cs -- images                  the base, agent and proxy images
//   dotnet run eng/eval/run.cs -- prepare                 Spar packed, the two apps, the packages (needs network)
//   dotnet run eng/eval/run.cs -- validate [--tasks a,b]  each task with nothing done (its checks must fail) and with its
//                                                         reference solution (they must pass); no agent, no network
//   dotnet run eng/eval/run.cs -- run [--profiles dotnet,cli,mcp] [--runs 3] [--parallel 3] [--model sonnet] [--tasks a,b]
//                                                         the agent runs; needs ANTHROPIC_API_KEY or CLAUDE_CODE_OAUTH_TOKEN
//   dotnet run eng/eval/run.cs -- report <runs folder>    the report of a run folder, again
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using YamlDotNet.RepresentationModel;

var repository = FindRepository();
var spec = Path.Combine(repository, "eng", "eval");
var results = Path.Combine(repository, "artifacts", "eval");
const string PrepVolume = "spar-eval-prep";
const string Network = "spar-eval-net";
const string Proxy = "spar-eval-proxy";
const string AgentImage = "spar-eval-agent";

if (args.Length == 0)
{
    Console.Error.WriteLine("Usage: dotnet run eng/eval/run.cs -- images | prepare | validate | run | report <folder>");
    return 64;
}

var options = Options.Parse(args[1..]);
var tasks = EvalTask.ReadAll(Path.Combine(spec, "eval.yaml"))
    .Where(task => options.Tasks is null || options.Tasks.Contains(task.Name))
    .ToList();

switch (args[0])
{
    case "images":
        return await ImagesAsync();
    case "prepare":
        return await PrepareAsync();
    case "validate":
        return await ValidateAsync();
    case "run":
        return await RunAsync();
    case "report":
        return Report.Write(options.Positional.FirstOrDefault() ?? throw new ArgumentException("report needs the run folder."), tasks);
    default:
        Console.Error.WriteLine($"Unknown command '{args[0]}'.");
        return 64;
}

async Task<int> ImagesAsync()
{
    var sdk = JsonDocument.Parse(File.ReadAllText(Path.Combine(repository, "global.json"))).RootElement.GetProperty("sdk").GetProperty("version").GetString()!;
    (string Tag, string Folder, string[] Arguments)[] images =
    [
        ("spar-linux-ci:eval", Path.Combine(repository, "eng", "linux-ci"), ["--build-arg", $"DOTNET_SDK_VERSION={sdk}"]),
        (AgentImage, spec, []),
        (Proxy, Path.Combine(spec, "proxy"), []),
    ];
    foreach (var (tag, folder, extra) in images)
    {
        if (await Docker.RunAsync(["build", "-t", tag, .. extra, folder], echo: true) != 0)
        {
            return 1;
        }
    }

    return 0;
}

async Task<int> PrepareAsync()
{
    var suffix = "eval." + DateTime.UtcNow.ToString("yyyyMMddHHmm", CultureInfo.InvariantCulture);
    await Docker.RunAsync(["volume", "create", PrepVolume]);

    // A new volume belongs to root: the image's user (uid 1000) gets it first.
    await Docker.RunAsync(["run", "--rm", "--user", "root", "--mount", $"type=volume,source={PrepVolume},target=/eval/prep", AgentImage, "chown", "spar:spar", "/eval/prep"]);
    return await Docker.RunAsync(
    [
        "run", "--rm", .. Docker.WindowOptions,
        "--mount", $"type=bind,source={repository},target=/repo,readonly",
        "--mount", $"type=volume,source={PrepVolume},target=/eval/prep",
        "-e", $"EVAL_VERSION_SUFFIX={suffix}",
        AgentImage, "bash", "/repo/eng/eval/prepare.sh",
    ], echo: true);
}

async Task<int> ValidateAsync()
{
    var folder = NewRunFolder("validate");
    var failures = 0;
    foreach (var task in tasks)
    {
        foreach (var mode in new[] { "none", "reference" })
        {
            var run = Path.Combine(folder, task.Name, mode);
            var checks = await RunContainerAsync(task, mode, "cli", run, model: "", network: "none");
            var passed = checks.Count > 0 && checks.All(check => check.Passed);
            var expected = mode == "reference";
            var ok = passed == expected;
            failures += ok ? 0 : 1;
            Console.WriteLine($"{(ok ? "ok  " : "FAIL")} {task.Name,-24} {mode,-9} {string.Join(", ", checks.Select(c => $"{c.Name} {(c.Passed ? "pass" : "fail")}"))}");
        }
    }

    Console.WriteLine(failures == 0 ? "Every task's checks fail before and pass with its reference." : $"{failures} unexpected result(s); see {folder}.");
    return failures == 0 ? 0 : 1;
}

async Task<int> RunAsync()
{
    if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")) && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CLAUDE_CODE_OAUTH_TOKEN")))
    {
        Console.Error.WriteLine("Set ANTHROPIC_API_KEY or CLAUDE_CODE_OAUTH_TOKEN in this shell first; it is passed to the containers by name, never written.");
        return 2;
    }

    var folder = NewRunFolder("run");
    var profiles = options.Profiles ?? ["dotnet", "cli", "mcp"];
    var runs = options.Runs ?? 3;
    var model = options.Model ?? "sonnet";
    var settings = new JsonObject
    {
        ["profiles"] = new JsonArray([.. profiles.Select(p => JsonValue.Create(p))]),
        ["runs"] = runs,
        ["model"] = model,
        ["tasks"] = new JsonArray([.. tasks.Select(t => JsonValue.Create(t.Name))]),
    };
    File.WriteAllText(Path.Combine(folder, "settings.json"), settings.ToJsonString());

    await StartProxyAsync();
    try
    {
        using var gate = new SemaphoreSlim(options.Parallel ?? 3);
        var work = new List<Task>();
        for (var round = 1; round <= runs; round++)
        {
            foreach (var task in tasks)
            {
                foreach (var profile in profiles)
                {
                    var run = Path.Combine(folder, task.Name, $"{profile}-{round}");
                    work.Add(Task.Run(async () =>
                    {
                        await gate.WaitAsync();
                        try
                        {
                            var checks = await RunContainerAsync(task, "agent", profile, run, model, Network);
                            Console.WriteLine($"{DateTime.Now:HH:mm} {task.Name,-24} {Path.GetFileName(run),-9} {(checks.Count > 0 && checks.All(c => c.Passed) ? "pass" : "fail")}");
                        }
                        finally
                        {
                            gate.Release();
                        }
                    }));
                }
            }
        }

        await Task.WhenAll(work);
    }
    finally
    {
        await Docker.RunAsync(["rm", "-f", Proxy]);
    }

    return Report.Write(folder, tasks);
}

async Task StartProxyAsync()
{
    await Docker.RunAsync(["rm", "-f", Proxy]);
    await Docker.RunAsync(["network", "create", "--internal", Network]);
    await Docker.RunAsync(["run", "-d", "--name", Proxy, Proxy]);
    await Docker.RunAsync(["network", "connect", Network, Proxy]);
}

async Task<List<Check>> RunContainerAsync(EvalTask task, string mode, string profile, string run, string model, string network)
{
    Directory.CreateDirectory(run);
    var description = new JsonObject
    {
        ["name"] = task.Name,
        ["app"] = task.App,
        ["prompt"] = task.Prompt,
        ["model"] = model,
        ["maxTurns"] = task.MaxTurns,
        ["maxMinutes"] = task.MaxMinutes,
        ["commands"] = new JsonArray([.. task.Commands.Select(c => JsonValue.Create(c))]),
        ["graders"] = new JsonArray([.. task.Graders.Select(c => JsonValue.Create(c))]),
    };
    File.WriteAllText(Path.Combine(run, "task.json"), description.ToJsonString());

    string[] proxy = network == Network
        ? ["-e", $"HTTPS_PROXY=http://{Proxy}:8888", "-e", $"HTTP_PROXY=http://{Proxy}:8888", "-e", "NO_PROXY=localhost,127.0.0.1", "-e", "ANTHROPIC_API_KEY", "-e", "CLAUDE_CODE_OAUTH_TOKEN"]
        : [];
    var exit = await Docker.RunAsync(
    [
        "run", "--rm", .. Docker.WindowOptions, "--network", network, .. proxy,
        "--mount", $"type=volume,source={PrepVolume},target=/eval/prep,readonly",
        "--mount", $"type=bind,source={spec},target=/eval/spec,readonly",
        "--mount", $"type=bind,source={run},target=/eval/out",
        AgentImage, "bash", "/eval/spec/in-container.sh", mode, profile,
    ], log: Path.Combine(run, "container.log"));
    File.WriteAllText(Path.Combine(run, "container-exit"), exit.ToString(CultureInfo.InvariantCulture));
    return Check.Read(run);
}

string NewRunFolder(string kind)
{
    var folder = Path.Combine(results, $"{kind}-{DateTime.Now:yyyyMMdd-HHmmss}");
    Directory.CreateDirectory(folder);
    return folder;
}

static string FindRepository()
{
    for (var folder = new DirectoryInfo(Environment.CurrentDirectory); folder is not null; folder = folder.Parent)
    {
        if (File.Exists(Path.Combine(folder.FullName, "Spar.slnx")))
        {
            return folder.FullName;
        }
    }

    throw new InvalidOperationException("Run it from inside the Spar repository.");
}

/// <summary>The command line's options.</summary>
sealed class Options
{
    public HashSet<string>? Tasks { get; private set; }

    public string[]? Profiles { get; private set; }

    public int? Runs { get; private set; }

    public int? Parallel { get; private set; }

    public string? Model { get; private set; }

    public List<string> Positional { get; } = [];

    public static Options Parse(string[] args)
    {
        var options = new Options();
        for (var index = 0; index < args.Length; index++)
        {
            string Next() => args[++index];
            switch (args[index])
            {
                case "--tasks": options.Tasks = [.. Next().Split(',')]; break;
                case "--profiles": options.Profiles = Next().Split(','); break;
                case "--runs": options.Runs = int.Parse(Next(), CultureInfo.InvariantCulture); break;
                case "--parallel": options.Parallel = int.Parse(Next(), CultureInfo.InvariantCulture); break;
                case "--model": options.Model = Next(); break;
                default: options.Positional.Add(args[index]); break;
            }
        }

        return options;
    }
}

/// <summary>One stimulus of eval.yaml.</summary>
sealed record EvalTask(string Name, string App, string Prompt, string[] Skills, string[] Commands, string[] Graders, int MaxTurns, int MaxMinutes, long MaxTokens)
{
    public static List<EvalTask> ReadAll(string file)
    {
        var stream = new YamlStream();
        stream.Load(new StringReader(File.ReadAllText(file)));
        var root = (YamlMappingNode)stream.Documents[0].RootNode;
        var defaults = Map(Map(root, "defaults"), "constraints");
        var tasks = new List<EvalTask>();
        foreach (YamlMappingNode stimulus in (YamlSequenceNode)root.Children["stimuli"])
        {
            var constraints = Map(stimulus, "constraints") ?? defaults;
            var environment = Map(stimulus, "environment")!;
            var graders = ((YamlSequenceNode)stimulus.Children["graders"]).Cast<YamlMappingNode>()
                .Select(grader => Text(Map(grader, "config"), "command")!).ToArray();
            tasks.Add(new EvalTask(
                Text(stimulus, "name")!,
                Text(environment, "app") == "notebook" ? "Notebook" : "Pantry",
                Text(stimulus, "prompt")!.Trim(),
                List(Map(stimulus, "tags"), "skills"),
                List(environment, "commands"),
                graders,
                int.Parse(Text(constraints, "max_turns") ?? Text(defaults, "max_turns")!, CultureInfo.InvariantCulture),
                Minutes(Text(constraints, "max_duration") ?? Text(defaults, "max_duration")!),
                long.Parse(Text(constraints, "max_tokens") ?? Text(defaults, "max_tokens")!, CultureInfo.InvariantCulture)));
        }

        return tasks;
    }

    private static YamlMappingNode? Map(YamlMappingNode? node, string key) =>
        node is not null && node.Children.TryGetValue(key, out var child) ? child as YamlMappingNode : null;

    private static string? Text(YamlMappingNode? node, string key) =>
        node is not null && node.Children.TryGetValue(key, out var child) ? ((YamlScalarNode)child).Value : null;

    private static string[] List(YamlMappingNode? node, string key) =>
        node is not null && node.Children.TryGetValue(key, out var child) ? [.. ((YamlSequenceNode)child).Select(item => ((YamlScalarNode)item).Value!)] : [];

    private static int Minutes(string text) => text.EndsWith('m') ? int.Parse(text[..^1], CultureInfo.InvariantCulture) : throw new FormatException($"'{text}' is not minutes (20m).");
}

/// <summary>One hidden check's result, from a run's checks.txt.</summary>
sealed record Check(string Name, bool Passed)
{
    public static List<Check> Read(string run)
    {
        var file = Path.Combine(run, "checks.txt");
        return !File.Exists(file)
            ? []
            : [.. File.ReadAllLines(file).Select(line => line.Split(' ')).Where(parts => parts.Length == 3).Select(parts => new Check(parts[1], parts[2] == "pass"))];
    }
}

/// <summary>The docker command line.</summary>
static class Docker
{
    /// <summary>What the window tests need from the container, as eng/linux-ci/run.sh starts it.</summary>
    public static readonly string[] WindowOptions = ["--init", "--shm-size=1g", "--security-opt", "seccomp=unconfined", "--security-opt", "systempaths=unconfined"];

    public static async Task<int> RunAsync(string[] arguments, bool echo = false, string? log = null)
    {
        var start = new ProcessStartInfo("docker", arguments) { RedirectStandardOutput = !echo, RedirectStandardError = !echo };
        using var process = Process.Start(start)!;
        if (!echo)
        {
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            if (log is not null)
            {
                await File.WriteAllTextAsync(log, await output + await error);
            }
        }
        else
        {
            await process.WaitForExitAsync();
        }

        return process.ExitCode;
    }
}

/// <summary>The report: each run's outcome and numbers, the profiles compared, and the surface the rule keeps.</summary>
static class Report
{
    public static int Write(string folder, List<EvalTask> tasks)
    {
        var runs = new List<RunResult>();
        foreach (var task in tasks)
        {
            var taskFolder = Path.Combine(folder, task.Name);
            if (!Directory.Exists(taskFolder))
            {
                continue;
            }

            foreach (var run in Directory.GetDirectories(taskFolder))
            {
                runs.Add(RunResult.Read(task, run));
            }
        }

        File.WriteAllText(Path.Combine(folder, "runs.json"), new JsonArray([.. runs.Select(r => r.ToJson())]).ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

        var text = new StringBuilder();
        text.Append("# Evaluation report\n\n");
        text.Append("| Profile | Runs | Passed | Pass rate | Median tokens | Median time (s) | Median turns | Median cost (USD) |\n|---|---|---|---|---|---|---|---|\n");
        var profiles = runs.GroupBy(r => r.Profile).OrderBy(g => g.Key, StringComparer.Ordinal).ToList();
        foreach (var profile in profiles)
        {
            var list = profile.ToList();
            text.Append(CultureInfo.InvariantCulture, $"| {profile.Key} | {list.Count} | {list.Count(r => r.Passed)} | {Rate(list):P0} | {Median(list.Select(r => (double)r.Tokens)):N0} | {Median(list.Select(r => r.Seconds)):N0} | {Median(list.Select(r => (double)r.Turns)):N0} | {Median(list.Select(r => r.Cost)):N2} |\n");
        }

        text.Append("\n## Per task\n\n| Task | " + string.Join(" | ", profiles.Select(p => p.Key)) + " |\n|---|" + string.Concat(profiles.Select(_ => "---|")) + "\n");
        foreach (var task in tasks)
        {
            var cells = profiles.Select(p =>
            {
                var list = p.Where(r => r.Task == task.Name).ToList();
                return list.Count == 0 ? "–" : $"{list.Count(r => r.Passed)}/{list.Count}";
            });
            text.Append($"| {task.Name} | {string.Join(" | ", cells)} |\n");
        }

        text.Append("\n## Per skill (tasks tagged with it)\n\n| Skill | " + string.Join(" | ", profiles.Select(p => p.Key)) + " |\n|---|" + string.Concat(profiles.Select(_ => "---|")) + "\n");
        foreach (var skill in tasks.SelectMany(t => t.Skills).Distinct().Order(StringComparer.Ordinal))
        {
            var tagged = tasks.Where(t => t.Skills.Contains(skill)).Select(t => t.Name).ToHashSet();
            var cells = profiles.Select(p =>
            {
                var list = p.Where(r => tagged.Contains(r.Task)).ToList();
                return list.Count == 0 ? "–" : $"{Rate(list):P0} ({list.Count(r => r.Passed)}/{list.Count})";
            });
            text.Append($"| {skill} ({tagged.Count} tasks) | {string.Join(" | ", cells)} |\n");
        }

        text.Append("\n## The surviving surface (decision \"M2e's decide-first questions\", item 8)\n\n");
        text.Append(Decide(runs) + "\n");
        File.WriteAllText(Path.Combine(folder, "report.md"), text.ToString());
        Console.WriteLine(text);
        return 0;
    }

    /// <summary>The higher pass rate; within one task (10 points), fewer median tokens; within 10 % of those, the CLI.</summary>
    private static string Decide(List<RunResult> runs)
    {
        var cli = runs.Where(r => r.Profile == "cli").ToList();
        var mcp = runs.Where(r => r.Profile == "mcp").ToList();
        if (cli.Count == 0 || mcp.Count == 0)
        {
            return "Not decided: the run has no cli or no mcp results.";
        }

        var (cliRate, mcpRate) = (Rate(cli), Rate(mcp));
        if (Math.Abs(cliRate - mcpRate) > 0.10 + 1e-9)
        {
            return $"By pass rate: {(cliRate > mcpRate ? "cli" : "mcp")} ({cliRate:P0} against {Math.Min(cliRate, mcpRate):P0}).";
        }

        var (cliTokens, mcpTokens) = (Median(cli.Select(r => (double)r.Tokens)), Median(mcp.Select(r => (double)r.Tokens)));
        var fewer = Math.Min(cliTokens, mcpTokens);
        if (Math.Abs(cliTokens - mcpTokens) > 0.10 * fewer)
        {
            return $"Pass rates within one task ({cliRate:P0}, {mcpRate:P0}); by median tokens: {(cliTokens < mcpTokens ? "cli" : "mcp")} ({fewer:N0} against {Math.Max(cliTokens, mcpTokens):N0}).";
        }

        return $"Pass rates within one task ({cliRate:P0}, {mcpRate:P0}) and median tokens within 10 % ({cliTokens:N0}, {mcpTokens:N0}): cli.";
    }

    private static double Rate(List<RunResult> list) => list.Count == 0 ? 0 : (double)list.Count(r => r.Passed) / list.Count;

    private static double Median(IEnumerable<double> values)
    {
        var sorted = values.Order().ToList();
        return sorted.Count == 0 ? 0 : sorted.Count % 2 == 1 ? sorted[sorted.Count / 2] : (sorted[sorted.Count / 2 - 1] + sorted[sorted.Count / 2]) / 2;
    }
}

/// <summary>
/// One agent run's outcome: its checks, and from Claude Code's result event its turns, time, cost and tokens. Tokens are
/// input, output and cache writes; cache reads, which every turn repeats, are recorded apart and do not count against
/// the cap.
/// </summary>
sealed record RunResult(string Task, string Profile, bool Passed, bool ChecksPassed, bool WithinLimits, long Tokens, long CacheReads, int Turns, double Seconds, double Cost, string Model, int SparCli, int SparMcp, string[] Checks)
{
    public static RunResult Read(EvalTask task, string run)
    {
        var checks = Check.Read(run);
        var checksPassed = checks.Count > 0 && checks.All(c => c.Passed);
        var profile = Path.GetFileName(run).Split('-')[0];
        long tokens = 0, cacheReads = 0;
        int turns = 0, sparCli = 0, sparMcp = 0;
        double seconds = 0, cost = 0;
        var model = "";
        var transcript = Path.Combine(run, "transcript.jsonl");
        if (File.Exists(transcript))
        {
            foreach (var line in File.ReadLines(transcript))
            {
                JsonNode? node;
                try
                {
                    node = JsonNode.Parse(line);
                }
                catch (JsonException)
                {
                    continue;
                }

                if (node?["type"]?.GetValue<string>() == "result")
                {
                    var usage = node["usage"];
                    tokens = Long(usage, "input_tokens") + Long(usage, "output_tokens") + Long(usage, "cache_creation_input_tokens");
                    cacheReads = Long(usage, "cache_read_input_tokens");
                    turns = (int)Long(node, "num_turns");
                    seconds = Long(node, "duration_ms") / 1000.0;
                    cost = node["total_cost_usd"]?.GetValue<double>() ?? 0;
                    model = string.Join(",", node["modelUsage"]?.AsObject().Select(p => p.Key) ?? []);
                }

                if (node?["type"]?.GetValue<string>() == "assistant" && node["message"]?["content"] is JsonArray content)
                {
                    foreach (var block in content.OfType<JsonObject>().Where(b => b["type"]?.GetValue<string>() == "tool_use"))
                    {
                        var name = block["name"]?.GetValue<string>() ?? "";
                        sparMcp += name.StartsWith("mcp__spar__", StringComparison.Ordinal) ? 1 : 0;
                        var command = block["input"]?["command"]?.ToString() ?? "";
                        sparCli += name == "Bash" && command.Contains("dotnet spar ", StringComparison.Ordinal) ? 1 : 0;
                    }
                }
            }
        }

        var within = tokens <= task.MaxTokens && turns <= task.MaxTurns && seconds <= task.MaxMinutes * 60;
        return new RunResult(task.Name, profile, checksPassed && within, checksPassed, within, tokens, cacheReads, turns, seconds, cost, model, sparCli, sparMcp,
            [.. checks.Select(c => $"{c.Name} {(c.Passed ? "pass" : "fail")}")]);
    }

    public JsonObject ToJson() => new()
    {
        ["task"] = Task, ["profile"] = Profile, ["passed"] = Passed, ["checksPassed"] = ChecksPassed, ["withinLimits"] = WithinLimits,
        ["tokens"] = Tokens, ["cacheReads"] = CacheReads, ["turns"] = Turns, ["seconds"] = Seconds, ["cost"] = Cost, ["model"] = Model,
        ["sparCli"] = SparCli, ["sparMcp"] = SparMcp, ["checks"] = new JsonArray([.. Checks.Select(c => JsonValue.Create(c))]),
    };

    private static long Long(JsonNode? node, string key) => node?[key] is JsonValue value && value.TryGetValue<long>(out var number) ? number : 0;
}
