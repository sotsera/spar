using System.Diagnostics;
using Microsoft.Playwright;

// Verbs:
//   connect <http-endpoint> [ws-endpoint]   ConnectOverCDPAsync (Chromium and WebKit types) and ConnectAsync (WebKit)
//   launch <url> [executable]               the library's WebKit, or another executable in its place, then a short journey
var verb = args.ElementAtOrDefault(0);
using var playwright = await Playwright.CreateAsync();
Console.WriteLine($"PART1 library {typeof(IPlaywright).Assembly.GetName().Version}");
switch (verb)
{
    case "connect":
        await TryAsync("Chromium.ConnectOverCDPAsync " + args[1], () => playwright.Chromium.ConnectOverCDPAsync(args[1], new() { Timeout = 8000 }));
        await TryAsync("Webkit.ConnectOverCDPAsync " + args[1], () => playwright.Webkit.ConnectOverCDPAsync(args[1], new() { Timeout = 8000 }));
        if (args.Length > 2)
        {
            await TryAsync("Webkit.ConnectAsync " + args[2], () => playwright.Webkit.ConnectAsync(args[2], new() { Timeout = 8000 }));
            await TryAsync("Chromium.ConnectOverCDPAsync " + args[2], () => playwright.Chromium.ConnectOverCDPAsync(args[2], new() { Timeout = 8000 }));
        }

        break;
    case "launch":
        var clock = Stopwatch.StartNew();
        var options = new BrowserTypeLaunchOptions { Headless = true, Timeout = 20000 };
        if (args.Length > 2)
        {
            options.ExecutablePath = args[2];
        }

        var browser = await TryAsync($"Webkit.LaunchAsync executable={options.ExecutablePath ?? "(the library's WebKit)"}", () => playwright.Webkit.LaunchAsync(options));
        if (browser is null)
        {
            break;
        }

        Console.WriteLine($"PART1 launched in {clock.ElapsedMilliseconds} ms, version {browser.Version}");
        for (var run = 0; run < 5; run++)
        {
            clock.Restart();
            var context = await browser.NewContextAsync(new() { ViewportSize = new() { Width = 1008, Height = 729 } });
            var page = await context.NewPageAsync();
            await page.GotoAsync(args[1]);
            var opened = clock.ElapsedMilliseconds;
            await page.EvaluateAsync("() => { window.__t = null; addEventListener('click', e => window.__t = e.isTrusted, { capture: true, once: true }); }");
            await page.GetByLabel("Customer name").FillAsync("Ada Lovelace");
            await page.GetByRole(AriaRole.Button, new() { Name = "Actions" }).ClickAsync();
            await page.GetByRole(AriaRole.Menuitem, new() { Name = "Archive draft" }).ClickAsync();
            await Assertions.Expect(page.GetByRole(AriaRole.Status)).ToHaveTextAsync("Draft archived");
            await Assertions.Expect(page.GetByRole(AriaRole.List, new() { Name = "Recent orders" }).GetByRole(AriaRole.Listitem)).ToHaveCountAsync(3);
            Console.WriteLine($"PART1 run {run}: context+page+goto {opened} ms, fill+menu+expects {clock.ElapsedMilliseconds - opened} ms, first click trusted={await page.EvaluateAsync<bool?>("() => window.__t")}");
            await context.CloseAsync();
        }

        await browser.CloseAsync();
        break;
    default:
        Console.WriteLine("verbs: connect | launch");
        return 64;
}

return 0;

static async Task<T?> TryAsync<T>(string what, Func<Task<T>> action) where T : class
{
    var clock = Stopwatch.StartNew();
    try
    {
        var result = await action();
        Console.WriteLine($"PART1 {what}: ok in {clock.ElapsedMilliseconds} ms");
        return result;
    }
    catch (Exception exception)
    {
        var message = exception.Message.ReplaceLineEndings(" | ");
        Console.WriteLine($"PART1 {what}: {exception.GetType().Name} after {clock.ElapsedMilliseconds} ms: {message[..Math.Min(700, message.Length)]}");
        return null;
    }
}
