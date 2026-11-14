#if SPAR_DEVTOOLS
using System.Buffers;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using System.Text.Json;
using D8.App.Interop;

namespace D8.App.Devtools;

internal static class DevtoolsFeature
{
    [FeatureSwitchDefinition("Spar.Devtools.IsSupported")]
    internal static bool IsSupported =>
        !AppContext.TryGetSwitch("Spar.Devtools.IsSupported", out var enabled) || enabled;
}

internal sealed record AxNode(string Role, string Name, string? Value, long BackendNodeId);

internal sealed record TreeResult(IReadOnlyList<AxNode> Nodes, int RawChars, double CdpMilliseconds, double ParseMilliseconds);

/// <summary>In-process page automation over <c>ICoreWebView2.CallDevToolsProtocolMethod</c>. UI thread only.</summary>
internal sealed class Automation
{
    private static readonly string[] s_clickSequence = ["mouseMoved", "mousePressed", "mouseReleased"];

    private readonly ICoreWebView2 _webView;

    public Automation(ICoreWebView2 webView)
    {
        _webView = webView;
    }

    public Task<string> CallAsync(string method, string parametersJson = "{}")
    {
        var handler = new DevToolsCallHandler(method);
        _webView.CallDevToolsProtocolMethod(method, parametersJson, handler);
        return handler.Task;
    }

    public async Task<TreeResult> TreeAsync()
    {
        var start = Stopwatch.GetTimestamp();
        var json = await CallAsync("Accessibility.getFullAXTree");
        var cdp = Stopwatch.GetElapsedTime(start).TotalMilliseconds;

        start = Stopwatch.GetTimestamp();
        var nodes = new List<AxNode>();
        using (var document = JsonDocument.Parse(json))
        {
            foreach (var node in document.RootElement.GetProperty("nodes").EnumerateArray())
            {
                if (node.TryGetProperty("ignored", out var ignored) && ignored.GetBoolean())
                {
                    continue;
                }

                var role = ReadAxValue(node, "role") ?? "";
                var name = ReadAxValue(node, "name") ?? "";
                if (role is "InlineTextBox" || (role is "generic" or "none" && name.Length == 0))
                {
                    continue;
                }

                var backendId = node.TryGetProperty("backendDOMNodeId", out var id) ? id.GetInt64() : 0;
                nodes.Add(new AxNode(role, name, ReadAxValue(node, "value"), backendId));
            }
        }

        var parse = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        return new TreeResult(nodes, json.Length, cdp, parse);
    }

    public static AxNode Find(TreeResult tree, string role, string name)
    {
        foreach (var node in tree.Nodes)
        {
            if (node.Role == role && node.Name == name)
            {
                return node;
            }
        }

        throw new InvalidOperationException($"No '{role}' named '{name}'.");
    }

    /// <summary>Replaces the text of an editable element as real keyboard input would.</summary>
    public async Task FillWithInputAsync(long backendNodeId, string text)
    {
        await CallAsync("DOM.focus", Params(w => w.WriteNumber("backendNodeId", backendNodeId)));
        var objectId = await ResolveAsync(backendNodeId);
        await CallAsync("Runtime.callFunctionOn", Params(w =>
        {
            w.WriteString("objectId", objectId);
            w.WriteString("functionDeclaration", "function () { this.select(); }");
        }));
        await CallAsync("Input.insertText", Params(w => w.WriteString("text", text)));
        await ReleaseAsync(objectId);
    }

    /// <summary>Clicks the centre of an element with real (trusted) mouse events.</summary>
    public async Task ClickWithInputAsync(long backendNodeId)
    {
        await CallAsync("DOM.scrollIntoViewIfNeeded", Params(w => w.WriteNumber("backendNodeId", backendNodeId)));
        var box = await CallAsync("DOM.getBoxModel", Params(w => w.WriteNumber("backendNodeId", backendNodeId)));
        var (x, y) = Centre(box);
        foreach (var type in s_clickSequence)
        {
            await CallAsync("Input.dispatchMouseEvent", Params(w =>
            {
                w.WriteString("type", type);
                w.WriteNumber("x", x);
                w.WriteNumber("y", y);
                if (type != "mouseMoved")
                {
                    w.WriteString("button", "left");
                    w.WriteNumber("clickCount", 1);
                }
            }));
        }
    }

    public Task FillWithScriptAsync(string elementId, string text) => EvaluateAsync(
        $$"""
        (() => {
          const e = document.getElementById({{JsonString(elementId)}});
          e.focus();
          e.value = {{JsonString(text)}};
          e.dispatchEvent(new Event('input', { bubbles: true }));
        })()
        """);

    public Task ClickWithScriptAsync(string elementId) =>
        EvaluateAsync($"document.getElementById({JsonString(elementId)}).click()");

    /// <summary>Waits in the page (MutationObserver) for an element's text to contain a value.</summary>
    public async Task<string> WaitForTextWithScriptAsync(string elementId, string contains, int timeoutMilliseconds)
    {
        var result = await EvaluateAsync(
            $$"""
            new Promise((resolve, reject) => {
              const e = document.getElementById({{JsonString(elementId)}});
              const want = {{JsonString(contains)}};
              if (e.textContent.includes(want)) { resolve(e.textContent); return; }
              const timer = setTimeout(() => { observer.disconnect(); reject(new Error('timeout')); }, {{timeoutMilliseconds}});
              const observer = new MutationObserver(() => {
                if (e.textContent.includes(want)) { clearTimeout(timer); observer.disconnect(); resolve(e.textContent); }
              });
              observer.observe(e, { childList: true, characterData: true, subtree: true });
            })
            """,
            awaitPromise: true);
        using var document = JsonDocument.Parse(result);
        var root = document.RootElement;
        if (root.TryGetProperty("exceptionDetails", out var exception))
        {
            throw new TimeoutException("waitFor failed: " + exception.GetRawText());
        }

        return root.GetProperty("result").GetProperty("value").GetString() ?? "";
    }

    /// <summary>Waits by polling the accessibility tree from the host, with no page script.</summary>
    public async Task<(string Text, int Polls)> WaitForTextWithTreeAsync(string contains, int timeoutMilliseconds)
    {
        var start = Stopwatch.GetTimestamp();
        var polls = 0;
        while (Stopwatch.GetElapsedTime(start).TotalMilliseconds < timeoutMilliseconds)
        {
            polls++;
            var tree = await TreeAsync();
            foreach (var node in tree.Nodes)
            {
                if (node.Role == "StaticText" && node.Name.Contains(contains, StringComparison.Ordinal))
                {
                    return (node.Name, polls);
                }
            }

            await Task.Delay(10);
        }

        throw new TimeoutException($"Text '{contains}' not observed after {polls} polls.");
    }

    public async Task<byte[]> ScreenshotAsync()
    {
        var json = await CallAsync("Page.captureScreenshot", """{"format":"png"}""");
        using var document = JsonDocument.Parse(json);
        return Convert.FromBase64String(document.RootElement.GetProperty("data").GetString()!);
    }

    public async Task<string> EvaluateAsync(string expression, bool awaitPromise = false)
    {
        return await CallAsync("Runtime.evaluate", Params(w =>
        {
            w.WriteString("expression", expression);
            w.WriteBoolean("returnByValue", true);
            w.WriteBoolean("awaitPromise", awaitPromise);
        }));
    }

    public static string Params(Action<Utf8JsonWriter> write)
    {
        var buffer = new ArrayBufferWriter<byte>(256);
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            write(writer);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private async Task<string> ResolveAsync(long backendNodeId)
    {
        var json = await CallAsync("DOM.resolveNode", Params(w => w.WriteNumber("backendNodeId", backendNodeId)));
        using var document = JsonDocument.Parse(json);
        return document.RootElement.GetProperty("object").GetProperty("objectId").GetString()!;
    }

    private Task ReleaseAsync(string objectId) =>
        CallAsync("Runtime.releaseObject", Params(w => w.WriteString("objectId", objectId)));

    private static (double X, double Y) Centre(string boxModelJson)
    {
        using var document = JsonDocument.Parse(boxModelJson);
        var quad = document.RootElement.GetProperty("model").GetProperty("content");
        double x = 0;
        double y = 0;
        for (var i = 0; i < 8; i += 2)
        {
            x += quad[i].GetDouble();
            y += quad[i + 1].GetDouble();
        }

        return (x / 4, y / 4);
    }

    private static string? ReadAxValue(JsonElement node, string property)
    {
        if (!node.TryGetProperty(property, out var holder) || !holder.TryGetProperty("value", out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetDouble().ToString(CultureInfo.InvariantCulture),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => value.GetRawText(),
        };
    }

    // A JSON string literal is also a valid JavaScript string literal.
    private static string JsonString(string value) => "\"" + JsonEncodedText.Encode(value).ToString() + "\"";
}
#endif
