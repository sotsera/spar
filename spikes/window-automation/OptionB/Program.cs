using System.Diagnostics;
using System.Globalization;
using System.Runtime.Versioning;
using Spar.WindowAutomation;
using Spar.WindowAutomation.Automation;
using static Spar.WindowAutomation.Automation.Assertions;

[assembly: SupportedOSPlatform("windows10.0")]

// Option B: window tests driven by Spar's own small API over the in-process DevTools session. Verbs:
//   journey --runs N [--screenshots]   the orders journey N times, one InPrivate controller per test
//   coverage                            the primitives outside the journey (errors, dialogs, files, screenshots)
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
var processStarted = Process.GetCurrentProcess().StartTime.ToUniversalTime();
var options = new Options(args.Skip(1));
View.PageFolder = Paths.Page;
Directory.CreateDirectory(Paths.Output);
Ui.Start();
var clock = Stopwatch.StartNew();
var root = Paths.NewRoot("b");
var env = await Env.CreateAsync(root, remoteDebugging: false, options.Get("anchor") ?? "default");
Log.Write("SETUP", $"runtime={(File.Exists(Path.Combine(AppContext.BaseDirectory, "OptionB.dll")) ? "jit" : "aot")} env+anchor={clock.Elapsed.TotalMilliseconds:F1}ms since-process-start={(DateTime.UtcNow - processStarted).TotalMilliseconds:F0}ms");
var exitCode = 0;
try
{
    exitCode = args.FirstOrDefault() switch
    {
        "journey" => await JourneyRunsAsync(),
        "coverage" => await CoverageAsync(),
        "isolation" => await IsolationAsync(),
        "actions" => await ActionsAsync(),
        _ => 64,
    };
}
finally
{
    await env.CloseAsync();
    await env.Exited.Task.WaitAsync(TimeSpan.FromSeconds(10)).ContinueWith(_ => { });
    Log.Write("TEARDOWN", $"browser exited={env.Exited.Task.IsCompleted} leftover={Native.BrowserProcesses(env.Marker).Count} root deleted={await Paths.DeleteRootAsync(root)}");
    Ui.Stop();
}

return exitCode;

async Task<int> JourneyRunsAsync()
{
    var runs = options.Int("runs", 10);
    var steps = new Timings();
    var test = new Timings();
    var failures = 0;
    for (var run = 0; run < runs; run++)
    {
        var started = Stopwatch.GetTimestamp();
        var view = await test.TimeAsync("create controller", () => View.CreateAsync(env));
        try
        {
            var page = await test.TimeAsync("attach", () => WindowPage.AttachAsync(view));
            await test.TimeAsync("navigate", () => view.NavigateAsync(View.Origin + "/"));
            var screenshot = options.Has("screenshots") && run == 0 ? Path.Combine(Paths.Output, "b-settings.png") : null;
            await test.TimeAsync("journey", () => Journey.RunAsync(page, steps, screenshot));
            if (page.PageErrors.Count > 0)
            {
                throw new InvalidOperationException("page errors: " + string.Join(" | ", page.PageErrors));
            }
        }
        catch (Exception exception)
        {
            failures++;
            Log.Write("FAIL", $"run={run} {exception.GetType().Name}: {exception.Message.ReplaceLineEndings(" ")}");
        }
        finally
        {
            await test.TimeAsync("close", view.CloseAsync);
            test.Add("per test", Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }

        if (run == 0)
        {
            Log.Write("MEMORY", $"browser={Native.BrowserPrivateBytes(env.Marker) / 1048576.0:F1}MB test-process={Process.GetCurrentProcess().PrivateMemorySize64 / 1048576.0:F1}MB");
        }
    }

    test.Report("TEST");
    steps.Report("STEP");
    foreach (var (reason, count) in WindowPage.RetryReasons.OrderByDescending(r => r.Value))
    {
        Log.Write("RETRY", $"{count,5} x {reason}");
    }

    Log.Write("RESULT", $"runs={runs} failures={failures}");
    return failures == 0 ? 0 : 1;
}

// Where a click's time goes: resolve by role, the actionability check (two frames), the three mouse events.
async Task<int> ActionsAsync()
{
    var view = await View.CreateAsync(env);
    var page = await WindowPage.AttachAsync(view);
    await view.NavigateAsync(View.Origin + "/");
    await page.WaitForUiReadyAsync();
    var t = new Timings();
    for (var i = 0; i < 100; i++)
    {
        var attempt = new Attempt(page.Cdp, "bench-" + i);
        var button = await t.TimeAsync("resolve role (queryAXTree + resolveNode)", async () => (await page.GetByRole("button", "Actions").ResolveAsync(attempt))[0]);
        await page.EvaluateAsync("document.querySelector('[role=status]').textContent = 'changed ' + Math.random()");
        await t.TimeAsync("resolve role right after a DOM change", () => page.GetByRole("status").ResolveAsync(attempt));
        await page.EvaluateAsync("document.querySelector('[role=status]').textContent = 'changed ' + Math.random()");
        await t.TimeAsync("resolve label right after a DOM change", () => page.GetByLabel("Customer name").ResolveAsync(attempt));
        await t.TimeAsync("resolve label (script + getProperties)", () => page.GetByLabel("Customer name").ResolveAsync(attempt));
        var check = await t.TimeAsync("actionable, pointer (1 frame)", () => page.Cdp.CallFunctionAsync(button, Scripts.Actionable, attempt.Group, awaitPromise: true, arguments: ["true", "false", "true"]));
        await t.TimeAsync("actionable, no pointer", () => page.Cdp.CallFunctionAsync(button, Scripts.Actionable, attempt.Group, awaitPromise: true, arguments: ["true", "false", "false"]));
        await t.TimeAsync("mouse moved", () => page.Mouse.MoveAsync(5, 5));
        await t.TimeAsync("3 mouse events (empty area)", () => page.Mouse.ClickAsync(5, 5));
        await t.TimeAsync("key press (keyDown+keyUp)", () => page.Keyboard.PressAsync("Escape"));
        await t.TimeAsync("Runtime.evaluate 1+1", () => page.EvaluateAsync("1+1"));
        await t.TimeAsync("release group", () => page.Cdp.ReleaseGroupAsync(attempt.Group));
        await t.TimeAsync("full ClickAsync (open+close menu)", async () =>
        {
            await page.GetByRole("button", "Actions").ClickAsync();
            await page.Keyboard.PressAsync("Escape");
        });
        var current = (await page.EvaluateAsync("document.querySelector('[role=status]').textContent")).GetString()!;
        attempt = new Attempt(page.Cdp, "bench-state-" + i);
        await t.TimeAsync("state (resolve + State script)", () => page.GetByRole("status").StateAsync(attempt));
        var statusElement = (await page.GetByRole("status").ResolveAsync(attempt))[0];
        await t.TimeAsync("State script only", () => page.Cdp.CallFunctionAsync(statusElement, Scripts.State, attempt.Group));
        await t.TimeAsync("Describe script only", () => page.Cdp.CallFunctionAsync(statusElement, Scripts.Describe, attempt.Group));
        await t.TimeAsync("expect text (already true, idle) ", () => Expect(page.GetByRole("status")).ToHaveTextAsync(current));
        await page.GetByRole("button", "Actions").ClickAsync();
        await page.Keyboard.PressAsync("Escape");
        await t.TimeAsync("expect text right after the menu closed", () => Expect(page.GetByRole("status")).ToHaveTextAsync(current));
        await t.TimeAsync("screenshot (viewport)", () => page.CaptureAsync());
    }

    t.Report("ACTION");
    foreach (var (reason, count) in WindowPage.RetryReasons.OrderByDescending(r => r.Value))
    {
        Log.Write("RETRY", $"{count,5} x {reason}");
    }

    await view.CloseAsync();
    return 0;
}

async Task<int> IsolationAsync()
{
    const string Read = "(localStorage.getItem('k') ?? '-') + ' cookie=' + (document.cookie || '-')";
    async Task<(View View, WindowPage Page)> OpenAsync(string? profileName = null)
    {
        var view = await View.CreateAsync(env, profileName: profileName);
        var page = await WindowPage.AttachAsync(view);
        await view.NavigateAsync(View.Origin + "/");
        return (view, page);
    }

    Log.Write("ISOLATION", $"anchor={options.Get("anchor") ?? "default"}");
    for (var test = 0; test < 5; test++)
    {
        var started = Stopwatch.GetTimestamp();
        var (view, page) = await OpenAsync();
        var opened = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        var found = (await page.EvaluateAsync(Read)).GetString();
        await page.EvaluateAsync($"localStorage.setItem('k', 'test{test}'); document.cookie = 'c=test{test}; Secure'");
        await view.CloseAsync();
        Log.Write("ISOLATION", $"consecutive test {test}: open={opened:F0} ms found=[{found}]");
    }

    foreach (var names in new[] { new string?[] { null, null }, new string?[] { "t1", "t2" } })
    {
        var (a, pageA) = await OpenAsync(names[0]);
        var (b, pageB) = await OpenAsync(names[1]);
        await pageA.EvaluateAsync("localStorage.setItem('k', 'from-a'); document.cookie = 'c=from-a; Secure'");
        var found = (await pageB.EvaluateAsync(Read)).GetString();
        Log.Write("ISOLATION", $"simultaneous InPrivate, profile names {names[0] ?? "(default)"}/{names[1] ?? "(default)"}: b found=[{found}]");
        await a.CloseAsync();
        await b.CloseAsync();
    }

    return 0;
}

async Task<int> CoverageAsync()
{
    var view = await View.CreateAsync(env);
    var page = await WindowPage.AttachAsync(view);
    await view.NavigateAsync(View.Origin + "/");
    await page.WaitForUiReadyAsync();
    async Task Check(string name, Func<Task<string>> probe)
    {
        var started = Stopwatch.GetTimestamp();
        try
        {
            var detail = await probe();
            Log.Write("COVER", $"{name}: ok ({Stopwatch.GetElapsedTime(started).TotalMilliseconds:F1} ms) {detail}");
        }
        catch (Exception exception)
        {
            Log.Write("COVER", $"{name}: FAILED {exception.GetType().Name}: {exception.Message.ReplaceLineEndings(" ")}");
        }
    }

    await Check("evaluate", async () => "title=" + (await page.EvaluateAsync("document.title")).GetString());
    await Check("getByText exact", async () => "count=" + await page.GetByText("Recent orders", exact: true).CountAsync());
    await Check("hover", async () =>
    {
        await page.GetByRole("button", "Show alert").HoverAsync();
        return "";
    });
    await Check("element screenshot", async () =>
    {
        var png = await page.GetByRole("form", "New order").ScreenshotAsync();
        await File.WriteAllBytesAsync(Path.Combine(Paths.Output, "b-form.png"), png);
        return $"{png.Length} bytes";
    });
    await Check("timeout message (wrong text)", async () =>
    {
        try
        {
            await Expect(page.GetByRole("status")).ToHaveTextAsync("Shipped", timeout: 300);
            return "no error?";
        }
        catch (WaitTimeoutException exception)
        {
            return "message: " + exception.Message;
        }
    });
    await Check("timeout message (disabled button)", async () =>
    {
        try
        {
            await page.GetByRole("button", "Place order").ClickAsync(timeout: 300);
            return "no error?";
        }
        catch (WaitTimeoutException exception)
        {
            return "message: " + exception.Message;
        }
    });
    await Check("strict mode message", async () =>
    {
        try
        {
            await page.GetByRole("button").ClickAsync(timeout: 300);
            return "no error?";
        }
        catch (InvalidOperationException exception)
        {
            return "message: " + exception.Message[..Math.Min(220, exception.Message.Length)];
        }
    });
    await Check("console and page errors", async () =>
    {
        await page.GetByRole("button", "Log error").ClickAsync();
        var deadline = Stopwatch.StartNew();
        while (page.PageErrors.Count == 0 && deadline.ElapsedMilliseconds < 2000)
        {
            await Task.Delay(10);
        }

        return $"console={page.ConsoleMessages.Count} errors={page.PageErrors.Count} after {deadline.ElapsedMilliseconds} ms; " +
            $"console[0] has 'disk full'={page.ConsoleMessages.FirstOrDefault()?.Contains("disk full")} error has 'Unhandled boom'={page.PageErrors.FirstOrDefault()?.Contains("Unhandled boom")}";
    });
    await Check("set input files (DOM.setFileInputFiles)", async () =>
    {
        var file = Path.Combine(Paths.Output, "upload.txt");
        await File.WriteAllTextAsync(file, "spike");
        await page.GetByLabel("Attachment").SetInputFilesAsync([file]);
        await Expect(page.GetByRole("status")).ToHaveTextAsync("Attached 1 file(s)");
        return $"host interceptions so far={view.FileChoosersIntercepted}";
    });
    await Check("click on file input (host's picker block)", async () =>
    {
        var before = view.FileChoosersIntercepted;
        var watch = DialogWatch.Start(view);
        await page.GetByLabel("Attachment").ClickAsync();
        await Task.Delay(1000);
        var dialogs = watch.Stop();
        return $"intercepted={view.FileChoosersIntercepted - before} native dialogs={dialogs}";
    });
    await Check("alert (Page.javascriptDialogOpening, accepted)", async () =>
    {
        var watch = DialogWatch.Start(view);
        await page.GetByRole("button", "Show alert").ClickAsync();
        await Expect(page.GetByRole("status")).ToHaveTextAsync("Alert closed");
        var dialogs = watch.Stop();
        return $"dialog events={page.Dialogs.Count} native dialogs={dialogs}";
    });
    await view.CloseAsync();

    // Controls: alert() when nobody answers Page.javascriptDialogOpening, with and without the host's Page.enable
    // (as Spar's host leaves it in every build: the browser's own script dialogs on, Page.enable for the picker).
    foreach (var pageEnabled in new[] { true, false })
    {
        await Check($"alert with nobody answering (host Page.enable={pageEnabled})", async () =>
        {
            var raw = await View.CreateAsync(env, interceptPicker: pageEnabled);
            try
            {
                var cdp = new Cdp(raw);
                var dialogEvents = 0;
                await Ui.Invoke(() => raw.Subscribe("Page.javascriptDialogOpening", _ => Interlocked.Increment(ref dialogEvents)));
                await raw.NavigateAsync(View.Origin + "/");
                await cdp.EvaluateAsync(Scripts.TwoFrames);
                var box = await cdp.EvaluateAsync("(() => { const b = [...document.querySelectorAll('button')].find(b => b.textContent === 'Show alert'); b.scrollIntoView({ block: 'center', behavior: 'instant' }); const r = b.getBoundingClientRect(); return [r.x + r.width / 2, r.y + r.height / 2]; })()");
                var watch = WindowWatch.Start(raw);
                var click = new Mouse(cdp).ClickAsync(box[0].GetDouble(), box[1].GetDouble());
                var clicked = await Task.WhenAny(click, Task.Delay(3000)) == click;
                var status = cdp.EvaluateAsync("document.querySelector('[role=status]').textContent", awaitPromise: false);
                var answered = await Task.WhenAny(status, Task.Delay(2000)) == status;
                var dialogs = watch.Stop();
                return $"click returned={clicked} dialog events={dialogEvents} new windows=[{dialogs}] page answered={answered}{(answered ? " status=" + status.Result.GetString() : "")}";
            }
            finally
            {
                await raw.CloseAsync();
            }
        });
    }

    return 0;
}
