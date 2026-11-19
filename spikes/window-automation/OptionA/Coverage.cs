using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Spar.WindowAutomation;

/// <summary>Which of the library's features work against WebView2 through the CDP attach, and how it meets the host.</summary>
internal static class Coverage
{
    public static async Task<int> RunAsync(Env env, IBrowser browser, int port, bool newTargets)
    {
        // Identity: three test controllers at once beside the anchor.
        var views = new List<View>();
        var pages = new List<IPage>();
        for (var i = 0; i < 3; i++)
        {
            var view = await View.CreateAsync(env);
            var token = "id" + i + Guid.NewGuid().ToString("N")[..8];
            await view.NavigateAsync($"{View.Origin}/?test={token}");
            views.Add(view);
            pages.Add(await Attach.FindPageAsync(browser, token));
        }

        Log.Write("IDENTITY", $"contexts={browser.Contexts.Count} pages per context=[{string.Join(", ", browser.Contexts.Select(c => c.Pages.Count))}]");
        foreach (var context in browser.Contexts)
        {
            foreach (var page in context.Pages)
            {
                Log.Write("IDENTITY", $"  context#{browser.Contexts.ToList().IndexOf(context)} page url={Redact(page.Url)}");
            }
        }

        for (var i = 0; i < 3; i++)
        {
            var started = Stopwatch.GetTimestamp();
            var target = await Attach.TargetIdAsync(pages[i]);
            Log.Write("IDENTITY", $"view {i}: host target={views[i].TargetId[..8]} library page target={target[..8]} same={target == views[i].TargetId} ({Stopwatch.GetElapsedTime(started).TotalMilliseconds:F1} ms via a CDP session)");
        }

        // Isolation: what one InPrivate controller stores, the next one does not see; and what the library's
        // context-level storage APIs report, given that every page sits in its default context.
        await pages[0].EvaluateAsync("() => { localStorage.setItem('secret', 'from-view-0'); document.cookie = 'session=view0; Secure; SameSite=Strict'; }");
        var seen = await pages[1].EvaluateAsync<string>("() => (localStorage.getItem('secret') ?? 'none') + ' / cookie=' + (document.cookie || 'none')");
        Log.Write("ISOLATION", $"view 1 sees: {seen}");
        await Probe("context.CookiesAsync (default context)", async () =>
            $"{(await pages[0].Context.CookiesAsync()).Count} cookies: {string.Join(", ", (await pages[0].Context.CookiesAsync()).Select(c => c.Name + "@" + c.Domain))}");
        await Probe("context.StorageStateAsync", async () =>
        {
            var state = await pages[0].Context.StorageStateAsync();
            return state.Length > 160 ? state[..160] + "…" : state;
        });

        var p = pages[0];
        var v = views[0];
        await p.EvaluateAsync("new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r)))");

        // Locators and reads beyond the journey.
        await Probe("GetByTestId + GetByText", async () => $"export={await p.GetByTestId("export").CountAsync()} text={await p.GetByText("Recent orders", new() { Exact = true }).CountAsync()}");
        await Probe("AriaSnapshotAsync", async () =>
        {
            var snapshot = await p.GetByRole(AriaRole.Form, new() { Name = "New order" }).AriaSnapshotAsync();
            await File.WriteAllTextAsync(Path.Combine(Paths.Output, "a-aria-snapshot.yml"), snapshot);
            return $"{snapshot.Split('\n').Length} lines, first: {snapshot.Split('\n')[0]}";
        });
        await Probe("EvaluateAsync", async () => "title=" + await p.EvaluateAsync<string>("document.title"));
        await Probe("element ScreenshotAsync", async () => (await p.GetByRole(AriaRole.Form, new() { Name = "New order" }).ScreenshotAsync(new() { Path = Path.Combine(Paths.Output, "a-form.png") })).Length + " bytes");
        await Probe("Keyboard (Tab moves focus)", async () =>
        {
            await p.GetByLabel("Customer name").FocusAsync();
            await p.Keyboard.PressAsync("Tab");
            return "focused after Tab: " + await p.EvaluateAsync<string>("document.activeElement.outerHTML.slice(0, 50)");
        });
        await Probe("timeout message (wrong text)", async () =>
        {
            try
            {
                await Expect(p.GetByRole(AriaRole.Status)).ToHaveTextAsync("Shipped", new() { Timeout = 300 });
                return "no error?";
            }
            catch (PlaywrightException exception)
            {
                return "message: " + exception.Message.ReplaceLineEndings(" ")[..Math.Min(300, exception.Message.Length)];
            }
        });

        // Console and page errors.
        var console = new List<string>();
        var errors = new List<string>();
        p.Console += (_, message) => console.Add(message.Type + ": " + message.Text);
        p.PageError += (_, error) => errors.Add(error);
        await Probe("console + page error events", async () =>
        {
            await p.GetByRole(AriaRole.Button, new() { Name = "Log error" }).ClickAsync();
            await Task.Delay(300);
            return $"console=[{string.Join("; ", console)}] pageerror=[{string.Join("; ", errors.Select(e => e.Split('\n')[0]))}]";
        });

        // Script dialogs, with Spar's default (the browser's own dialogs on) and the host's Page.enable.
        await Probe("Dialog event (alert, accepted)", async () =>
        {
            var dialogs = new List<string>();
            void OnDialog(object? sender, IDialog dialog)
            {
                dialogs.Add(dialog.Type + ": " + dialog.Message);
                _ = dialog.AcceptAsync();
            }

            p.Dialog += OnDialog;
            var watch = DialogWatch.Start(v);
            await p.GetByRole(AriaRole.Button, new() { Name = "Show alert" }).ClickAsync();
            await Expect(p.GetByRole(AriaRole.Status)).ToHaveTextAsync("Alert closed");
            var native = watch.Stop();
            p.Dialog -= OnDialog;
            return $"events=[{string.Join("; ", dialogs)}] native dialogs={native}";
        });

        // Network events and route interception on a page served by WebResourceRequested.
        await Probe("network events on reload", async () =>
        {
            var requests = new List<string>();
            void OnRequest(object? sender, IRequest request) => requests.Add(new Uri(request.Url).AbsolutePath);
            p.Request += OnRequest;
            var served = v.Served;
            var response = p.WaitForResponseAsync("**/api/orders.json");
            await p.ReloadAsync();
            var orders = await response;
            p.Request -= OnRequest;
            return $"requests seen=[{string.Join(", ", requests)}] host served={v.Served - served} orders.json status={orders.Status} from-service-worker={orders.FromServiceWorker}";
        });
        await Probe("RouteAsync fulfil (api)", async () =>
        {
            var routed = 0;
            await p.RouteAsync("**/api/orders.json", route =>
            {
                routed++;
                return route.FulfillAsync(new() { ContentType = "application/json", Body = """[{"id":1,"customer":"Routed Customer","total":"1.00"}]""" });
            });
            var served = v.Served;
            await p.ReloadAsync();
            await Task.Delay(1500);
            var texts = await p.GetByRole(AriaRole.List, new() { Name = "Recent orders" }).GetByRole(AriaRole.Listitem).AllTextContentsAsync();
            await p.UnrouteAllAsync();
            return $"route handler calls={routed}; list shows [{string.Join("; ", texts)}]; host served during reload={v.Served - served}";
        });
        await Probe("RouteAsync abort (api)", async () =>
        {
            var routed = 0;
            await p.RouteAsync("**/api/orders.json", route =>
            {
                routed++;
                return route.AbortAsync();
            });
            await p.ReloadAsync();
            await Task.Delay(1500);
            var count = await p.GetByRole(AriaRole.List, new() { Name = "Recent orders" }).GetByRole(AriaRole.Listitem).CountAsync();
            await p.UnrouteAllAsync();
            return $"route handler calls={routed}; list items={count}";
        });

        // Tracing on the default context (where the library put every page).
        await Probe("Tracing (screenshots + snapshots)", async () =>
        {
            await p.Context.Tracing.StartAsync(new() { Screenshots = true, Snapshots = true, Sources = false });
            await p.GetByLabel("Customer name").FillAsync("Traced");
            await p.GetByRole(AriaRole.Button, new() { Name = "Actions" }).ClickAsync();
            await p.Keyboard.PressAsync("Escape");
            var path = Path.Combine(Paths.Output, "a-trace.zip");
            await p.Context.Tracing.StopAsync(new() { Path = path });
            using var zip = ZipFile.OpenRead(path);
            var trace = zip.GetEntry("trace.trace");
            using var reader = new StreamReader(trace!.Open());
            var lines = (await reader.ReadToEndAsync()).Split('\n');
            return $"{new FileInfo(path).Length} bytes, entries=[{string.Join(", ", zip.Entries.Select(e => e.FullName.Length > 30 ? e.FullName[..30] + "…" : e.FullName).Take(12))}], " +
                $"trace events={lines.Length}, screencast frames={lines.Count(l => l.Contains("\"screencast-frame\""))}, snapshots={lines.Count(l => l.Contains("\"frame-snapshot\""))}";
        });

        // File input: the host's interception against the library's own file chooser handling.
        var upload = Path.Combine(Paths.Output, "upload.txt");
        await File.WriteAllTextAsync(upload, "spike");
        var input = p.GetByLabel("Attachment");
        await Probe("file input click, no library listener", async () => await ClickFileInputAsync(p, v, input));
        await Probe("RunAndWaitForFileChooserAsync + SetFilesAsync", async () =>
        {
            var before = v.FileChoosersIntercepted;
            var watch = DialogWatch.Start(v);
            var chooser = await p.RunAndWaitForFileChooserAsync(() => input.ClickAsync(), new() { Timeout = 5000 });
            await chooser.SetFilesAsync(upload);
            await Expect(p.GetByRole(AriaRole.Status)).ToHaveTextAsync("Attached 1 file(s)");
            await Task.Delay(500);
            return $"library chooser ok; host intercepted={v.FileChoosersIntercepted - before} native dialogs={watch.Stop()}";
        });
        await Probe("file input click after the library's chooser", async () => await ClickFileInputAsync(p, v, input));
        await Probe("SetInputFilesAsync", async () =>
        {
            await input.SetInputFilesAsync(upload);
            await Expect(p.GetByRole(AriaRole.Status)).ToHaveTextAsync("Attached 1 file(s)");
            return "ok";
        });

        // The host's own in-process calls while the library is attached.
        await Probe("host in-process Runtime.evaluate + captureScreenshot", async () =>
        {
            var value = await v.CallAsync("Runtime.evaluate", """{"expression":"1+1","returnByValue":true}""");
            var shot = await v.CallAsync("Page.captureScreenshot", """{"format":"png"}""");
            return $"evaluate={value} screenshot={shot.Length} chars";
        });

        // What the library cannot do on this browser.
        // New targets made by the library (a new context, a new page) may open browser windows of their own: only on request.
        if (newTargets)
        {
        await Probe("Browser.NewContextAsync (RecordVideoDir)", async () =>
        {
            var watch = WindowWatch.Start(v);
            var context = await browser.NewContextAsync(new() { RecordVideoDir = Path.Combine(Paths.Output, "video") });
            var page = await context.NewPageAsync();
            await page.GotoAsync(View.Origin + "/");
            await Task.Delay(1500);
            var title = await page.TitleAsync();
            var shot = (await page.ScreenshotAsync()).Length;
            await context.CloseAsync();
            var video = await page.Video!.PathAsync();
            return $"title={title} screenshot={shot} bytes video={new FileInfo(video).Length} bytes new windows=[{watch.Stop()}]";
        });
        await Probe("default context NewPageAsync", async () =>
        {
            var watch = WindowWatch.Start(v);
            var page = await browser.Contexts[0].NewPageAsync();
            await Task.Delay(500);
            await page.CloseAsync();
            return $"new windows=[{watch.Stop()}]";
        });
        }

        await Probe("page.CloseAsync on a WebView2 page (5 s limit)", async () =>
        {
            var close = pages[2].CloseAsync();
            var finished = await Task.WhenAny(close, Task.Delay(5000)) == close;
            return $"returned={finished}; library page closed={pages[2].IsClosed}";
        });

        foreach (var view in views)
        {
            await view.CloseAsync();
        }

        // Does the library's Browser.CloseAsync end WebView2's browser (the app's own)?
        await Probe("browser.CloseAsync on a CDP connection", async () =>
        {
            await browser.CloseAsync();
            var exited = await Task.WhenAny(env.Exited.Task, Task.Delay(3000)) == env.Exited.Task;
            return $"WebView2 browser process exited={exited}";
        });
        return 0;
    }

    private static async Task<string> ClickFileInputAsync(IPage page, View view, ILocator input)
    {
        var before = view.FileChoosersIntercepted;
        var watch = DialogWatch.Start(view);
        await input.ClickAsync();
        await Task.Delay(1000);
        return $"host intercepted={view.FileChoosersIntercepted - before} native dialogs={watch.Stop()}";
    }

    private static async Task Probe(string name, Func<Task<string>> probe)
    {
        var started = Stopwatch.GetTimestamp();
        try
        {
            var detail = await probe();
            Log.Write("COVER", $"{name}: ok ({Stopwatch.GetElapsedTime(started).TotalMilliseconds:F1} ms) {detail}");
        }
        catch (Exception exception)
        {
            Log.Write("COVER", $"{name}: FAILED {exception.GetType().Name}: {exception.Message.ReplaceLineEndings(" ")[..Math.Min(400, exception.Message.Length)]}");
        }
    }

    private static string Redact(string url) => url.Length > 80 ? url[..80] + "…" : url;
}
