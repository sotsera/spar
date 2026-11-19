using System.Text.RegularExpressions;
using Spar.WindowAutomation.Automation;
using static Spar.WindowAutomation.Automation.Assertions;

namespace Spar.WindowAutomation;

/// <summary>The orders journey, step for step the same as OptionA/Journey.cs, on Spar's own small API.</summary>
internal static class Journey
{
    public static async Task RunAsync(WindowPage page, Timings t, string? screenshot)
    {
        await t.TimeAsync("01 ready (two frames)", page.WaitForUiReadyAsync);
        await t.TimeAsync("02 expect heading", () => Expect(page.GetByRole("heading", "Orders", exact: true)).ToBeVisibleAsync());
        await t.TimeAsync("03 fill name", () => page.GetByLabel("Customer name").FillAsync("Ada Lovelace"));
        await t.TimeAsync("04 check native", () => page.GetByLabel("Express delivery").CheckAsync());
        await t.TimeAsync("05 check radix", () => page.GetByRole("checkbox", "Gift wrap").CheckAsync());
        await t.TimeAsync("06 select option", () => page.GetByLabel("Region").SelectOptionAsync("Europe"));
        await t.TimeAsync("07 open listbox", () => page.GetByRole("combobox", "Priority").ClickAsync());
        await t.TimeAsync("08 pick option", () => page.GetByRole("option", "High").ClickAsync());
        await t.TimeAsync("09 expect listbox text", () => Expect(page.GetByRole("combobox", "Priority")).ToHaveTextAsync(new Regex("High")));
        await t.TimeAsync("10 open menu", () => page.GetByRole("button", "Actions").ClickAsync());
        await t.TimeAsync("11 pick menu item", () => page.GetByRole("menuitem", "Archive draft").ClickAsync());
        await t.TimeAsync("12 expect status", () => Expect(page.GetByRole("status")).ToHaveTextAsync("Draft archived"));
        var orders = page.GetByRole("list", "Recent orders").GetByRole("listitem");
        await t.TimeAsync("13 wait async list", () => Expect(orders).ToHaveCountAsync(3));
        await t.TimeAsync("14 expect enabled later", () => Expect(page.GetByTestId("export")).ToBeEnabledAsync());
        await t.TimeAsync("15 fill filter", () => page.GetByRole("searchbox", "Filter orders").FillAsync("bob"));
        await t.TimeAsync("16 press Enter", () => page.GetByRole("searchbox", "Filter orders").PressAsync("Enter"));
        await t.TimeAsync("17 expect filtered", () => Expect(orders).ToHaveCountAsync(1));
        await t.TimeAsync("18 expect item text", () => Expect(orders.First).ToContainTextAsync("Bob Kahn"));
        await t.TimeAsync("19 open dialog", () => page.GetByRole("button", "Place order").ClickAsync());
        var dialog = page.GetByRole("dialog", "Confirm order");
        await t.TimeAsync("20 expect dialog", () => Expect(dialog).ToContainTextAsync("Ada Lovelace (express, gift, Europe, High)"));
        await t.TimeAsync("21 cancel dialog", () => dialog.GetByRole("button", "Cancel").ClickAsync());
        await t.TimeAsync("22 expect dialog hidden", () => Expect(dialog).ToBeHiddenAsync());
        await t.TimeAsync("23 reopen dialog", () => page.GetByRole("button", "Place order").ClickAsync());
        await t.TimeAsync("24 confirm", () => dialog.GetByRole("button", "Confirm").ClickAsync());
        await t.TimeAsync("25 expect placed", () => Expect(page.GetByRole("status")).ToHaveTextAsync("Order placed for Ada Lovelace (express, gift, Europe, High)"));
        await t.TimeAsync("26 click link", () => page.GetByRole("link", "Settings").ClickAsync());
        await t.TimeAsync("27 wait for URL", () => page.WaitForUrlAsync("**/settings"));
        await t.TimeAsync("28 expect route text", () => Expect(page.GetByText("Theme: System")).ToBeVisibleAsync());
        if (screenshot is not null)
        {
            await t.TimeAsync("29 screenshot", () => page.ScreenshotAsync(screenshot));
        }
    }
}
