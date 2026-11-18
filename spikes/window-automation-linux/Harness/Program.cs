using System.Diagnostics;
using System.Globalization;
using System.Runtime.Versioning;
using Spar.WindowAutomation.Linux;
using Spar.WindowAutomation.Linux.Automation;
using Spar.WindowAutomation.Linux.Native;

[assembly: SupportedOSPlatform("linux")]

// The Linux window-test harness. Verbs:
//   journey --runs N [--screenshots] [--input xtest|script] [--focus auto|never|always] [--fill keys|insert]
//   actions | coverage | isolation | tree [--hold FILE] | serve --seconds N | webdriver-host
// Common: --page DIR (the built page), --out DIR (PNGs), --csp (send a strict Content-Security-Policy).
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
var processStarted = Process.GetCurrentProcess().StartTime.ToUniversalTime();
var verb = args.FirstOrDefault() ?? "";
var options = new Options(args.Skip(1));
if (Environment.GetEnvironmentVariable("GSK_RENDERER") is null)
{
    // Decision "Software GPU default on Linux": GTK4's cairo renderer and WebKit acceleration off (set per view).
    Libc.setenv("GSK_RENDERER", "cairo", 0);
}

if (Environment.GetEnvironmentVariable("GDK_BACKEND") != "x11")
{
    Console.Error.WriteLine("GDK_BACKEND=x11 is required (XTEST input)");
    return 2;
}

Session.PageFolder = Path.GetFullPath(options.Get("page") ?? Path.Combine(AppContext.BaseDirectory, "page")) + Path.DirectorySeparatorChar;
Session.Csp = options.Has("csp") ? "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; connect-src 'self'; object-src 'none'; base-uri 'none'" : null;
Session.DeveloperExtras = options.Has("devtools");
var output = options.Get("out") ?? Path.Combine(Environment.CurrentDirectory, "out");
Directory.CreateDirectory(output);
var clock = Stopwatch.StartNew();
Ui.Start();
await Ui.Invoke(() => Session.Initialize(automationAllowed: verb == "webdriver-host"));
using var x = new XInput();
IInput input = options.Get("input") == "script"
    ? new ScriptInput(EngineCall)
    : new XtestInput(x, options.Get("focus") ?? "auto");
Log.Write("SETUP", $"runtime={(File.Exists(Path.Combine(AppContext.BaseDirectory, "Harness.dll")) ? "jit" : "aot")} gtk+context={clock.Elapsed.TotalMilliseconds:F1}ms " +
    $"since-process-start={(DateTime.UtcNow - processStarted).TotalMilliseconds:F0}ms xtest={x.Version} input={input.Name} csp={Session.Csp is not null}");

var exitCode = 64;
try
{
    exitCode = verb switch
    {
        "journey" => await JourneyRunsAsync(),
        "actions" => await Probes.ActionsAsync(input, output),
        "coverage" => await Probes.CoverageAsync(input, x, output),
        "isolation" => await Probes.IsolationAsync(input),
        "activation" => await Probes.ActivationAsync(x),
        "tree" => await Probes.TreeAsync(input, options.Get("hold"), output),
        "serve" => await Probes.ServeAsync(options.Int("seconds", 30), output),
        "webdriver-host" => await Probes.WebDriverHostAsync(options.Int("seconds", 120)),
        _ => 64,
    };
}
catch (Exception exception)
{
    Log.Write("FATAL", exception.ToString().ReplaceLineEndings(" | "));
    exitCode = 1;
}
finally
{
    Ui.Stop();
    await Task.Delay(300);
    Log.Write("TEARDOWN", $"WebKitWebProcess={Processes.Count("WebKitWebProces")} WebKitNetworkProcess={Processes.Count("WebKitNetworkPr")} (names truncated by the kernel; counts include other runs)");
}

return exitCode;

static Task<System.Text.Json.JsonElement> EngineCall(View view, string request) =>
    view.CallAsync("return globalThis.__sparAutomation.run(" + request + ");").ContinueWith(t => WindowPage.Parse(t.Result), TaskScheduler.Default);

async Task<int> JourneyRunsAsync()
{
    var runs = options.Int("runs", 10);
    var insert = options.Get("fill") == "insert";
    var steps = new Timings();
    var test = new Timings();
    var failures = 0;
    for (var run = 0; run < runs; run++)
    {
        var started = Stopwatch.GetTimestamp();
        var view = await test.TimeAsync("create window + view + session", () => View.CreateAsync());
        try
        {
            var page = WindowPage.Attach(view, input);
            await test.TimeAsync("navigate (load, present)", () => view.NavigateAsync(Session.Origin + "/"));
            var screenshot = options.Has("screenshots") && run == 0 ? Path.Combine(output, $"linux-{input.Name}-settings.png") : null;
            await test.TimeAsync("journey", () => Journey.RunAsync(page, steps, screenshot, insert));
            if (run == 0)
            {
                Log.Write("MEMORY", $"test-process VmRSS={Processes.StatusKilobytes("self", "VmRSS") / 1024.0:F1}MB RssAnon={Processes.StatusKilobytes("self", "RssAnon") / 1024.0:F1}MB web processes now={Processes.Count("WebKitWebProces")} web VmRSS={Processes.RssKilobytes("WebKitWebProces") / 1024.0:F1}MB network VmRSS={Processes.RssKilobytes("WebKitNetworkPr") / 1024.0:F1}MB");
            }

            if (page.PageErrors.Count > 0)
            {
                throw new InvalidOperationException("page errors: " + string.Join(" | ", page.PageErrors));
            }
        }
        catch (Exception exception)
        {
            failures++;
            Log.Write("FAIL", $"run={run} {exception.GetType().Name}: {exception.Message.ReplaceLineEndings(" ")}");
            await view.CallAsync("return document.title + ' ' + location.href", world: null).ContinueWith(t => Log.Write("FAIL-PAGE", t.IsCompletedSuccessfully ? t.Result ?? "" : "?"));
        }
        finally
        {
            await test.TimeAsync("close", view.CloseAsync);
            test.Add("per test", Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }

    }

    test.Report("TEST");
    steps.Report("STEP");
    foreach (var (reason, count) in WindowPage.RetryReasons.OrderByDescending(r => r.Value))
    {
        Log.Write("RETRY", $"{count,5} x {reason}");
    }

    foreach (var (problem, count) in WindowPage.InputProblems.OrderByDescending(r => r.Value))
    {
        Log.Write("INPUT-PROBLEM", $"{count,5} x {problem}");
    }

    if (input is XtestInput xtest)
    {
        Log.Write("FOCUS", $"key strokes that moved the X focus first: {xtest.FocusMoves}; remapped characters: {x.Remapped}");
    }

    Log.Write("RESULT", $"runs={runs} failures={failures} input={input.Name} fill={(insert ? "insert" : "keys")}");
    return failures == 0 ? 0 : 1;
}
