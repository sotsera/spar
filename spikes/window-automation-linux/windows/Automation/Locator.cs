using System.Text.Json;

namespace Spar.WindowAutomation.Automation;

/// <summary>One attempt of a retried operation: its own object group, released afterwards.</summary>
internal sealed class Attempt(Cdp cdp, string group)
{
    private string? _document;

    public Cdp Cdp { get; } = cdp;

    public string Group { get; } = group;

    public async Task<string> DocumentAsync() =>
        _document ??= (await Cdp.EvaluateAsync("document", Group, awaitPromise: false, byValue: false)).GetProperty("objectId").GetString()!;

    /// <summary>The elements of a JavaScript array returned by <paramref name="function"/>, as remote object ids.</summary>
    public async Task<List<string>> ElementsAsync(string root, string function, params string[] arguments)
    {
        var array = await Cdp.CallFunctionAsync(root, function, Group, byValue: false, arguments: arguments);
        var properties = await Cdp.CallAsync("Runtime.getProperties", w =>
        {
            w.WriteString("objectId", array.GetProperty("objectId").GetString());
            w.WriteBoolean("ownProperties", true);
        });
        var elements = new List<string>();
        foreach (var property in properties.GetProperty("result").EnumerateArray())
        {
            if (int.TryParse(property.GetProperty("name").GetString(), out _) && property.GetProperty("value").TryGetProperty("objectId", out var id))
            {
                elements.Add(id.GetString()!);
            }
        }

        return elements;
    }

    public async Task<string> DescribeAsync(string element) => (await Cdp.CallFunctionAsync(element, Scripts.Describe, Group)).GetString() ?? "?";
}

/// <summary>How a locator finds its elements under one root.</summary>
internal abstract class Query
{
    public abstract string Describe();

    public abstract Task<List<string>> FindAsync(Attempt attempt, string root);
}

/// <summary>
/// By ARIA role and accessible name: through <c>Accessibility.queryAXTree</c> (Chromium's own role and name computation), or,
/// with <see cref="WindowPage.UseEngine"/>, through the shared page-script engine (engine/engine.js).
/// </summary>
internal sealed class RoleQuery(string role, string? name, bool exact) : Query
{
    public override string Describe() => name is null ? $"role={role}" : $"role={role}[name{(exact ? "=" : "~=")}\"{name}\"]";

    public override async Task<List<string>> FindAsync(Attempt attempt, string root)
    {
        if (WindowPage.UseEngine)
        {
            var step = "{\"k\":\"role\",\"role\":" + Json.String(role) + (name is null ? "" : ",\"name\":" + Json.String(name)) + ",\"exact\":" + Json.Bool(exact) + "}";
            return await attempt.ElementsAsync(root, "function(step) { return globalThis.__sparAutomation.queryFrom(this, step); }", step);
        }

        var result = await attempt.Cdp.CallAsync("Accessibility.queryAXTree", w =>
        {
            w.WriteString("objectId", root);
            w.WriteString("role", role);
            if (exact && name is not null)
            {
                w.WriteString("accessibleName", name);
            }
        });
        var found = new List<string>();
        foreach (var node in result.GetProperty("nodes").EnumerateArray())
        {
            if (node.TryGetProperty("ignored", out var ignored) && ignored.GetBoolean() || !node.TryGetProperty("backendDOMNodeId", out var backend))
            {
                continue;
            }

            if (name is not null && !exact)
            {
                var nodeName = node.TryGetProperty("name", out var n) && n.TryGetProperty("value", out var v) ? v.GetString() ?? "" : "";
                if (!nodeName.Contains(name, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
            }

            var resolved = await attempt.Cdp.CallAsync("DOM.resolveNode", w =>
            {
                w.WriteNumber("backendNodeId", backend.GetInt32());
                w.WriteString("objectGroup", attempt.Group);
            });
            found.Add(resolved.GetProperty("object").GetProperty("objectId").GetString()!);
        }

        return found;
    }
}

/// <summary>By a page-side function that returns an array of elements.</summary>
internal sealed class ScriptQuery(string description, string function, params string[] arguments) : Query
{
    public override string Describe() => description;

    public override Task<List<string>> FindAsync(Attempt attempt, string root) => attempt.ElementsAsync(root, function, arguments);
}

/// <summary>
/// A lazy description of elements, resolved again at every attempt, as the library's locators are. Actions are
/// strict (exactly one element), wait for actionability, and retry until the page's timeout.
/// </summary>
internal sealed class Locator
{
    private readonly WindowPage _page;
    private readonly Locator? _parent;
    private readonly Query _query;
    private readonly int? _nth;

    internal Locator(WindowPage page, Locator? parent, Query query, int? nth = null)
    {
        _page = page;
        _parent = parent;
        _query = query;
        _nth = nth;
    }

    internal WindowPage Page => _page;

    public string Description => (_parent is null ? "" : _parent.Description + " >> ") + _query.Describe() + (_nth is { } n ? $" >> nth={n}" : "");

    public Locator GetByRole(string role, string? name = null, bool exact = false) => new(_page, this, new RoleQuery(role, name, exact));

    public Locator GetByLabel(string text, bool exact = false) => new(_page, this, Queries.Label(text, exact));

    public Locator GetByText(string text, bool exact = false) => new(_page, this, Queries.Text(text, exact));

    public Locator GetByTestId(string id) => new(_page, this, Queries.TestId(id));

    public Locator Nth(int index) => new(_page, _parent, _query, index);

    public Locator First => Nth(0);

    internal async Task<List<string>> ResolveAsync(Attempt attempt)
    {
        var roots = _parent is null ? [await attempt.DocumentAsync()] : await _parent.ResolveAsync(attempt);
        var found = new List<string>();
        foreach (var root in roots)
        {
            found.AddRange(await _query.FindAsync(attempt, root));
        }

        return _nth is { } n ? (n < found.Count ? [found[n]] : []) : found;
    }

    /// <summary>Exactly one element, or why not. More than one is a strict-mode error (thrown, not retried).</summary>
    internal async Task<(string? Element, string Why)> SingleAsync(Attempt attempt)
    {
        var found = await ResolveAsync(attempt);
        if (found.Count == 1)
        {
            return (found[0], "");
        }

        if (found.Count == 0)
        {
            return (null, "no element matches");
        }

        var described = new List<string>();
        foreach (var element in found.Take(5))
        {
            described.Add(await attempt.DescribeAsync(element));
        }

        throw new InvalidOperationException($"Strict mode: {Description} matches {found.Count} elements: {string.Join("; ", described)}");
    }

    public Task ClickAsync(int? timeout = null) => _page.RetryAsync($"click {Description}", timeout, async attempt =>
    {
        var (point, why) = await ActionableAsync(attempt, enabled: true, editable: false, pointer: true);
        if (point is not { } p)
        {
            return why;
        }

        await _page.Mouse.ClickAsync(p.X, p.Y);
        return null;
    });

    public Task HoverAsync(int? timeout = null) => _page.RetryAsync($"hover {Description}", timeout, async attempt =>
    {
        var (point, why) = await ActionableAsync(attempt, enabled: false, editable: false, pointer: true);
        if (point is not { } p)
        {
            return why;
        }

        await _page.Mouse.MoveAsync(p.X, p.Y);
        return null;
    });

    /// <summary>Focus, select all, then <c>Input.insertText</c> (trusted <c>beforeinput</c>/<c>input</c>), or Delete for "".</summary>
    public Task FillAsync(string value, int? timeout = null) => _page.RetryAsync($"fill {Description}", timeout, async attempt =>
    {
        var (element, why) = await ElementIfActionableAsync(attempt, enabled: true, editable: true);
        if (element is null)
        {
            return why;
        }

        var focused = await attempt.Cdp.CallFunctionAsync(element, Scripts.FocusAndSelectAll, attempt.Group);
        if (!focused.GetBoolean())
        {
            return "element did not take focus";
        }

        if (value.Length == 0)
        {
            await _page.Keyboard.PressAsync("Delete");
        }
        else
        {
            await attempt.Cdp.CallAsync("Input.insertText", w => w.WriteString("text", value));
        }

        return null;
    });

    public Task PressAsync(string key, int? timeout = null) => _page.RetryAsync($"press {key} on {Description}", timeout, async attempt =>
    {
        var (element, why) = await SingleAsync(attempt);
        if (element is null)
        {
            return why;
        }

        await attempt.Cdp.CallFunctionAsync(element, Scripts.Focus, attempt.Group);
        await _page.Keyboard.PressAsync(key);
        return null;
    });

    /// <summary>Clicks unless already checked, then checks that the state changed.</summary>
    public Task CheckAsync(int? timeout = null) => _page.RetryAsync($"check {Description}", timeout, async attempt =>
    {
        var (element, why) = await SingleAsync(attempt);
        if (element is null)
        {
            return why;
        }

        if ((await attempt.Cdp.CallFunctionAsync(element, Scripts.State, attempt.Group)).GetProperty("checked").GetBoolean())
        {
            return null;
        }

        var (point, notActionable) = await ActionableAsync(attempt, enabled: true, editable: false, pointer: true);
        if (point is not { } p)
        {
            return notActionable;
        }

        await _page.Mouse.ClickAsync(p.X, p.Y);
        var after = await attempt.Cdp.CallFunctionAsync(element, Scripts.State, attempt.Group);
        return after.GetProperty("checked").GetBoolean() ? null : "clicking did not check the element";
    });

    public Task SelectOptionAsync(string value, int? timeout = null) => _page.RetryAsync($"select \"{value}\" in {Description}", timeout, async attempt =>
    {
        var (element, why) = await ElementIfActionableAsync(attempt, enabled: true, editable: false);
        if (element is null)
        {
            return why;
        }

        var result = await attempt.Cdp.CallFunctionAsync(element, Scripts.SelectOption, attempt.Group, arguments: "[" + Json.String(value) + "]");
        return result.TryGetProperty("error", out var error) ? error.GetString() : null;
    });

    /// <summary><c>DOM.setFileInputFiles</c>: no picker opens, so the host's interception is not involved.</summary>
    public Task SetInputFilesAsync(string[] paths, int? timeout = null) => _page.RetryAsync($"set files on {Description}", timeout, async attempt =>
    {
        var (element, why) = await SingleAsync(attempt);
        if (element is null)
        {
            return why;
        }

        await attempt.Cdp.CallAsync("DOM.setFileInputFiles", w =>
        {
            w.WriteStartArray("files");
            foreach (var path in paths)
            {
                w.WriteStringValue(path);
            }

            w.WriteEndArray();
            w.WriteString("objectId", element);
        });
        return null;
    });

    public async Task<int> CountAsync()
    {
        int count = 0;
        await _page.RetryAsync($"count {Description}", 0, async attempt =>
        {
            count = (await ResolveAsync(attempt)).Count;
            return null;
        });
        return count;
    }

    /// <summary>A PNG of the element's box (<c>Page.captureScreenshot</c> with a clip).</summary>
    public async Task<byte[]> ScreenshotAsync(int? timeout = null)
    {
        double[] box = [];
        await _page.RetryAsync($"screenshot {Description}", timeout, async attempt =>
        {
            var (element, why) = await ElementIfActionableAsync(attempt, enabled: false, editable: false);
            if (element is null)
            {
                return why;
            }

            box = [.. (await attempt.Cdp.CallFunctionAsync(element, Scripts.BoundingBox, attempt.Group)).EnumerateArray().Select(v => v.GetDouble())];
            return null;
        });
        return await _page.CaptureAsync(box);
    }

    /// <summary>The state an assertion reads, or null when the locator does not resolve to exactly one element.</summary>
    internal async Task<(JsonElement? State, int Count)> StateAsync(Attempt attempt)
    {
        var found = await ResolveAsync(attempt);
        if (found.Count != 1)
        {
            return (null, found.Count);
        }

        return (await attempt.Cdp.CallFunctionAsync(found[0], Scripts.State, attempt.Group), 1);
    }

    private async Task<(string? Element, string Why)> ElementIfActionableAsync(Attempt attempt, bool enabled, bool editable)
    {
        var (element, why) = await SingleAsync(attempt);
        if (element is null)
        {
            return (null, why);
        }

        var check = await attempt.Cdp.CallFunctionAsync(element, Scripts.Actionable, attempt.Group, awaitPromise: true,
            arguments: [Json.Bool(enabled), Json.Bool(editable), "false"]);
        var state = check.GetProperty("state").GetString()!;
        return state == "ok" ? (element, "") : (null, $"{await attempt.DescribeAsync(element)} is {state}");
    }

    private async Task<((double X, double Y)? Point, string Why)> ActionableAsync(Attempt attempt, bool enabled, bool editable, bool pointer)
    {
        var (element, why) = await SingleAsync(attempt);
        if (element is null)
        {
            return (null, why);
        }

        var check = await attempt.Cdp.CallFunctionAsync(element, Scripts.Actionable, attempt.Group, awaitPromise: true,
            arguments: [Json.Bool(enabled), Json.Bool(editable), Json.Bool(pointer)]);
        var state = check.GetProperty("state").GetString()!;
        if (state != "ok")
        {
            return (null, $"{await attempt.DescribeAsync(element)}: {state}");
        }

        return ((check.GetProperty("x").GetDouble(), check.GetProperty("y").GetDouble()), "");
    }
}

internal static class Queries
{
    public static Query Label(string text, bool exact) => new ScriptQuery($"label{(exact ? "=" : "~=")}\"{text}\"", Scripts.ByLabel, Json.String(text), Json.Bool(exact));

    public static Query Text(string text, bool exact) => new ScriptQuery($"text{(exact ? "=" : "~=")}\"{text}\"", Scripts.ByText, Json.String(text), Json.Bool(exact));

    public static Query TestId(string id) => new ScriptQuery($"testid=\"{id}\"", Scripts.ByTestId, Json.String(id));
}
