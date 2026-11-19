using System.Text.Json;
using System.Text.RegularExpressions;

namespace Spar.WindowAutomation.Linux.Automation;

/// <summary>Web-first assertions: each re-resolves its locator in the page and retries until it holds or times out.</summary>
internal static class Assertions
{
    public static LocatorAssertions Expect(Locator locator) => new(locator);
}

internal sealed class LocatorAssertions(Locator locator)
{
    private readonly WindowPage _page = locator.Page;

    public Task ToBeVisibleAsync(int? timeout = null) => CheckAsync("to be visible", timeout, s => s.GetProperty("visible").GetBoolean() ? null : "hidden");

    public Task ToBeHiddenAsync(int? timeout = null) => _page.RetryAsync($"expect {locator.Description} to be hidden", timeout, async () =>
    {
        var (state, count) = await locator.StateOnceAsync();
        return count == 0 || state is { } s && !s.GetProperty("visible").GetBoolean() ? null : $"{count} element(s), visible";
    });

    public Task ToBeEnabledAsync(int? timeout = null) => CheckAsync("to be enabled", timeout, s => s.GetProperty("enabled").GetBoolean() ? null : "disabled");

    public Task ToBeCheckedAsync(int? timeout = null) => CheckAsync("to be checked", timeout, s => s.GetProperty("checked").GetBoolean() ? null : "not checked");

    public Task ToBeFocusedAsync(int? timeout = null) => CheckAsync("to be focused", timeout, s => s.GetProperty("focused").GetBoolean() ? null : "not focused");

    public Task ToHaveTextAsync(string expected, int? timeout = null) =>
        CheckAsync($"to have text \"{expected}\"", timeout, s => Text(s) == Normalize(expected) ? null : $"text is \"{Text(s)}\"");

    public Task ToHaveTextAsync(Regex expected, int? timeout = null) =>
        CheckAsync($"to have text /{expected}/", timeout, s => expected.IsMatch(Text(s)) ? null : $"text is \"{Text(s)}\"");

    public Task ToContainTextAsync(string expected, int? timeout = null) =>
        CheckAsync($"to contain text \"{expected}\"", timeout, s => Text(s).Contains(Normalize(expected), StringComparison.Ordinal) ? null : $"text is \"{Text(s)}\"");

    public Task ToHaveValueAsync(string expected, int? timeout = null) =>
        CheckAsync($"to have value \"{expected}\"", timeout, s => s.GetProperty("value").GetString() == expected ? null : $"value is \"{s.GetProperty("value")}\"");

    public Task ToHaveCountAsync(int expected, int? timeout = null) => _page.RetryAsync($"expect {locator.Description} to have count {expected}", timeout, async () =>
    {
        var count = await locator.CountOnceAsync();
        return count == expected ? null : $"count is {count}";
    });

    private Task CheckAsync(string what, int? timeout, Func<JsonElement, string?> holds) =>
        _page.RetryAsync($"expect {locator.Description} {what}", timeout, async () =>
        {
            var (state, count) = await locator.StateOnceAsync();
            return state is { } s ? holds(s) : count == 0 ? "no element matches" : $"{count} elements match (strict)";
        });

    private static string Text(JsonElement state) => state.GetProperty("text").GetString() ?? "";

    private static string Normalize(string text) => Regex.Replace(text, @"\s+", " ").Trim();
}
