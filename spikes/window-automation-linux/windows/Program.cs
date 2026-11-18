using System.Diagnostics;
using System.Globalization;
using System.Runtime.Versioning;
using System.Text.Json;
using Spar.WindowAutomation;
using Spar.WindowAutomation.Automation;
using static Spar.WindowAutomation.Automation.Assertions;

[assembly: SupportedOSPlatform("windows10.0")]

// The Windows spike's Option B with role queries through the shared page-script engine. Verbs:
//   journey --runs N [--engine]   the orders journey (queryAXTree, or the engine with --engine)
//   actions                       role lookups both ways, 100 each
//   tree --out DIR                the engine's tree and Chromium's (Accessibility.getFullAXTree) in three page states
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
var options = new Options(args.Skip(1));
WindowPage.UseEngine = options.Has("engine") || args.FirstOrDefault() is "actions" or "tree";
View.PageFolder = Paths.Page;
Ui.Start();
var root = Paths.NewRoot("engine");
var env = await Env.CreateAsync(root, remoteDebugging: false, "default");
Log.Write("SETUP", $"runtime={(File.Exists(Path.Combine(AppContext.BaseDirectory, "OptionB.Engine.dll")) ? "jit" : "aot")} role queries={(WindowPage.UseEngine ? "engine" : "queryAXTree")}");
var exitCode = 0;
try
{
    exitCode = args.FirstOrDefault() switch
    {
        "journey" => await JourneyRunsAsync(),
        "actions" => await ActionsAsync(),
        "tree" => await TreeAsync(options.Get("out") ?? "."),
        _ => 64,
    };
}
finally
{
    await env.CloseAsync();
    await env.Exited.Task.WaitAsync(TimeSpan.FromSeconds(10)).ContinueWith(_ => { });
    Log.Write("TEARDOWN", $"browser exited={env.Exited.Task.IsCompleted} root deleted={await Paths.DeleteRootAsync(root)}");
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
            await test.TimeAsync("journey", () => Journey.RunAsync(page, steps, null));
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
    }

    test.Report("TEST");
    steps.Report("STEP");
    Log.Write("RESULT", $"runs={runs} failures={failures} role queries={(WindowPage.UseEngine ? "engine" : "queryAXTree")}");
    return failures == 0 ? 0 : 1;
}

async Task<int> ActionsAsync()
{
    var view = await View.CreateAsync(env);
    var page = await WindowPage.AttachAsync(view);
    await view.NavigateAsync(View.Origin + "/");
    await page.WaitForUiReadyAsync();
    await Expect(page.GetByRole("list", "Recent orders").GetByRole("listitem")).ToHaveCountAsync(3);
    var t = new Timings();
    const string Change = "document.querySelector('[role=status]').textContent = 'changed ' + Math.random()";
    for (var i = 0; i < 100; i++)
    {
        foreach (var engine in new[] { false, true })
        {
            WindowPage.UseEngine = engine;
            var how = engine ? "engine" : "queryAXTree";
            var attempt = new Attempt(page.Cdp, $"bench-{how}-{i}");
            await t.TimeAsync($"resolve role button \"Actions\" ({how})", () => page.GetByRole("button", "Actions").ResolveAsync(attempt));
            await page.EvaluateAsync(Change);
            await t.TimeAsync($"resolve role right after a DOM change ({how})", () => page.GetByRole("status").ResolveAsync(attempt));
            await t.TimeAsync($"resolve listitems in the named list ({how})", () => page.GetByRole("list", "Recent orders").GetByRole("listitem").ResolveAsync(attempt));
            var current = (await page.EvaluateAsync("document.querySelector('[role=status]').textContent")).GetString()!;
            await t.TimeAsync($"expect text, already true ({how})", () => Expect(page.GetByRole("status")).ToHaveTextAsync(current));
            await t.TimeAsync($"full ClickAsync (open menu) + Escape ({how})", async () =>
            {
                await page.GetByRole("button", "Actions").ClickAsync();
                await page.Keyboard.PressAsync("Escape");
            });
            await page.Cdp.ReleaseGroupAsync(attempt.Group);
        }
    }

    t.Report("ACTION");
    await view.CloseAsync();
    return 0;
}

// Chromium's accessibility tree, as role/name per element (its index in document.querySelectorAll('*')), next to the
// engine's, in the same three states as the Linux run (orders page, menu open, dialog open).
async Task<int> TreeAsync(string output)
{
    Directory.CreateDirectory(output);
    var view = await View.CreateAsync(env);
    var page = await WindowPage.AttachAsync(view);
    await view.NavigateAsync(View.Origin + "/");
    await page.WaitForUiReadyAsync();
    await Expect(page.GetByRole("list", "Recent orders").GetByRole("listitem")).ToHaveCountAsync(3);
    await page.GetByLabel("Customer name").FillAsync("Ada Lovelace");
    async Task DumpAsync(string state)
    {
        await page.WaitForUiReadyAsync();
        var engine = await page.EvaluateAsync("globalThis.__sparAutomation.run({ op: 'tree' })");
        await File.WriteAllTextAsync(Path.Combine(output, $"tree-engine-{state}.json"), engine.GetRawText());
        var started = Stopwatch.GetTimestamp();
        var full = await page.Cdp.CallAsync("Accessibility.getFullAXTree");
        var ms = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        var nodes = new List<string>();
        foreach (var node in full.GetProperty("nodes").EnumerateArray())
        {
            if (node.TryGetProperty("ignored", out var ignored) && ignored.GetBoolean() || !node.TryGetProperty("backendDOMNodeId", out var backend))
            {
                continue;
            }

            var role = node.TryGetProperty("role", out var r) && r.TryGetProperty("value", out var rv) ? rv.GetString() ?? "" : "";
            var name = node.TryGetProperty("name", out var n) && n.TryGetProperty("value", out var nv) ? nv.GetString() ?? "" : "";
            var resolved = await page.Cdp.CallAsync("DOM.resolveNode", w =>
            {
                w.WriteNumber("backendNodeId", backend.GetInt32());
                w.WriteString("objectGroup", "tree");
            });
            if (!resolved.GetProperty("object").TryGetProperty("objectId", out var objectId))
            {
                continue;
            }

            var info = await page.Cdp.CallFunctionAsync(objectId.GetString()!, "function() { return this.nodeType === 1 ? [globalThis.__sparAutomation.indexOf(this), this.localName] : [-1, '#']; }", "tree");
            var index = info[0].GetInt32();
            if (index >= 0)
            {
                nodes.Add($"{{\"index\":{index},\"tag\":{Json.String(info[1].GetString()!)},\"role\":{Json.String(role)},\"name\":{Json.String(name)}}}");
            }
        }

        await page.Cdp.ReleaseGroupAsync("tree");
        await File.WriteAllTextAsync(Path.Combine(output, $"tree-chromium-{state}.json"), "[" + string.Join(",", nodes) + "]");
        Log.Write("TREE", $"{state}: engine {engine.GetArrayLength()} nodes; Chromium getFullAXTree {ms:F1} ms, {nodes.Count} element nodes");
    }

    await DumpAsync("orders");
    await page.GetByRole("button", "Actions").ClickAsync();
    await Expect(page.GetByRole("menuitem", "Archive draft")).ToBeVisibleAsync();
    await DumpAsync("menu");
    await page.Keyboard.PressAsync("Escape");
    await page.GetByRole("button", "Place order").ClickAsync();
    await Expect(page.GetByRole("dialog", "Confirm order")).ToBeVisibleAsync();
    await DumpAsync("dialog");
    await view.CloseAsync();
    return 0;
}
