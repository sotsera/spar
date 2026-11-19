using System.Text.RegularExpressions;
using Spar.Testing;
using static Spar.Testing.SparAssertions;

namespace Spar.WindowTests;

/// <summary>
/// The window tests' page API on a real window: what the page engine finds, the waiting and actionability rules that
/// keep journeys reliable, trusted input, reads, assertions, screenshots, and the failure messages.
/// </summary>
[Trait("Surface", "Window")]
public sealed partial class PageApiTests
{
    [Fact]
    public async Task Roles_names_labels_and_text_find_what_a_person_sees()
    {
        await using var host = await HostPage.OpenAsync();
        var page = host.Page;

        await Expect(page.GetByRole(SparRole.Heading, new() { Name = "Window host", Level = 1 })).ToBeVisibleAsync();
        await Expect(page.GetByRole(SparRole.Region, new() { Name = "Order" }).GetByRole(SparRole.Button)).ToHaveCountAsync(1);
        await Expect(page.GetByLabel("Customer name")).ToBeEditableAsync();
        await Expect(page.GetByLabel("Quantity")).ToHaveValueAsync("1");
        await Expect(page.GetByRole(SparRole.Checkbox, new() { Name = "Gift wrap" })).ToBeCheckedAsync(false);
        await Expect(page.GetByRole(SparRole.Combobox, new() { Name = "Region" })).ToHaveValueAsync("Europe");
        await Expect(page.GetByRole(SparRole.Textbox, new() { Name = "Message" })).ToBeVisibleAsync();
        await Expect(page.GetByText("No order yet")).ToBeVisibleAsync();
        await Expect(page.GetByRole(SparRole.Link, new() { NameRegex = SecondPage() })).ToHaveAttributeAsync("href", "/other.html");
        await Expect(page.GetByRole(SparRole.Button, new() { Name = "Place order" })).ToBeDisabledAsync();

        // Hidden from everyone: the hidden button; visibility: hidden still counts for assistive technology but not as visible.
        await Expect(page.GetByRole(SparRole.Button, new() { Name = "Hidden" })).ToHaveCountAsync(0);
        await Expect(page.GetByRole(SparRole.Button, new() { Name = "Hidden", IncludeHidden = true })).ToHaveCountAsync(1);
        await Expect(page.GetByRole(SparRole.Button, new() { Name = "Invisible", IncludeHidden = true })).ToBeHiddenAsync();
        host.AssertClean();
    }

    [Fact]
    public async Task A_form_is_filled_checked_selected_and_sent_with_trusted_input()
    {
        await using var host = await HostPage.OpenAsync();
        var order = host.Page.GetByRole(SparRole.Region, new() { Name = "Order" });

        await order.GetByLabel("Customer name").FillAsync("Ada Lovelace");
        await order.GetByLabel("Quantity").FillAsync("3");
        await order.GetByRole(SparRole.Checkbox, new() { Name = "Gift wrap" }).CheckAsync();
        await order.GetByLabel("Region").SelectOptionAsync("Asia Pacific");
        await order.GetByLabel("Delivery day").FillAsync("2026-12-24");
        await order.GetByRole(SparRole.Textbox, new() { Name = "Message" }).FillAsync("Leave it at the door");
        await order.GetByRole(SparRole.Button, new() { Name = "Place order" }).ClickAsync();

        await Expect(order.GetByText("ordered")).ToHaveTextAsync("Ada Lovelace ordered 3 for apac, gift wrapped, 2026-12-24");
        await Expect(order.GetByRole(SparRole.Textbox, new() { Name = "Message" })).ToHaveTextAsync("Leave it at the door");
        await Expect(order.GetByLabel("Gift wrap")).ToBeCheckedAsync();
        Assert.True(await host.Page.EvaluateAsync<bool>("navigator.userActivation.hasBeenActive"));
        host.AssertClean();
    }

    [Fact]
    public async Task Fill_replaces_text_and_typing_sends_key_events()
    {
        await using var host = await HostPage.OpenAsync();
        var name = host.Page.GetByLabel("Customer name");

        await name.FillAsync("first");
        await name.FillAsync("second");
        await Expect(name).ToHaveValueAsync("second");

        await name.PressAsync("End");
        await name.PressSequentiallyAsync(" Grüße, café!");
        await Expect(name).ToHaveValueAsync("second Grüße, café!");

        await name.PressAsync("Control+a");
        await host.Page.Keyboard.PressAsync("Backspace");
        await Expect(name).ToHaveValueAsync("");
        await Expect(host.Page.GetByRole(SparRole.Button, new() { Name = "Place order" })).ToBeDisabledAsync();

        await name.ClearAsync();
        await host.Page.GetByRole(SparRole.Heading, new() { Level = 1 }).ClickAsync();
        await host.Page.Keyboard.PressAsync("/");
        await Expect(name).ToBeFocusedAsync();
        host.AssertClean();
    }

    [Fact]
    public async Task Actions_wait_for_late_covered_animated_and_scrolled_away_elements()
    {
        await using var host = await HostPage.OpenAsync();
        var page = host.Page;

        await host.ClickAsync("Load orders");
        await Expect(page.GetByRole(SparRole.List, new() { Name = "Orders" }).GetByRole(SparRole.Listitem)).ToHaveCountAsync(3);

        await host.ClickAsync("Covered button");
        await Expect(host.Status).ToHaveTextAsync("Covered button clicked");

        await host.ClickAsync("Show the late button");
        await host.ClickAsync("Late button");
        await Expect(host.Status).ToHaveTextAsync("Late button clicked");

        await host.ClickAsync("Open the dialog");
        var dialog = page.GetByRole(SparRole.Dialog, new() { Name = "Confirm the order" });
        await dialog.GetByRole(SparRole.Button, new() { Name = "Confirm" }).ClickAsync();
        await Expect(dialog).ToBeHiddenAsync();
        await Expect(host.Status).ToHaveTextAsync("Confirmed");

        await host.ClickAsync("Far away");
        await Expect(host.Status).ToHaveTextAsync("Far away clicked");
        host.AssertClean();
    }

    [Fact]
    public async Task A_click_whose_element_moves_between_press_and_release_is_stopped_and_tried_again()
    {
        await using var host = await HostPage.OpenAsync();

        // The button jumps 60 px on its first press, so that release lands elsewhere: the stray click is stopped before
        // the page sees it, and the second try clicks the button where it now is.
        await host.ClickAsync("Jumpy button");

        await Expect(host.Status).ToHaveTextAsync("Jumpy button clicked");
        Assert.Equal("[\"jumpy\"]", await host.Page.EvaluateAsync<string>("JSON.stringify(window.hostClicks)"));
        host.AssertClean();
    }

    [Fact]
    public async Task Chains_filters_and_positions_narrow_a_locator()
    {
        await using var host = await HostPage.OpenAsync();
        await host.ClickAsync("Load orders");
        var orders = host.Page.GetByRole(SparRole.List, new() { Name = "Orders" }).GetByRole(SparRole.Listitem);

        await Expect(orders.First).ToHaveTextAsync("Order for Ada");
        await Expect(orders.Last).ToHaveTextAsync("Order for Linus");
        await Expect(orders.Nth(1)).ToHaveTextAsync("Order for Grace");
        await Expect(orders.Filter(new() { HasText = "a" })).ToHaveCountAsync(2);
        await Expect(orders.Filter(new() { HasNotText = "Grace" })).ToHaveCountAsync(2);
        await Expect(orders.Filter(new() { HasTextRegex = new Regex("Linus$") })).ToHaveCountAsync(1);
        Assert.Equal(["Order for Ada", "Order for Grace", "Order for Linus"], await orders.AllInnerTextsAsync());
        Assert.Equal(3, await orders.CountAsync());
    }

    [Fact]
    public async Task Reads_never_wait_for_what_is_not_there()
    {
        await using var host = await HostPage.OpenAsync();
        var page = host.Page;

        Assert.True(await page.GetByLabel("Customer name").IsEditableAsync());
        Assert.False(await page.GetByRole(SparRole.Button, new() { Name = "Place order" }).IsEnabledAsync());
        Assert.False(await page.GetByRole(SparRole.Button, new() { Name = "Nowhere" }).IsVisibleAsync());
        Assert.True(await page.GetByRole(SparRole.Dialog, new() { IncludeHidden = true }).IsHiddenAsync());
        Assert.Equal("1", await page.GetByLabel("Quantity").InputValueAsync());
        Assert.Equal("No order yet", await page.GetByText("No order yet").InnerTextAsync());
        Assert.Equal("/other.html", await page.GetByRole(SparRole.Link).GetAttributeAsync("href"));
    }

    [Fact]
    public async Task Console_messages_page_errors_and_dialogs_are_recorded()
    {
        await using var host = await HostPage.OpenAsync();
        var page = host.Page;

        await host.ClickAsync("Log an error");
        await host.ClickAsync("Throw");
        await host.ClickAsync("Show an alert");
        await Expect(host.Status).ToHaveTextAsync("Alert closed");

        await WaitUntilAsync(() => page.PageErrors.Count > 0);
        Assert.Contains(page.ConsoleMessages, message => message is { Type: "error", Text: "Something went wrong on purpose" });
        Assert.Contains(page.PageErrors, error => error.Contains("Thrown on purpose", StringComparison.Ordinal));
        Assert.Equal([new SparDialog("alert", "Saved!")], page.Dialogs);

        // The same errors reach the app's log through the host, so a window test sees them as problems.
        await WaitUntilAsync(() => host.App.Problems.Any(problem => problem.Contains("Thrown on purpose", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Navigation_is_followed_and_waited_for()
    {
        await using var host = await HostPage.OpenAsync();
        var page = host.Page;

        await page.GetByRole(SparRole.Link, new() { Name = "Second page" }).ClickAsync();
        await page.WaitForURLAsync("**/other.html");
        await Expect(page).ToHaveURLAsync("https://app.localhost/other.html");
        await Expect(page.GetByRole(SparRole.Status)).ToHaveTextAsync("Ready");
        await page.GetByRole(SparRole.Button, new() { Name = "Echo" }).ClickAsync();
        await Expect(page.GetByRole(SparRole.Status)).ToHaveTextAsync("Echo: again");

        await page.GotoAsync("/");
        await Expect(page).ToHaveTitleAsync("Window host");
        await page.GetByLabel("Customer name").FillAsync("before the reload");
        await page.ReloadAsync();
        await Expect(page.GetByLabel("Customer name")).ToHaveValueAsync("");
        await page.WaitForUiReadyAsync();
        host.AssertClean();
    }

    [Fact]
    public async Task Screenshots_capture_the_page_and_one_element()
    {
        await using var host = await HostPage.OpenAsync();
        var folder = Results.Folder("screenshots");

        var whole = await host.Page.ScreenshotAsync(new() { Path = Path.Combine(folder, "window-host.png") });
        var order = await host.Page.GetByRole(SparRole.Region, new() { Name = "Order" }).ScreenshotAsync();

        Assert.True(IsPng(whole) && IsPng(order));
        Assert.True(Width(whole) > Width(order), $"page {Width(whole)} px, element {Width(order)} px");
        Assert.True(File.Exists(Path.Combine(folder, "window-host.png")));
    }

    private static Task WaitUntilAsync(Func<bool> condition) => Waits.UntilAsync(condition, TimeSpan.FromSeconds(5));

    private static bool IsPng(byte[] image) => image.Length > 24 && image[0] == 0x89 && image[1] == (byte)'P';

    private static int Width(byte[] png) => (png[16] << 24) | (png[17] << 16) | (png[18] << 8) | png[19];

    [GeneratedRegex("^Second")]
    private static partial Regex SecondPage();
}
