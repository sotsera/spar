using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Spar.WindowAutomation;

/// <summary>
/// The orders journey on Microsoft.Playwright. The same code runs on both surfaces: the WebView2 window (attached over
/// CDP) and headless Chromium launched by the library (the Browser surface). Only the setup that hands it the page
/// differs. Step for step the same as OptionB/Journey.cs.
/// </summary>
internal static class Journey
{
    public static async Task RunAsync(IPage page, Timings t, string? screenshot)
    {
        await t.TimeAsync("01 ready (two frames)", () => page.EvaluateAsync("new Promise(r => requestAnimationFrame(() => requestAnimationFrame(() => r(true))))"));
        await t.TimeAsync("02 expect heading", () => Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Orders", Exact = true })).ToBeVisibleAsync());
        await t.TimeAsync("03 fill name", () => page.GetByLabel("Customer name").FillAsync("Ada Lovelace"));
        await t.TimeAsync("04 check native", () => page.GetByLabel("Express delivery").CheckAsync());
        await t.TimeAsync("05 check radix", () => page.GetByRole(AriaRole.Checkbox, new() { Name = "Gift wrap" }).CheckAsync());
        await t.TimeAsync("06 select option", () => page.GetByLabel("Region").SelectOptionAsync("Europe"));
        await t.TimeAsync("07 open listbox", () => page.GetByRole(AriaRole.Combobox, new() { Name = "Priority" }).ClickAsync());
        await t.TimeAsync("08 pick option", () => page.GetByRole(AriaRole.Option, new() { Name = "High" }).ClickAsync());
        await t.TimeAsync("09 expect listbox text", () => Expect(page.GetByRole(AriaRole.Combobox, new() { Name = "Priority" })).ToHaveTextAsync(new Regex("High")));
        await t.TimeAsync("10 open menu", () => page.GetByRole(AriaRole.Button, new() { Name = "Actions" }).ClickAsync());
        await t.TimeAsync("11 pick menu item", () => page.GetByRole(AriaRole.Menuitem, new() { Name = "Archive draft" }).ClickAsync());
        await t.TimeAsync("12 expect status", () => Expect(page.GetByRole(AriaRole.Status)).ToHaveTextAsync("Draft archived"));
        var orders = page.GetByRole(AriaRole.List, new() { Name = "Recent orders" }).GetByRole(AriaRole.Listitem);
        await t.TimeAsync("13 wait async list", () => Expect(orders).ToHaveCountAsync(3));
        await t.TimeAsync("14 expect enabled later", () => Expect(page.GetByTestId("export")).ToBeEnabledAsync());
        await t.TimeAsync("15 fill filter", () => page.GetByRole(AriaRole.Searchbox, new() { Name = "Filter orders" }).FillAsync("bob"));
        await t.TimeAsync("16 press Enter", () => page.GetByRole(AriaRole.Searchbox, new() { Name = "Filter orders" }).PressAsync("Enter"));
        await t.TimeAsync("17 expect filtered", () => Expect(orders).ToHaveCountAsync(1));
        await t.TimeAsync("18 expect item text", () => Expect(orders.First).ToContainTextAsync("Bob Kahn"));
        await t.TimeAsync("19 open dialog", () => page.GetByRole(AriaRole.Button, new() { Name = "Place order" }).ClickAsync());
        var dialog = page.GetByRole(AriaRole.Dialog, new() { Name = "Confirm order" });
        await t.TimeAsync("20 expect dialog", () => Expect(dialog).ToContainTextAsync("Ada Lovelace (express, gift, Europe, High)"));
        await t.TimeAsync("21 cancel dialog", () => dialog.GetByRole(AriaRole.Button, new() { Name = "Cancel" }).ClickAsync());
        await t.TimeAsync("22 expect dialog hidden", () => Expect(dialog).ToBeHiddenAsync());
        await t.TimeAsync("23 reopen dialog", () => page.GetByRole(AriaRole.Button, new() { Name = "Place order" }).ClickAsync());
        await t.TimeAsync("24 confirm", () => dialog.GetByRole(AriaRole.Button, new() { Name = "Confirm" }).ClickAsync());
        await t.TimeAsync("25 expect placed", () => Expect(page.GetByRole(AriaRole.Status)).ToHaveTextAsync("Order placed for Ada Lovelace (express, gift, Europe, High)"));
        await t.TimeAsync("26 click link", () => page.GetByRole(AriaRole.Link, new() { Name = "Settings" }).ClickAsync());
        await t.TimeAsync("27 wait for URL", () => page.WaitForURLAsync("**/settings"));
        await t.TimeAsync("28 expect route text", () => Expect(page.GetByText("Theme: System")).ToBeVisibleAsync());
        if (screenshot is not null)
        {
            await t.TimeAsync("29 screenshot", () => page.ScreenshotAsync(new() { Path = screenshot }));
        }
    }
}
