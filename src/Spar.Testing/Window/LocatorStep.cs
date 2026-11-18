using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Spar.Testing;

/// <summary>
/// One step of a locator's chain, as the page engine reads it (<c>{ "kind": … }</c>), with the C# call that made it for
/// failure messages, so a message names the locator as the test wrote it.
/// </summary>
internal sealed class LocatorStep
{
    private readonly Action<Utf8JsonWriter> _write;

    private LocatorStep(string description, Action<Utf8JsonWriter> write)
    {
        Description = description;
        _write = write;
    }

    public string Description { get; }

    public void Write(Utf8JsonWriter writer)
    {
        writer.WriteStartObject();
        _write(writer);
        writer.WriteEndObject();
    }

    public static void WriteChain(Utf8JsonWriter writer, string name, IReadOnlyList<LocatorStep> chain)
    {
        writer.WriteStartArray(name);
        foreach (var step in chain)
        {
            step.Write(writer);
        }

        writer.WriteEndArray();
    }

    public static LocatorStep Role(SparRole role, SparGetByRoleOptions? options)
    {
        var name = role.ToString().ToLowerInvariant();
        var described = DescribeRole(role, options);
        return new LocatorStep(described, writer =>
        {
            writer.WriteString("kind", "role");
            writer.WriteString("role", name);
            if (options is null)
            {
                return;
            }

            PageJson.WritePattern(writer, "name", options.Name, options.NameRegex);
            WriteFlag(writer, "exact", options.Exact);
            WriteFlag(writer, "checked", options.Checked);
            WriteFlag(writer, "disabled", options.Disabled);
            WriteFlag(writer, "expanded", options.Expanded);
            WriteFlag(writer, "includeHidden", options.IncludeHidden);
            WriteFlag(writer, "pressed", options.Pressed);
            WriteFlag(writer, "selected", options.Selected);
            if (options.Level is { } level)
            {
                writer.WriteNumber("level", level);
            }
        });
    }

    /// <summary>A text query: <c>label</c>, <c>text</c>, <c>placeholder</c>, <c>alt</c> or <c>title</c>.</summary>
    public static LocatorStep Text(string kind, string method, string? text, Regex? regex, SparGetByTextOptions? options) =>
        new($"{method}({Quote(text, regex)}{(options?.Exact == true ? ", Exact" : "")})", writer =>
        {
            writer.WriteString("kind", kind);
            PageJson.WritePattern(writer, "text", text, regex);
            WriteFlag(writer, "exact", options?.Exact);
        });

    public static LocatorStep Css(string selector) =>
        new($"Locator(\"{selector}\")", writer =>
        {
            writer.WriteString("kind", "css");
            writer.WriteString("selector", selector);
        });

    public static LocatorStep Nth(int index) =>
        new(index switch { 0 => "First", -1 => "Last", _ => $"Nth({index.ToString(CultureInfo.InvariantCulture)})" }, writer =>
        {
            writer.WriteString("kind", "nth");
            writer.WriteNumber("index", index);
        });

    public static LocatorStep Filter(SparLocatorFilterOptions options)
    {
        var parts = new List<string>();
        if (options.HasText is not null || options.HasTextRegex is not null)
        {
            parts.Add("HasText = " + Quote(options.HasText, options.HasTextRegex));
        }

        if (options.HasNotText is not null || options.HasNotTextRegex is not null)
        {
            parts.Add("HasNotText = " + Quote(options.HasNotText, options.HasNotTextRegex));
        }

        if (options.Has is { } has)
        {
            parts.Add("Has = " + has);
        }

        if (options.HasNot is { } hasNot)
        {
            parts.Add("HasNot = " + hasNot);
        }

        return new LocatorStep($"Filter({string.Join(", ", parts)})", writer =>
        {
            writer.WriteString("kind", "filter");
            PageJson.WritePattern(writer, "hasText", options.HasText, options.HasTextRegex);
            PageJson.WritePattern(writer, "hasNotText", options.HasNotText, options.HasNotTextRegex);
            if (options.Has is { } inner)
            {
                WriteChain(writer, "has", inner.Chain);
            }

            if (options.HasNot is { } innerNot)
            {
                WriteChain(writer, "hasNot", innerNot.Chain);
            }
        });
    }

    private static string DescribeRole(SparRole role, SparGetByRoleOptions? options)
    {
        var parts = new List<string> { role.ToString() };
        if (options is { Name: not null } or { NameRegex: not null })
        {
            parts.Add(Quote(options.Name, options.NameRegex));
        }

        if (options?.Exact == true)
        {
            parts.Add("Exact");
        }

        if (options?.Level is { } level)
        {
            parts.Add("Level = " + level.ToString(CultureInfo.InvariantCulture));
        }

        if (options?.Checked is { } isChecked)
        {
            parts.Add("Checked = " + (isChecked ? "true" : "false"));
        }

        return $"GetByRole({string.Join(", ", parts)})";
    }

    private static string Quote(string? text, Regex? regex) => regex is not null ? $"/{regex}/" : $"\"{text}\"";

    private static void WriteFlag(Utf8JsonWriter writer, string name, bool? value)
    {
        if (value is { } flag)
        {
            writer.WriteBoolean(name, flag);
        }
    }
}
