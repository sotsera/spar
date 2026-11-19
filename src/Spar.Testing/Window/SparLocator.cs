using System.Text.Json;
using System.Text.RegularExpressions;

namespace Spar.Testing;

/// <summary>
/// A way to find elements in a window test's page, with the method names of Microsoft's browser automation library, so
/// a window journey reads like a browser journey (decision "Step 8's shape: window tests driven by Spar's own API").
/// Locators find elements by role and accessible name, label and text, never by test ids; each action and read finds
/// them again from the page, so a locator stays valid across re-renders. An action needs exactly one element (strict
/// mode) and waits until it is actionable: attached, visible, stable, enabled (and editable for text), scrolled into
/// view and, for the pointer, the element that would receive the click.
/// </summary>
public sealed class SparLocator
{
    internal SparLocator(SparPage page, IReadOnlyList<LocatorStep> chain)
    {
        Page = page;
        Chain = chain;
    }

    /// <summary>The page this locator searches.</summary>
    public SparPage Page { get; }

    /// <summary>The first element this locator finds.</summary>
    public SparLocator First => With(LocatorStep.Nth(0));

    /// <summary>The last element this locator finds.</summary>
    public SparLocator Last => With(LocatorStep.Nth(-1));

    internal IReadOnlyList<LocatorStep> Chain { get; }

    private PageSession Session => Page.Session;

    // ---- Finding ------------------------------------------------------------------------------------------------

    /// <summary>Elements of <paramref name="role"/> inside the elements this locator finds.</summary>
    public SparLocator GetByRole(SparRole role, SparGetByRoleOptions? options = null) => With(LocatorStep.Role(role, options));

    public SparLocator GetByLabel(string text, SparGetByTextOptions? options = null) =>
        With(LocatorStep.Text("label", "GetByLabel", text, null, options));

    public SparLocator GetByLabel(Regex text) => With(LocatorStep.Text("label", "GetByLabel", null, text, null));

    public SparLocator GetByText(string text, SparGetByTextOptions? options = null) =>
        With(LocatorStep.Text("text", "GetByText", text, null, options));

    public SparLocator GetByText(Regex text) => With(LocatorStep.Text("text", "GetByText", null, text, null));

    public SparLocator GetByPlaceholder(string text, SparGetByTextOptions? options = null) =>
        With(LocatorStep.Text("placeholder", "GetByPlaceholder", text, null, options));

    public SparLocator GetByAltText(string text, SparGetByTextOptions? options = null) =>
        With(LocatorStep.Text("alt", "GetByAltText", text, null, options));

    public SparLocator GetByTitle(string text, SparGetByTextOptions? options = null) =>
        With(LocatorStep.Text("title", "GetByTitle", text, null, options));

    /// <summary>Elements matching a CSS selector: for structure no role names, such as the <c>strong</c> of rendered text.</summary>
    public SparLocator Locator(string selector) => With(LocatorStep.Css(selector));

    /// <summary>The element at <paramref name="index"/> (from 0) of those this locator finds.</summary>
    public SparLocator Nth(int index) => With(LocatorStep.Nth(index));

    /// <summary>The elements this locator finds that also meet <paramref name="options"/>.</summary>
    public SparLocator Filter(SparLocatorFilterOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return With(LocatorStep.Filter(options));
    }

    // ---- Actions ------------------------------------------------------------------------------------------------

    /// <summary>Clicks the element's visible middle with the left button, once it is actionable.</summary>
    public Task ClickAsync(SparActionOptions? options = null) => ClickAsync("ClickAsync", 1, options);

    public Task DblClickAsync(SparActionOptions? options = null) => ClickAsync("DblClickAsync", 2, options);

    /// <summary>Moves the pointer over the element's middle.</summary>
    public async Task HoverAsync(SparActionOptions? options = null)
    {
        var point = await PointAsync("HoverAsync", enabled: false, options);
        await Page.Mouse.MoveAsync(point.X, point.Y);
        await Session.EngineAsync("landed", _ => { }, TimeSpan.FromSeconds(1));
    }

    /// <summary>
    /// Replaces the text of a text field, text area or editable content with <paramref name="value"/>: the element is
    /// focused, its text selected, then the value inserted as an input method would (trusted <c>input</c> events). Date,
    /// time, colour and range inputs get their value set, with <c>input</c> and <c>change</c>.
    /// </summary>
    public async Task FillAsync(string value, SparActionOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(value);
        var timeout = Page.Timeout(options?.Timeout);
        var prepared = await PageRetry.RunAsync($"{this}.FillAsync", timeout, async left =>
            PageRetry.Read(await Session.EngineAsync("prepareFill", writer =>
            {
                LocatorStep.WriteChain(writer, "chain", Chain);
                writer.WriteString("value", value);
            }, left), this));
        if (PageJson.Bool(prepared, "done"))
        {
            return;
        }

        await Session.BeforeInputAsync(timeout);
        if (value.Length == 0)
        {
            await Page.Keyboard.PressAsync("Delete");
            return;
        }

        await Page.Keyboard.InsertTextAsync(value);
    }

    /// <summary>Empties a text field (<see cref="FillAsync"/> with no text).</summary>
    public Task ClearAsync(SparActionOptions? options = null) => FillAsync("", options);

    /// <summary>Focuses the element, then presses a key or a combination (<see cref="SparKeyboard.PressAsync"/>).</summary>
    public async Task PressAsync(string key, SparActionOptions? options = null)
    {
        await FocusAsync(options);
        await Page.Keyboard.PressAsync(key);
    }

    /// <summary>Focuses the element, then types <paramref name="text"/> key by key (<see cref="SparKeyboard.TypeAsync"/>).</summary>
    public async Task PressSequentiallyAsync(string text, SparActionOptions? options = null)
    {
        await FocusAsync(options);
        await Page.Keyboard.TypeAsync(text);
    }

    public async Task FocusAsync(SparActionOptions? options = null)
    {
        await PageRetry.RunAsync($"{this}.FocusAsync", Page.Timeout(options?.Timeout), async left =>
            PageRetry.Read(await Session.EngineAsync("focus", writer => LocatorStep.WriteChain(writer, "chain", Chain), left), this));
    }

    /// <summary>Checks a checkbox, radio button or switch by clicking it, unless it is checked already; fails if the click did not check it.</summary>
    public Task CheckAsync(SparActionOptions? options = null) => SetCheckedAsync(true, options);

    public Task UncheckAsync(SparActionOptions? options = null) => SetCheckedAsync(false, options);

    public async Task SetCheckedAsync(bool checkedState, SparActionOptions? options = null)
    {
        var what = $"{this}.{(checkedState ? "CheckAsync" : "UncheckAsync")}";
        var state = await StateAsync(what, options?.Timeout);
        if (PageJson.Bool(state, "checked") == checkedState)
        {
            return;
        }

        await ClickAsync(what, 1, options);
        var timeout = Page.Timeout(options?.Timeout);
        await PageRetry.RunAsync(what, timeout, async left =>
        {
            var after = await Session.EngineAsync("state", writer => LocatorStep.WriteChain(writer, "chain", Chain), left);
            return after is { } value && PageJson.Bool(value, "checked") == checkedState
                ? Attempt<bool>.Success(true)
                : Attempt<bool>.Retry($"the click did not {(checkedState ? "check" : "uncheck")} it");
        });
    }

    /// <summary>Selects the options of a <c>&lt;select&gt;</c> by value or label; fires <c>input</c> and <c>change</c>.</summary>
    public async Task SelectOptionAsync(IEnumerable<string> values, SparActionOptions? options = null)
    {
        var wanted = values.ToArray();
        await PageRetry.RunAsync($"{this}.SelectOptionAsync", Page.Timeout(options?.Timeout), async left =>
            PageRetry.Read(await Session.EngineAsync("selectOption", writer =>
            {
                LocatorStep.WriteChain(writer, "chain", Chain);
                writer.WriteStartArray("values");
                foreach (var value in wanted)
                {
                    writer.WriteStringValue(value);
                }

                writer.WriteEndArray();
            }, left), this));
    }

    public Task SelectOptionAsync(string value, SparActionOptions? options = null) => SelectOptionAsync([value], options);

    /// <summary>A PNG of the element, scrolled into view, in CSS pixels of the viewport.</summary>
    public async Task<byte[]> ScreenshotAsync(SparScreenshotOptions? options = null)
    {
        var box = await PageRetry.RunAsync($"{this}.ScreenshotAsync", Page.Timeout(null), async left =>
            PageRetry.Read(await Session.EngineAsync("box", writer => LocatorStep.WriteChain(writer, "chain", Chain), left), this));
        return await Page.CaptureAsync(
            (PageJson.Double(box, "x"), PageJson.Double(box, "y"), PageJson.Double(box, "width"), PageJson.Double(box, "height")),
            options?.Path);
    }

    /// <summary>Waits until the element is attached, detached, visible (the default) or hidden.</summary>
    public async Task WaitForAsync(SparWaitForOptions? options = null)
    {
        var state = options?.State ?? SparWaitForState.Visible;
        await PageRetry.RunAsync($"{this}.WaitForAsync({state})", Page.Timeout(options?.Timeout), async left =>
        {
            var answer = await Session.EngineAsync("state", writer => LocatorStep.WriteChain(writer, "chain", Chain), left);
            if (answer is not { } value)
            {
                return Attempt<bool>.Retry("the page is loading");
            }

            var count = PageJson.Int(value, "count");
            if (count > 1 && state is SparWaitForState.Visible or SparWaitForState.Hidden)
            {
                return Attempt<bool>.Fail(PageRetry.StrictViolation(this, count, PageJson.Strings(value, "matches")));
            }

            var visible = count == 1 && PageJson.Bool(value, "visible");
            var reached = state switch
            {
                SparWaitForState.Attached => count > 0,
                SparWaitForState.Detached => count == 0,
                SparWaitForState.Visible => visible,
                _ => !visible,
            };
            return reached ? Attempt<bool>.Success(true) : Attempt<bool>.Retry($"{count} elements, {(visible ? "visible" : "not visible")}");
        });
    }

    // ---- Reads --------------------------------------------------------------------------------------------------

    /// <summary>How many elements the locator finds now.</summary>
    public async Task<int> CountAsync()
    {
        var answer = await Session.EngineAsync("count", writer => LocatorStep.WriteChain(writer, "chain", Chain), Page.Timeout(null));
        return answer is { } value ? PageJson.Int(value, "count") : 0;
    }

    /// <summary>The element's rendered text, whitespace normalized; waits for exactly one element.</summary>
    public async Task<string> InnerTextAsync() => PageJson.String(await StateAsync($"{this}.InnerTextAsync", null), "text") ?? "";

    /// <summary>The rendered text of every element the locator finds, in document order.</summary>
    public async Task<IReadOnlyList<string>> AllInnerTextsAsync()
    {
        var answer = await Session.EngineAsync("state", writer =>
        {
            LocatorStep.WriteChain(writer, "chain", Chain);
            writer.WriteBoolean("all", true);
        }, Page.Timeout(null));
        if (answer is not { } value)
        {
            return [];
        }

        return PageJson.Int(value, "count") == 1 ? [PageJson.String(value, "text") ?? ""] : PageJson.Strings(value, "texts");
    }

    /// <summary>The value of an input, text area or select; waits for exactly one element.</summary>
    public async Task<string> InputValueAsync() =>
        PageJson.String(await StateAsync($"{this}.InputValueAsync", null), "value")
        ?? throw new SparPageException($"{this}.InputValueAsync: the element is not an input, a text area or a select.");

    public async Task<string?> GetAttributeAsync(string name)
    {
        var state = await StateAsync($"{this}.GetAttributeAsync", null, name);
        return PageJson.String(state, "attribute");
    }

    /// <summary>Whether the element is visible now; false when there is none. Never waits.</summary>
    public async Task<bool> IsVisibleAsync() => await CurrentAsync("visible");

    public async Task<bool> IsHiddenAsync() => !await CurrentAsync("visible");

    public async Task<bool> IsEnabledAsync() => await CurrentAsync("enabled");

    public async Task<bool> IsCheckedAsync() => await CurrentAsync("checked");

    public async Task<bool> IsEditableAsync() => await CurrentAsync("editable");

    /// <summary>The chain as the test wrote it, for messages: <c>GetByRole(Dialog, "New page").GetByLabel("Title")</c>.</summary>
    public override string ToString() => string.Join(".", Chain.Select(step => step.Description));

    /// <summary>The element's state; waits for exactly one element (strict mode).</summary>
    internal Task<JsonElement> StateAsync(string what, float? timeout, string? attribute = null) =>
        PageRetry.RunAsync(what, Page.Timeout(timeout), async left =>
        {
            var answer = await Session.EngineAsync("state", writer =>
            {
                LocatorStep.WriteChain(writer, "chain", Chain);
                if (attribute is not null)
                {
                    writer.WriteString("attribute", attribute);
                }
            }, left);
            if (answer is not { } value)
            {
                return Attempt<JsonElement>.Retry("the page is loading");
            }

            var count = PageJson.Int(value, "count");
            return count switch
            {
                1 => Attempt<JsonElement>.Success(value),
                0 => Attempt<JsonElement>.Retry(await NothingMatchesAsync(left)),
                _ => Attempt<JsonElement>.Fail(PageRetry.StrictViolation(this, count, PageJson.Strings(value, "matches"))),
            };
        });

    /// <summary>What a failure message says when nothing matches: the step that found nothing, and what is near.</summary>
    internal async Task<string> NothingMatchesAsync(TimeSpan left)
    {
        var explained = await Session.EngineAsync("explain", writer => LocatorStep.WriteChain(writer, "chain", Chain), left);
        if (explained is not { } value)
        {
            return "no element matches";
        }

        return PageRetry.NothingMatches(this, PageJson.Int(value, "step", -1), PageJson.Strings(value, "near"));
    }

    private SparLocator With(LocatorStep step) => new(Page, [.. Chain, step]);

    private async Task<bool> CurrentAsync(string property)
    {
        var answer = await Session.EngineAsync("state", writer => LocatorStep.WriteChain(writer, "chain", Chain), Page.Timeout(null));
        return answer is { } value && PageJson.Int(value, "count") == 1 && PageJson.Bool(value, property);
    }

    private async Task ClickAsync(string what, int clickCount, SparActionOptions? options)
    {
        var timeout = Page.Timeout(options?.Timeout);
        await Session.BeforeInputAsync(timeout);
        await PageRetry.RunAsync($"{this}.{what}", timeout, async left =>
        {
            var answer = PageRetry.Read(await Session.EngineAsync("actionable", writer =>
            {
                LocatorStep.WriteChain(writer, "chain", Chain);
                writer.WriteBoolean("enabled", true);
                writer.WriteBoolean("pointer", true);
            }, left), this);
            if (answer.Failure is { } failure)
            {
                return Attempt<bool>.Fail(failure);
            }

            if (!answer.Done)
            {
                return Attempt<bool>.Retry(answer.NotYet ?? "not yet", answer.Again);
            }

            var value = answer.Value;
            await Page.Mouse.ClickAsync((float)PageJson.Double(value, "x"), (float)PageJson.Double(value, "y"), clickCount);
            var landed = await Session.EngineAsync("landed", _ => { }, left);
            if (landed is { } result && PageJson.Bool(result, "seen") && !PageJson.Bool(result, "onTarget"))
            {
                // The page put something over the element between the check and the press; the press was stopped.
                return Attempt<bool>.Retry($"{PageJson.String(result, "hit")} received the press instead; trying again");
            }

            if (landed is { } moved && PageJson.Bool(moved, "moved"))
            {
                // The element moved between the press and the release (an animation that started after the stability
                // check): the click that reached something else was stopped before the page saw it.
                return Attempt<bool>.Retry("it moved between the press and the release; trying again", again: true);
            }

            return Attempt<bool>.Success(true);
        });
    }

    private async Task<(float X, float Y)> PointAsync(string what, bool enabled, SparActionOptions? options)
    {
        var timeout = Page.Timeout(options?.Timeout);
        await Session.BeforeInputAsync(timeout);
        var value = await PageRetry.RunAsync($"{this}.{what}", timeout, async left =>
            PageRetry.Read(await Session.EngineAsync("actionable", writer =>
            {
                LocatorStep.WriteChain(writer, "chain", Chain);
                writer.WriteBoolean("enabled", enabled);
                writer.WriteBoolean("pointer", true);
            }, left), this));
        return ((float)PageJson.Double(value, "x"), (float)PageJson.Double(value, "y"));
    }
}
