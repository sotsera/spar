using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Spar.WindowAutomation.Linux.Automation;

/// <summary>One step of a locator chain, as the engine reads it (JSON).</summary>
internal sealed record Step(string Json, string Description)
{
    public static Step Role(string role, string? name, bool exact) => new(
        $$"""{"k":"role","role":{{Automation.Json.String(role)}}{{(name is null ? "" : ",\"name\":" + Automation.Json.String(name))}},"exact":{{Automation.Json.Bool(exact)}}}""",
        name is null ? $"role={role}" : $"role={role}[name{(exact ? "=" : "~=")}\"{name}\"]");

    public static Step Role(string role, Regex name) => new(
        "{\"k\":\"role\",\"role\":" + Automation.Json.String(role) + ",\"name\":{\"re\":" + Automation.Json.String(name.ToString()) +
            ",\"flags\":\"" + ((name.Options & RegexOptions.IgnoreCase) != 0 ? "i" : "") + "\"}}",
        $"role={role}[name=/{name}/]");

    public static Step Label(string text, bool exact) => new($$"""{"k":"label","text":{{Automation.Json.String(text)}},"exact":{{Automation.Json.Bool(exact)}}}""", $"label{(exact ? "=" : "~=")}\"{text}\"");

    public static Step Text(string text, bool exact) => new($$"""{"k":"text","text":{{Automation.Json.String(text)}},"exact":{{Automation.Json.Bool(exact)}}}""", $"text{(exact ? "=" : "~=")}\"{text}\"");

    public static Step TestId(string id) => new($$"""{"k":"testid","id":{{Automation.Json.String(id)}}}""", $"testid=\"{id}\"");

    public static Step Nth(int index) => new($$"""{"k":"nth","i":{{index}}}""", $"nth={index}");
}

/// <summary>
/// A lazy description of elements, resolved again at every attempt inside the page, as the library's locators are.
/// Actions are strict (exactly one element), wait for actionability, and retry until the page's timeout.
/// </summary>
internal sealed class Locator(WindowPage page, Step[] steps)
{
    internal WindowPage Page => page;

    public string Description => string.Join(" >> ", steps.Select(s => s.Description));

    internal string Chain => "[" + string.Join(",", steps.Select(s => s.Json)) + "]";

    public Locator GetByRole(string role, string? name = null, bool exact = false) => new(page, [.. steps, Step.Role(role, name, exact)]);

    public Locator GetByLabel(string text, bool exact = false) => new(page, [.. steps, Step.Label(text, exact)]);

    public Locator GetByText(string text, bool exact = false) => new(page, [.. steps, Step.Text(text, exact)]);

    public Locator GetByTestId(string id) => new(page, [.. steps, Step.TestId(id)]);

    public Locator Nth(int index) => new(page, [.. steps, Step.Nth(index)]);

    public Locator First => Nth(0);

    public Locator Last => Nth(-1);

    public Task ClickAsync(int? timeout = null) => page.RetryAsync($"click {Description}", timeout, () => ClickOnceAsync(enabled: true));

    public Task HoverAsync(int? timeout = null) => page.RetryAsync($"hover {Description}", timeout, async () =>
    {
        var result = await ActionableAsync(enabled: false, editable: false, pointer: true, arm: null);
        if (Why(result) is { } why)
        {
            return why;
        }

        await page.Mouse.MoveAsync(result.GetProperty("x").GetDouble(), result.GetProperty("y").GetDouble());
        return null;
    });

    /// <summary>
    /// Focus and select all, then type the value as key strokes (trusted with XTEST), or press Delete for "".
    /// With <paramref name="insertText"/>, the editor inserts the text in one step instead (execCommand).
    /// </summary>
    public Task FillAsync(string value, int? timeout = null, bool insertText = false) => page.RetryAsync($"fill {Description}", timeout, async () =>
    {
        var check = await ActionableAsync(enabled: true, editable: true, pointer: false, arm: null);
        if (Why(check) is { } notActionable)
        {
            return notActionable;
        }

        if (insertText)
        {
            var inserted = await page.EngineAsync($$"""{"op":"insertText","chain":{{Chain}},"text":{{Json.String(value)}}}""");
            return Why(inserted);
        }

        var focused = await page.EngineAsync($$"""{"op":"focus","chain":{{Chain}},"selectAll":true}""");
        if (Why(focused) is { } why)
        {
            return why;
        }

        if (!focused.GetProperty("ok").GetBoolean())
        {
            return "element did not take focus";
        }

        if (value.Length == 0)
        {
            await page.Keyboard.PressAsync("Delete");
        }
        else
        {
            await page.Keyboard.TypeAsync(value);
        }

        return null;
    });

    public Task PressAsync(string key, int? timeout = null) => page.RetryAsync($"press {key} on {Description}", timeout, async () =>
    {
        var focused = await page.EngineAsync($$"""{"op":"focus","chain":{{Chain}}}""");
        if (Why(focused) is { } why)
        {
            return why;
        }

        await page.Keyboard.PressAsync(key);
        return null;
    });

    /// <summary>Clicks unless already checked, then checks that the state changed.</summary>
    public Task CheckAsync(int? timeout = null) => page.RetryAsync($"check {Description}", timeout, async () =>
    {
        var before = await StateOnceAsync();
        if (before.State is not { } state)
        {
            return before.Count == 0 ? "no element matches" : $"{before.Count} elements match (strict)";
        }

        if (state.GetProperty("checked").GetBoolean())
        {
            return null;
        }

        if (await ClickOnceAsync(enabled: true) is { } why)
        {
            return why;
        }

        var after = await StateOnceAsync();
        return after.State is { } s && s.GetProperty("checked").GetBoolean() ? null : "clicking did not check the element";
    });

    public Task SelectOptionAsync(string value, int? timeout = null) => page.RetryAsync($"select \"{value}\" in {Description}", timeout, async () =>
    {
        var check = await ActionableAsync(enabled: true, editable: false, pointer: false, arm: null);
        if (Why(check) is { } notActionable)
        {
            return notActionable;
        }

        return Why(await page.EngineAsync($$"""{"op":"selectOption","chain":{{Chain}},"values":[{{Json.String(value)}}]}"""));
    });

    /// <summary>
    /// Arms the host's file-chooser handler with the files, then clicks the input (trusted input opens the chooser;
    /// the host selects the files instead of showing a picker).
    /// </summary>
    public async Task SetInputFilesAsync(string[] paths, int? timeout = null)
    {
        page.View.ArmFiles(paths);
        await ClickAsync(timeout);
    }

    public async Task<int> CountAsync() =>
        (await page.EngineAsync($$"""{"op":"count","chain":{{Chain}}}""")).GetProperty("count").GetInt32();

    public async Task<byte[]> ScreenshotAsync(int? timeout = null)
    {
        double[] box = [];
        await page.RetryAsync($"screenshot {Description}", timeout, async () =>
        {
            var check = await ActionableAsync(enabled: false, editable: false, pointer: false, arm: null);
            if (Why(check) is { } why)
            {
                return why;
            }

            var b = await page.EngineAsync($$"""{"op":"box","chain":{{Chain}}}""");
            box = [b.GetProperty("x").GetDouble(), b.GetProperty("y").GetDouble(), b.GetProperty("width").GetDouble(), b.GetProperty("height").GetDouble()];
            return null;
        });
        return await page.CaptureAsync(box);
    }

    /// <summary>The state an assertion reads, or null when the locator does not resolve to exactly one element.</summary>
    internal async Task<(JsonElement? State, int Count)> StateOnceAsync()
    {
        var result = await page.EngineAsync($$"""{"op":"state","chain":{{Chain}}}""");
        var count = result.GetProperty("count").GetInt32();
        return (count == 1 ? result.GetProperty("state") : null, count);
    }

    internal async Task<int> CountOnceAsync() => await CountAsync();

    private async Task<string?> ClickOnceAsync(bool enabled)
    {
        var result = await ActionableAsync(enabled, editable: false, pointer: true, arm: "pointer");
        if (Why(result) is { } why)
        {
            return why;
        }

        await page.Input.ClickAsync(page.View, result.GetProperty("x").GetDouble(), result.GetProperty("y").GetDouble());
        page.NoteInput("click", await page.EngineAsync("""{"op":"landed","timeout":2000}"""));
        return null;
    }

    private Task<JsonElement> ActionableAsync(bool enabled, bool editable, bool pointer, string? arm) =>
        page.EngineAsync(new StringBuilder()
            .Append("{\"op\":\"actionable\",\"chain\":").Append(Chain)
            .Append(",\"enabled\":").Append(Json.Bool(enabled))
            .Append(",\"editable\":").Append(Json.Bool(editable))
            .Append(",\"pointer\":").Append(Json.Bool(pointer))
            .Append(arm is null ? "" : ",\"arm\":" + Json.String(arm))
            .Append('}').ToString());

    /// <summary>Null when the engine's answer is a success; otherwise why not (strict mode throws).</summary>
    private string? Why(JsonElement result)
    {
        if (result.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (result.TryGetProperty("strict", out var strict))
        {
            var described = string.Join("; ", result.GetProperty("described").EnumerateArray().Select(d => d.GetString()));
            throw new InvalidOperationException($"Strict mode: {Description} matches {strict.GetInt32().ToString(CultureInfo.InvariantCulture)} elements: {described}");
        }

        if (result.TryGetProperty("why", out var why))
        {
            return why.GetString();
        }

        return result.TryGetProperty("error", out var error) ? error.GetString() : null;
    }
}
