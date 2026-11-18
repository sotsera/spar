using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Spar.Testing;

/// <summary>
/// Assertions that retry until they hold or their time is up, named as Microsoft's browser automation library names
/// them: <c>await Expect(page.GetByRole(SparRole.Status)).ToHaveTextAsync("Saved")</c>, with
/// <c>using static Spar.Testing.SparAssertions;</c>. A failure says what was expected and what was found at the last try.
/// </summary>
public static class SparAssertions
{
    public static SparLocatorAssertions Expect(SparLocator locator)
    {
        ArgumentNullException.ThrowIfNull(locator);
        return new SparLocatorAssertions(locator, negated: false);
    }

    public static SparPageAssertions Expect(SparPage page)
    {
        ArgumentNullException.ThrowIfNull(page);
        return new SparPageAssertions(page, negated: false);
    }
}

/// <summary>What a locator's elements must be; <see cref="Not"/> turns each assertion around.</summary>
public sealed class SparLocatorAssertions
{
    private readonly SparLocator _locator;
    private readonly bool _negated;

    internal SparLocatorAssertions(SparLocator locator, bool negated)
    {
        _locator = locator;
        _negated = negated;
    }

    /// <summary>The same assertions, each expecting the opposite.</summary>
    public SparLocatorAssertions Not => new(_locator, !_negated);

    /// <summary>One element, visible.</summary>
    public Task ToBeVisibleAsync(SparAssertionOptions? options = null) =>
        SingleAsync("ToBeVisibleAsync", options, state => (PageJson.Bool(state, "visible"), Shown(state, "visible")));

    /// <summary>No element, or one that is not visible.</summary>
    public Task ToBeHiddenAsync(SparAssertionOptions? options = null) =>
        SingleAsync("ToBeHiddenAsync", options, state => (!PageJson.Bool(state, "visible"), Shown(state, "visible")), noneHolds: true);

    public Task ToBeEnabledAsync(SparAssertionOptions? options = null) =>
        SingleAsync("ToBeEnabledAsync", options, state => (PageJson.Bool(state, "enabled"), Shown(state, "enabled")));

    public Task ToBeDisabledAsync(SparAssertionOptions? options = null) =>
        SingleAsync("ToBeDisabledAsync", options, state => (!PageJson.Bool(state, "enabled"), Shown(state, "enabled")));

    public Task ToBeEditableAsync(SparAssertionOptions? options = null) =>
        SingleAsync("ToBeEditableAsync", options, state => (PageJson.Bool(state, "editable"), Shown(state, "editable")));

    /// <summary>Checked (a checkbox, radio, switch or <c>aria-checked</c>); with <paramref name="isChecked"/> false, unchecked.</summary>
    public Task ToBeCheckedAsync(bool isChecked = true, SparAssertionOptions? options = null) =>
        SingleAsync(isChecked ? "ToBeCheckedAsync" : "ToBeCheckedAsync(false)", options,
            state => (PageJson.Bool(state, "checked") == isChecked, Shown(state, "checked")));

    public Task ToBeFocusedAsync(SparAssertionOptions? options = null) =>
        SingleAsync("ToBeFocusedAsync", options, state => (PageJson.Bool(state, "focused"), Shown(state, "focused")));

    /// <summary>The element's rendered text, whitespace normalized, equals <paramref name="expected"/>.</summary>
    public Task ToHaveTextAsync(string expected, SparAssertionOptions? options = null) =>
        TextAsync($"ToHaveTextAsync(\"{expected}\")", options, text => text == Normalize(expected));

    public Task ToHaveTextAsync(Regex expected, SparAssertionOptions? options = null) =>
        TextAsync($"ToHaveTextAsync(/{expected}/)", options, expected.IsMatch);

    /// <summary>The element's rendered text contains <paramref name="expected"/> (case-sensitive).</summary>
    public Task ToContainTextAsync(string expected, SparAssertionOptions? options = null) =>
        TextAsync($"ToContainTextAsync(\"{expected}\")", options, text => text.Contains(Normalize(expected), StringComparison.Ordinal));

    public Task ToContainTextAsync(Regex expected, SparAssertionOptions? options = null) =>
        TextAsync($"ToContainTextAsync(/{expected}/)", options, expected.IsMatch);

    /// <summary>The value of an input, text area or select equals <paramref name="expected"/>.</summary>
    public Task ToHaveValueAsync(string expected, SparAssertionOptions? options = null) =>
        SingleAsync($"ToHaveValueAsync(\"{Escape(expected)}\")", options,
            state => (PageJson.String(state, "value") == expected, $"value is \"{Escape(PageJson.String(state, "value") ?? "")}\""));

    public Task ToHaveValueAsync(Regex expected, SparAssertionOptions? options = null) =>
        SingleAsync($"ToHaveValueAsync(/{expected}/)", options,
            state => (expected.IsMatch(PageJson.String(state, "value") ?? ""), $"value is \"{Escape(PageJson.String(state, "value") ?? "")}\""));

    /// <summary>The element's accessible name equals <paramref name="expected"/>.</summary>
    public Task ToHaveAccessibleNameAsync(string expected, SparAssertionOptions? options = null) =>
        SingleAsync($"ToHaveAccessibleNameAsync(\"{expected}\")", options,
            state => (PageJson.String(state, "name") == Normalize(expected), $"name is \"{PageJson.String(state, "name")}\""));

    public Task ToHaveAttributeAsync(string name, string value, SparAssertionOptions? options = null) =>
        SingleAsync($"ToHaveAttributeAsync(\"{name}\", \"{value}\")", options,
            state => (PageJson.String(state, "attribute") == value, $"{name} is {Quoted(PageJson.String(state, "attribute"))}"), attribute: name);

    /// <summary>The locator finds exactly <paramref name="count"/> elements.</summary>
    public Task ToHaveCountAsync(int count, SparAssertionOptions? options = null) =>
        AssertAsync($"ToHaveCountAsync({count.ToString(CultureInfo.InvariantCulture)})", options, async left =>
        {
            var found = await _locator.Page.Session.EngineAsync("count", writer => LocatorStep.WriteChain(writer, "chain", _locator.Chain), left);
            if (found is not { } value)
            {
                return (false, "the page is loading");
            }

            var actual = PageJson.Int(value, "count");
            return (actual == count, $"count is {actual.ToString(CultureInfo.InvariantCulture)}");
        });

    private static string Normalize(string text) => Regex.Replace(text, @"[\s​]+", " ").Trim();

    private static string Escape(string text) => text.Replace("\n", "\\n", StringComparison.Ordinal);

    private static string Quoted(string? text) => text is null ? "absent" : $"\"{text}\"";

    private static string Shown(JsonElement state, string property) =>
        $"{PageJson.String(state, "element")} is {(PageJson.Bool(state, property) ? "" : "not ")}{property}";

    private Task TextAsync(string what, SparAssertionOptions? options, Func<string, bool> holds) =>
        SingleAsync(what, options, state =>
        {
            var text = PageJson.String(state, "text") ?? "";
            return (holds(text), $"text is \"{Escape(text)}\"");
        });

    /// <summary>An assertion about one element (strict mode); without one, it does not hold unless <paramref name="noneHolds"/>.</summary>
    private Task SingleAsync(
        string what,
        SparAssertionOptions? options,
        Func<JsonElement, (bool Holds, string Actual)> check,
        bool noneHolds = false,
        string? attribute = null) =>
        AssertAsync(what, options, async left =>
        {
            var answer = await _locator.Page.Session.EngineAsync("state", writer =>
            {
                LocatorStep.WriteChain(writer, "chain", _locator.Chain);
                if (attribute is not null)
                {
                    writer.WriteString("attribute", attribute);
                }
            }, left);
            if (answer is not { } state)
            {
                return (false, "the page is loading");
            }

            var count = PageJson.Int(state, "count");
            if (count > 1)
            {
                throw new SparPageException(
                    $"Expect({_locator}).{what}: {PageRetry.StrictViolation(_locator, count, PageJson.Strings(state, "matches"))}");
            }

            if (count == 0)
            {
                var nothing = !noneHolds && !_negated
                    ? await _locator.NothingMatchesAsync(left)
                    : "no element matches";
                return (noneHolds, nothing);
            }

            return check(state);
        });

    private async Task AssertAsync(string what, SparAssertionOptions? options, Func<TimeSpan, Task<(bool Holds, string Actual)>> check)
    {
        var name = $"Expect({_locator}){(_negated ? ".Not" : "")}.{what}";
        await PageRetry.RunAsync(name, _locator.Page.ExpectTimeout(options?.Timeout), async left =>
        {
            var (holds, actual) = await check(left);
            return holds != _negated ? Attempt<bool>.Success(true) : Attempt<bool>.Retry(actual);
        });
    }
}

/// <summary>What the page must be: its address and title; <see cref="Not"/> turns each assertion around.</summary>
public sealed class SparPageAssertions
{
    private readonly SparPage _page;
    private readonly bool _negated;

    internal SparPageAssertions(SparPage page, bool negated)
    {
        _page = page;
        _negated = negated;
    }

    public SparPageAssertions Not => new(_page, !_negated);

    /// <summary>The address equals <paramref name="url"/>, or matches it as a glob (<c>"**/settings"</c>).</summary>
    public Task ToHaveURLAsync(string url, SparAssertionOptions? options = null) =>
        AssertAsync($"ToHaveURLAsync(\"{url}\")", options, () => Task.FromResult((SparPage.Glob(url).IsMatch(_page.Url), $"the address is {_page.Url}")));

    public Task ToHaveURLAsync(Regex url, SparAssertionOptions? options = null) =>
        AssertAsync($"ToHaveURLAsync(/{url}/)", options, () => Task.FromResult((url.IsMatch(_page.Url), $"the address is {_page.Url}")));

    public Task ToHaveTitleAsync(string title, SparAssertionOptions? options = null) =>
        AssertAsync($"ToHaveTitleAsync(\"{title}\")", options, async () =>
        {
            var actual = await _page.TitleAsync();
            return (actual == title, $"the title is \"{actual}\"");
        });

    private async Task AssertAsync(string what, SparAssertionOptions? options, Func<Task<(bool Holds, string Actual)>> check)
    {
        var name = $"Expect(page){(_negated ? ".Not" : "")}.{what}";
        await PageRetry.RunAsync(name, _page.ExpectTimeout(options?.Timeout), async _ =>
        {
            var (holds, actual) = await check();
            return holds != _negated ? Attempt<bool>.Success(true) : Attempt<bool>.Retry(actual);
        });
    }
}
