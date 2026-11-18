using System.Diagnostics;
using Spar.WindowAutomation.Linux.Automation;
using static Spar.WindowAutomation.Linux.Automation.Assertions;

namespace Spar.WindowAutomation.Linux;

internal static partial class Probes
{
    /// <summary>Where an action's time goes, 100 of each.</summary>
    public static async Task<int> ActionsAsync(IInput input, string output)
    {
        var view = await View.CreateAsync();
        var page = WindowPage.Attach(view, input);
        await view.NavigateAsync(Session.Origin + "/");
        await page.WaitForUiReadyAsync();
        await Expect(page.GetByRole("list", "Recent orders").GetByRole("listitem")).ToHaveCountAsync(3);
        var t = new Timings();
        const string Change = "document.querySelector('[role=status]').textContent = 'changed ' + Math.random()";
        for (var i = 0; i < 100; i++)
        {
            await t.TimeAsync("engine call, no work (location.href)", page.UrlAsync);
            await t.TimeAsync("evaluate 1+1 (main world)", () => page.EvaluateAsync("1+1"));
            await t.TimeAsync("resolve role button \"Actions\" (count)", () => page.GetByRole("button", "Actions").CountAsync());
            await t.TimeAsync("resolve role, exact heading", () => page.GetByRole("heading", "Orders", exact: true).CountAsync());
            await t.TimeAsync("resolve role listitem in named list", () => page.GetByRole("list", "Recent orders").GetByRole("listitem").CountAsync());
            await page.EvaluateAsync(Change);
            await t.TimeAsync("resolve role right after a DOM change", () => page.GetByRole("status").CountAsync());
            await page.EvaluateAsync(Change);
            await t.TimeAsync("resolve label right after a DOM change", () => page.GetByLabel("Customer name").CountAsync());
            await t.TimeAsync("resolve label", () => page.GetByLabel("Customer name").CountAsync());
            await t.TimeAsync("resolve text", () => page.GetByText("Recent orders", exact: true).CountAsync());
            var actions = page.GetByRole("button", "Actions");
            await t.TimeAsync("actionable, pointer (1 frame)", () => page.EngineAsync($$"""{"op":"actionable","chain":{{actions.Chain}},"enabled":true,"pointer":true}"""));
            await t.TimeAsync("actionable, no pointer", () => page.EngineAsync($$"""{"op":"actionable","chain":{{actions.Chain}},"enabled":true,"pointer":false}"""));
            await t.TimeAsync("view geometry (UI thread + XTranslateCoordinates)", () => view.GeometryAsync());
            await t.TimeAsync("mouse move (send only)", () => page.Mouse.MoveAsync(5, 5));
            await t.TimeAsync("click on empty area (arm, input, landed)", () => page.Mouse.ClickAsync(5, 5));
            await t.TimeAsync("key press Escape (arm, input, landed)", () => page.Keyboard.PressAsync("Escape"));
            await t.TimeAsync("full ClickAsync (open menu) + Escape", async () =>
            {
                await page.GetByRole("button", "Actions").ClickAsync();
                await page.Keyboard.PressAsync("Escape");
            });
            var current = (await page.EvaluateAsync("document.querySelector('[role=status]').textContent")).GetString()!;
            await t.TimeAsync("expect text (already true, idle)", () => Expect(page.GetByRole("status")).ToHaveTextAsync(current));
            await page.GetByRole("button", "Actions").ClickAsync();
            await page.Keyboard.PressAsync("Escape");
            await t.TimeAsync("expect text right after the menu closed", () => Expect(page.GetByRole("status")).ToHaveTextAsync(current));
            await t.TimeAsync("type 12 characters (keys, landed)", async () =>
            {
                await page.GetByLabel("Customer name").FillAsync("abcdefghijkl");
            });
            await t.TimeAsync("fill 12 characters (insertText)", () => page.GetByLabel("Customer name").FillAsync("abcdefghijkl", insertText: true));
            await t.TimeAsync("screenshot (visible region, PNG)", () => page.CaptureAsync());
            await t.TimeAsync("snapshot only (pixels, no PNG)", () => view.SnapshotAsync());
            await t.TimeAsync("accessibility tree of the page (engine)", () => page.EngineAsync("""{"op":"tree"}"""));
        }

        t.Report("ACTION");
        foreach (var (reason, count) in WindowPage.RetryReasons.OrderByDescending(r => r.Value))
        {
            Log.Write("RETRY", $"{count,5} x {reason}");
        }

        foreach (var (problem, count) in WindowPage.InputProblems.OrderByDescending(r => r.Value))
        {
            Log.Write("INPUT-PROBLEM", $"{count,5} x {problem}");
        }

        await view.CloseAsync();
        return 0;
    }

    /// <summary>The primitives outside the journey: errors, dialogs, files, screenshots, trusted input and focus.</summary>
    public static async Task<int> CoverageAsync(IInput input, XInput x, string output)
    {
        var view = await View.CreateAsync();
        var page = WindowPage.Attach(view, input);
        await view.NavigateAsync(Session.Origin + "/");
        await page.WaitForUiReadyAsync();

        await Check("evaluate", async () => "title=" + (await page.EvaluateAsync("document.title")).GetString());
        await Check("the page's CSP applies to page script, not to the engine's calls", async () =>
        {
            var main = (await page.EvaluateAsync("(() => { try { eval('1'); return 'allowed'; } catch (e) { return 'blocked (' + e.name + ')'; } })()")).GetString();
            var engine = await page.EngineAsync("""{"op":"count","chain":[{"k":"role","role":"button"}]}""");
            return $"eval in the main world: {main}; engine role query in its world: {engine}";
        });
        await Check("getByText exact", async () => "count=" + await page.GetByText("Recent orders", exact: true).CountAsync());
        await Check("hover", async () =>
        {
            await page.EvaluateAsync("(window.__hovered = 0, document.querySelectorAll('button')[2].addEventListener('pointerenter', () => window.__hovered++), 0)");
            await page.GetByRole("button", "Show alert").HoverAsync();
            await page.WaitForUiReadyAsync();
            return "pointerenter on 'Show alert'=" + (await page.EvaluateAsync("[...document.querySelectorAll('button')].find(b => b.textContent === 'Show alert').matches(':hover')"));
        });
        await Check("view geometry and coordinate mapping", async () =>
        {
            var g = await view.GeometryAsync();
            var (ox, oy) = x.RootOrigin(g.Xid);
            await page.EvaluateAsync("(window.__at = null, addEventListener('pointerdown', e => window.__at = [e.clientX, e.clientY, e.isTrusted], { once: true, capture: true }), 0)");
            await page.Mouse.ClickAsync(300.0, 200.0);
            var at = await page.EvaluateAsync("window.__at");
            return $"xid=0x{g.Xid:x} window root origin=({ox},{oy}) view offset in surface=({g.X},{g.Y}) view={g.Width}x{g.Height}; click at CSS (300,200) arrived at {at}";
        });
        await Check("element screenshot", async () =>
        {
            var png = await page.GetByRole("form", "New order").ScreenshotAsync();
            await File.WriteAllBytesAsync(Path.Combine(output, $"linux-{input.Name}-form.png"), png);
            return $"{png.Length} bytes";
        });
        await Check("timeout message (wrong text)", () => Message(() => Expect(page.GetByRole("status")).ToHaveTextAsync("Shipped", timeout: 300)));
        await Check("timeout message (disabled button)", () => Message(() => page.GetByRole("button", "Place order").ClickAsync(timeout: 300)));
        await Check("strict mode message", () => Message(() => page.GetByRole("button").ClickAsync(timeout: 300)));
        await Check("console and page errors", async () =>
        {
            await page.GetByRole("button", "Log error").ClickAsync();
            var deadline = Stopwatch.StartNew();
            while (page.PageErrors.Count == 0 && deadline.ElapsedMilliseconds < 2000)
            {
                await Task.Delay(10);
            }

            return $"console={page.ConsoleMessages.Count} errors={page.PageErrors.Count} after {deadline.ElapsedMilliseconds} ms; " +
                $"console[0]={page.ConsoleMessages.FirstOrDefault()} | error[0]={page.PageErrors.FirstOrDefault()?.ReplaceLineEndings(" ")}";
        });
        await Check("set input files (click the input, the host's file-chooser handler selects the files)", async () =>
        {
            var file = Path.Combine(output, "upload.txt");
            await File.WriteAllTextAsync(file, "spike");
            var before = view.FileChoosers;
            await page.GetByLabel("Attachment").SetInputFilesAsync([file]);
            await Expect(page.GetByRole("status")).ToHaveTextAsync("Attached 1 file(s)");
            var name = await page.EvaluateAsync("document.querySelector('input[type=file]').files[0].name + ' ' + document.querySelector('input[type=file]').files[0].size");
            return $"run-file-chooser requests={view.FileChoosers - before} page sees {name}";
        });
        await Check("click on the file input, nothing armed (the picker block)", async () =>
        {
            var before = view.FileChoosers;
            var windowsBefore = x.TopLevels();
            await page.GetByLabel("Attachment").ClickAsync();
            await Task.Delay(1000);
            return $"run-file-chooser requests={view.FileChoosers - before} (answered with cancel); new X top-level windows=[{string.Join("; ", x.TopLevels().Except(windowsBefore))}] of {windowsBefore.Count}";
        });
        await Check("alert (script-dialog answered by the host)", async () =>
        {
            await page.GetByRole("button", "Show alert").ClickAsync();
            await Expect(page.GetByRole("status")).ToHaveTextAsync("Alert closed");
            return "dialogs=" + string.Join(" | ", page.Dialogs);
        });
        await Check($"trusted input and user activation ({input.Name})", async () =>
        {
            await page.EvaluateAsync("""
                (window.__seen = [], ['pointerdown', 'mousedown', 'click', 'keydown', 'input'].forEach(t => addEventListener(t,
                  e => window.__seen.push(t + ':' + e.isTrusted + (t === 'click' || t === 'keydown' ? ':activation=' + navigator.userActivation.isActive : '')), true)), 0)
                """);
            await page.GetByLabel("Customer name").ClickAsync();
            await page.Keyboard.PressAsync("x");
            await page.WaitForUiReadyAsync();
            return "events=" + await page.EvaluateAsync("window.__seen.join(' ')");
        });
        await Check($"window.open from a click handler: does it pass WebKit's popup blocker? ({input.Name})", async () =>
        {
            await page.EvaluateAsync("(document.querySelector('h1').addEventListener('click', () => { window.__opened = window.open('about:blank') === null ? 'null' : 'window'; }, { once: true }), 0)");
            var before = view.NewWindowRequests;
            await page.GetByRole("heading", "Orders", exact: true).ClickAsync();
            await page.WaitForUiReadyAsync();
            var fromClick = view.NewWindowRequests - before;
            return $"host create requests from the click={fromClick} (window.open returned {await page.EvaluateAsync("window.__opened")}); see the activation verb for the controls";
        });
        await Check("fill through execCommand('insertText')", async () =>
        {
            var result = await page.EngineAsync($$"""{"op":"insertText","chain":{{page.GetByLabel("Customer name").Chain}},"text":"Grace Hopper"}""");
            return "engine reports " + result + "; React state: Place order enabled=" + await page.EvaluateAsync("!document.querySelector('button[type=submit]').disabled");
        });
        await Check("Tab moves focus (trusted key)", async () =>
        {
            await page.GetByLabel("Customer name").PressAsync("Tab");
            await page.WaitForUiReadyAsync();
            return "focused after Tab: " + await page.EvaluateAsync("document.activeElement.outerHTML.slice(0, 60)");
        });
        await Check("typing characters the keymap lacks (remapped keycode)", async () =>
        {
            await page.GetByLabel("Notes").FillAsync("Grüße, café — 你好");
            return "value=" + await page.EvaluateAsync("document.querySelector('textarea').value") + $" remapped so far={x.Remapped} (spare keycodes {x.SpareKeycodes})";
        });
        if (input is XtestInput)
        {
            await Check("keys while the X focus is elsewhere (focus mode never, then auto)", async () =>
            {
                var g = await view.GeometryAsync();
                var focusBefore = x.Focus();
                await page.EvaluateAsync("(window.__keys = 0, addEventListener('keydown', () => window.__keys++, true), 0)");
                x.SetFocus(0);
                var never = new XtestInput(x, "never");
                await never.PressAsync(view, "a");
                await Task.Delay(300);
                var whenNone = (await page.EvaluateAsync("window.__keys")).GetInt32();
                x.SetFocus(1);
                await never.PressAsync(view, "a");
                await Task.Delay(300);
                var whenPointerRoot = (await page.EvaluateAsync("window.__keys")).GetInt32() - whenNone;
                var auto = new XtestInput(x, "auto");
                await auto.PressAsync(view, "a");
                await Task.Delay(300);
                var afterAuto = (await page.EvaluateAsync("window.__keys")).GetInt32() - whenNone - whenPointerRoot;
                return $"X focus at start=0x{focusBefore:x} (window 0x{g.Xid:x}); keys seen with focus None={whenNone}, PointerRoot (pointer over the window)={whenPointerRoot}, after auto moved focus={afterAuto}";
            });
            await Check("click into a window that is not focused (another window on top elsewhere)", async () =>
            {
                var other = await View.CreateAsync(width: 200, height: 150);
                await other.NavigateAsync(Session.Origin + "/settings");
                var g = await view.GeometryAsync();
                var focusAfterOther = x.Focus();
                var otherG = await other.GeometryAsync();
                await page.EvaluateAsync("(window.__clicks = 0, addEventListener('pointerdown', () => window.__clicks++, true), 0)");
                await page.Mouse.ClickAsync(600, 600);
                var clicks = (await page.EvaluateAsync("window.__clicks")).GetInt32();
                await other.CloseAsync();
                return $"focus moved to the new window=0x{focusAfterOther:x} (test window 0x{g.Xid:x}, other 0x{otherG.Xid:x}); a click outside the other window reached the test page: {clicks}";
            });
        }

        await view.CloseAsync();
        return 0;
    }

    private static async Task<string> Message(Func<Task> action)
    {
        try
        {
            await action();
            return "no error?";
        }
        catch (Exception exception) when (exception is WaitTimeoutException or InvalidOperationException)
        {
            return "message: " + exception.Message[..Math.Min(260, exception.Message.Length)];
        }
    }

    private static async Task Check(string name, Func<Task<string>> probe)
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
}
