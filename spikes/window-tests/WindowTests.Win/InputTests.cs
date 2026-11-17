using System.Diagnostics;

namespace Spar.WindowTests;

/// <summary>
/// Question 2: trusted input through in-process CDP in a shown, offscreen, never-activated window. Records what the
/// page saw for each action, whether user-activation-gated APIs accept it, latency per action, and the first-input
/// stall after a navigation.
/// </summary>
internal static class InputTests
{
    private const string Page = "https://app.localhost/input.html";

    public static async Task<int> RunAsync(Options options)
    {
        var root = Paths.Udf(options.Get("root") ?? "input");
        var iterations = options.Int("iterations", 100);
        var foregroundAtStart = Ui.Foreground();
        var env = await Env.CreateAsync(Path.Combine(root, "udf"));
        var newWindows = new List<string>();
        var view = await View.CreateAsync(env, "default", beforeFirstUse: v =>
        {
            v.OnNewWindowRequested(args =>
            {
                newWindows.Add($"uri={args.GetUri()} user={args.GetIsUserInitiated()}");
                args.PutHandled(1);
            });
            return Task.CompletedTask;
        });
        Log.Write("START", $"window=0x{view.WindowHandle:X} foreground=0x{foregroundAtStart:X} isOurs={foregroundAtStart == view.WindowHandle}");

        await view.NavigateAsync(Page);
        await view.TwoFramesAsync();
        Log.Write("BASELINE", $"state={await State(view)}");

        // Activation-gated APIs before any input, through Runtime.evaluate (not a user gesture).
        Log.Write("GATED-NO-INPUT", $"fullscreen={await view.EvaluateTextAsync("tryFullscreen()")} open={await view.EvaluateTextAsync("tryOpen()", awaitPromise: false)} " +
            $"newWindows=[{string.Join(';', newWindows)}] state={await State(view)}");
        newWindows.Clear();

        // 1. A trusted click on a button.
        await view.ClickAsync("#b");
        Log.Write("CLICK", $"events={await Events(view)} state={await State(view)}");

        // 2. Click into the first field, then Input.insertText.
        await view.ClickAsync("#t1");
        await view.InsertTextAsync("hello");
        Log.Write("INSERT-TEXT", $"events={await Events(view)} state={await State(view)}");

        // 3. Key events with text: one character.
        await view.KeyAsync("x", "KeyX", 0x58, "x");
        Log.Write("KEY-CHAR", $"events={await Events(view)} state={await State(view)}");

        // 4. Tab moves focus to the second field.
        await view.KeyAsync("Tab", "Tab", 0x09);
        Log.Write("KEY-TAB", $"events={await Events(view)} state={await State(view)}");

        // 5. Activation-gated APIs from trusted clicks.
        await view.ClickAsync("#fs");
        await Task.Delay(500);
        await view.ClickAsync("#pop");
        await Task.Delay(300);
        Log.Write("GATED-TRUSTED", $"gated={await view.EvaluateTextAsync("JSON.stringify(window.gated)", awaitPromise: false)} " +
            $"newWindows=[{string.Join(';', newWindows)}] state={await State(view)}");
        newWindows.Clear();
        await Events(view);

        // The same APIs from Runtime.evaluate with userGesture=true (the non-trusted route), for comparison.
        await view.NavigateAsync(Page);
        await view.TwoFramesAsync();
        Log.Write("GATED-USERGESTURE", $"fullscreen={await view.EvaluateTextAsync("tryFullscreen()", userGesture: true)} " +
            $"open={await view.EvaluateTextAsync("tryOpen()", awaitPromise: false, userGesture: true)} newWindows=[{string.Join(';', newWindows)}] state={await State(view)}");
        newWindows.Clear();

        // 6. Focus emulation: does it change hasFocus and :focus?
        await view.NavigateAsync(Page);
        await view.TwoFramesAsync();
        await view.ClickAsync("#t1");
        Log.Write("FOCUS-PLAIN", $"state={await State(view)}");
        await view.CallAsync("Emulation.setFocusEmulationEnabled", """{"enabled":true}""");
        await view.ClickAsync("#t1");
        Log.Write("FOCUS-EMULATED", $"state={await State(view)} events={await Events(view)}");
        await view.CallAsync("Emulation.setFocusEmulationEnabled", """{"enabled":false}""");

        // 7. Latency per action, 100 each, on a fresh page.
        await view.NavigateAsync(Page);
        await view.TwoFramesAsync();
        var (bx, by) = await view.CentreAsync("#b");
        var (tx, ty) = await view.CentreAsync("#t1");
        await view.ClickAtAsync(tx, ty);
        var timings = new Dictionary<string, List<double>>
        {
            ["click (3 mouse events, known point)"] = [],
            ["click with box lookup (evaluate + 3 events)"] = [],
            ["insertText (5 chars)"] = [],
            ["key char (keyDown + keyUp)"] = [],
            ["Tab (rawKeyDown + keyUp)"] = [],
        };
        for (var i = 0; i < iterations; i++)
        {
            var started = Stopwatch.GetTimestamp();
            await view.ClickAtAsync(bx, by);
            timings["click (3 mouse events, known point)"].Add(Time.Ms(started));

            started = Stopwatch.GetTimestamp();
            await view.ClickAsync("#b");
            timings["click with box lookup (evaluate + 3 events)"].Add(Time.Ms(started));

            await view.ClickAtAsync(tx, ty);
            started = Stopwatch.GetTimestamp();
            await view.InsertTextAsync("abcde");
            timings["insertText (5 chars)"].Add(Time.Ms(started));

            started = Stopwatch.GetTimestamp();
            await view.KeyAsync("y", "KeyY", 0x59, "y");
            timings["key char (keyDown + keyUp)"].Add(Time.Ms(started));

            started = Stopwatch.GetTimestamp();
            await view.KeyAsync("Tab", "Tab", 0x09);
            timings["Tab (rawKeyDown + keyUp)"].Add(Time.Ms(started));

            if (i % 20 == 19)
            {
                await view.EvaluateAsync("document.getElementById('t1').value = ''; window.events.length = 0; 0", awaitPromise: false);
            }
        }

        var check = await view.EvaluateTextAsync(
            "JSON.stringify({clicks: 0, t1Length: document.getElementById('t1').value.length, trustedAll: window.events.every(e => e.trusted), count: window.events.length})",
            awaitPromise: false);
        foreach (var (name, values) in timings)
        {
            Log.Write("LATENCY", $"action=[{name}] {Stats.Summary(values)}");
        }

        Log.Write("LATENCY-CHECK", check);

        // 8. First trusted input right after a navigation, with and without a two-frame wait: on the same controller,
        // on a new InPrivate controller each time, and on a new user-data folder each time (a new browser).
        var stallRuns = options.Int("stall-runs", 20);
        foreach (var fresh in new[] { "same", "controller", "udf" })
        {
            foreach (var wait in new[] { false, true })
            {
                var values = new List<double>();
                for (var i = 0; i < stallRuns; i++)
                {
                    var target = view;
                    Env? stallEnv = null;
                    if (fresh == "controller")
                    {
                        target = await View.CreateAsync(env, "~private");
                    }
                    else if (fresh == "udf")
                    {
                        stallEnv = await Env.CreateAsync(Path.Combine(root, $"stall-{wait}-{i}"));
                        target = await View.CreateAsync(stallEnv, "default");
                    }

                    await target.NavigateAsync(Page + "?n=" + i);
                    if (wait)
                    {
                        await target.TwoFramesAsync();
                    }

                    var started = Stopwatch.GetTimestamp();
                    await target.ClickAtAsync(bx, by);
                    var ms = Time.Ms(started);
                    values.Add(ms);
                    var seen = await target.EvaluateTextAsync("String(window.events.filter(e => e.type === 'click' && e.trusted).length)", awaitPromise: false);
                    if (ms > 1000 || seen != "1")
                    {
                        Log.Write("FIRST-INPUT-SLOW", $"fresh={fresh} wait={wait} i={i} ms={ms:F1} clicksSeen={seen}");
                    }

                    if (target != view)
                    {
                        target.Close();
                    }

                    if (stallEnv is not null)
                    {
                        await Task.WhenAny(stallEnv.Exited.Task, Task.Delay(10000));
                        await ProfileTests.WaitForNoProcessesAsync(stallEnv.Path, 10000);
                        Paths.TryDelete(stallEnv.Path, out _);
                    }
                }

                Log.Write("FIRST-INPUT", $"fresh={fresh} twoFrames={wait} {Stats.Summary(values)} over1s={values.Count(v => v > 1000)}");
            }
        }

        var foregroundAtEnd = Ui.Foreground();
        Log.Write("END", $"foregroundAtEnd=0x{foregroundAtEnd:X} unchanged={foregroundAtEnd == foregroundAtStart} isOurs={foregroundAtEnd == view.WindowHandle}");
        view.Close();
        var exited = await Task.WhenAny(env.Exited.Task, Task.Delay(15000)) == env.Exited.Task;
        await ProfileTests.WaitForNoProcessesAsync(env.Path, 15000);
        Log.Write("CLEANUP", $"browserExited={exited} deleted={Paths.TryDelete(root, out var error)} {error}");
        return 0;
    }

    private static Task<string> State(View view) => view.EvaluateTextAsync("JSON.stringify(state())", awaitPromise: false);

    private static Task<string> Events(View view) =>
        view.EvaluateTextAsync("(() => { const e = JSON.stringify(window.events.map(({t, ...rest}) => rest)); window.events.length = 0; return e; })()", awaitPromise: false);
}
