using System.Buffers;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Spar.Devtools;

/// <summary>
/// The page tools (decision "M2a's decide-first questions", items 4, 5 and 8): the window tests' engine finds the
/// elements, by a reference from the last <c>tree</c> of the current document or by a locator, exactly as the tests'
/// locators do. A click waits for its element to be actionable, clicks (trusted on Windows, the page's own events on
/// Linux), then waits for the calls it caused to settle and lists them.
/// </summary>
internal sealed partial class DevtoolsTools
{
    /// <summary>How long a page action waits for its element by default.</summary>
    private static readonly TimeSpan ActionWait = TimeSpan.FromSeconds(5);

    /// <summary>After an action, how long no call may run for its calls to count as settled, and the most it waits.</summary>
    private static readonly TimeSpan Quiet = TimeSpan.FromMilliseconds(300);

    private static readonly TimeSpan Settle = TimeSpan.FromSeconds(3);

    /// <summary>The locators that match an element by a text: its content, its label, or one of its attributes.</summary>
    private static readonly string[] TextKinds = ["text", "label", "placeholder", "alt", "title"];

    private readonly Lock _pageGate = new();
    private long _treeDocument = -1;
    private long _framedDocument = -1;

    private IDevtoolsPage Page() => _context.Pages.Current
        ?? throw new DevtoolsError(_context.Pages.Unavailable is null ? "no_page" : "unavailable",
            _context.Pages.Unavailable ?? "The window has no page yet (it is starting, or it closed); try again.");

    private async Task<Action<Utf8JsonWriter>> TreeAsync(JsonElement parameters)
    {
        var page = Page();
        var limit = Math.Clamp(Number(parameters, "limit") ?? 2000, 1, 5000);
        var document = page.Document;
        var answer = await EngineAsync(page, Request("snapshot", writer => writer.WriteNumber("limit", limit)), ActionWait)
            ?? throw new DevtoolsError("timeout", "The page did not answer within 5 s; it may be loading or busy.");
        using var parsed = JsonDocument.Parse(answer);
        if (Text(parsed.RootElement, "error") is { } error)
        {
            throw new DevtoolsError("invalid", "The page engine failed: " + error);
        }

        lock (_pageGate)
        {
            _treeDocument = document;
        }

        var raw = answer;
        return writer =>
        {
            writer.WriteStartObject();
            writer.WriteNumber("document", document);
            using var copy = JsonDocument.Parse(raw);
            foreach (var property in copy.RootElement.EnumerateObject())
            {
                property.WriteTo(writer);
            }

            writer.WriteEndObject();
        };
    }

    private async Task<Action<Utf8JsonWriter>> ClickAsync(JsonElement parameters, CancellationToken cancellationToken)
    {
        var page = Page();
        var chain = Chain(parameters, page);
        var wait = Milliseconds(parameters, "timeout") ?? ActionWait;
        var first = Calls.Next;
        await BeforeInputAsync(page, wait);
        var clicked = await RetryAsync("click", wait, cancellationToken, async left =>
        {
            if (!page.TrustedInput)
            {
                return await EngineAsync(page, Request("syntheticClick", writer => WriteChain(writer, chain)), left);
            }

            var answer = await EngineAsync(page, Request("actionable", writer =>
            {
                WriteChain(writer, chain);
                writer.WriteBoolean("enabled", true);
                writer.WriteBoolean("pointer", true);
            }), left);
            if (answer is null || !IsOk(answer, out var ready))
            {
                return answer;
            }

            await page.ClickAsync(ready.X, ready.Y);
            var landed = await EngineAsync(page, Request("landed", _ => { }), left);
            return LandedRetry(landed) is { } again ? Retry(again) : answer;
        });
        return await AfterActionAsync(first, clicked, cancellationToken);
    }

    private async Task<Action<Utf8JsonWriter>> FillAsync(JsonElement parameters, CancellationToken cancellationToken)
    {
        var page = Page();
        var chain = Chain(parameters, page);
        var text = parameters.TryGetProperty("text", out var given) && given.ValueKind == JsonValueKind.String
            ? given.GetString()!
            : throw Invalid("fill needs \"text\", the text that replaces the field's.");
        var wait = Milliseconds(parameters, "timeout") ?? ActionWait;
        var first = Calls.Next;
        var prepared = await RetryAsync("fill", wait, cancellationToken, async left =>
            await EngineAsync(page, Request("prepareFill", writer =>
            {
                WriteChain(writer, chain);
                writer.WriteString("value", text);
            }), left));
        using (var parsed = JsonDocument.Parse(prepared))
        {
            if (!(parsed.RootElement.TryGetProperty("done", out var done) && done.ValueKind == JsonValueKind.True))
            {
                await BeforeInputAsync(page, wait);
                if (page.TrustedInput && text.Length > 0)
                {
                    await page.InsertTextAsync(text);
                }
                else
                {
                    // Empty text deletes the selected text, which an insertion of nothing does not do through CDP.
                    await EngineAsync(page, Request("insertText", writer => writer.WriteString("text", text)), wait);
                }
            }
        }

        return await AfterActionAsync(first, prepared, cancellationToken);
    }

    private async Task<Action<Utf8JsonWriter>> WaitAsync(JsonElement parameters, CancellationToken cancellationToken)
    {
        var page = Page();
        var chain = Chain(parameters, page);
        var wanted = Text(parameters, "state") ?? "visible";
        if (wanted is not ("visible" or "hidden" or "attached" or "detached"))
        {
            throw Invalid($"state is '{wanted}': visible, hidden, attached or detached.");
        }

        var wait = Milliseconds(parameters, "timeout") ?? ActionWait;
        var clock = Stopwatch.StartNew();
        var answer = await RetryAsync("wait", wait, cancellationToken, async left =>
        {
            var state = await EngineAsync(page, Request("state", writer => WriteChain(writer, chain)), left);
            if (state is null)
            {
                return null;
            }

            using var parsed = JsonDocument.Parse(state);
            var root = parsed.RootElement;
            var count = root.TryGetProperty("count", out var number) && number.TryGetInt32(out var n) ? n : 0;
            var visible = count == 1 && root.TryGetProperty("visible", out var shown) && shown.ValueKind == JsonValueKind.True;
            var reached = wanted switch
            {
                "visible" => visible,
                "hidden" => count == 0 || (count == 1 && !visible),
                "attached" => count > 0,
                _ => count == 0,
            };
            if (count > 1 && wanted is "visible" or "hidden")
            {
                // Which one should be visible is not said: the target must name one element, as an action's does.
                var matches = root.TryGetProperty("matches", out var found) ? found.GetRawText() : "[]";
                return $"{{\"strict\":{count},\"matches\":{matches}}}";
            }

            return reached
                ? $"{{\"ok\":true,\"element\":{JsonSerializerString(Text(root, "element") ?? "")}}}"
                : Retry($"not {wanted} yet");
        });
        return writer =>
        {
            writer.WriteStartObject();
            writer.WriteString("state", wanted);
            writer.WriteNumber("milliseconds", Math.Round(clock.Elapsed.TotalMilliseconds));
            using var parsed = JsonDocument.Parse(answer);
            if (Text(parsed.RootElement, "element") is { Length: > 0 } element)
            {
                writer.WriteString("element", element);
            }

            writer.WriteEndObject();
        };
    }

    private async Task<Action<Utf8JsonWriter>> ScreenshotAsync()
    {
        var png = await Page().ScreenshotAsync();
        var (width, height) = PngSize(png);
        return writer =>
        {
            writer.WriteStartObject();
            writer.WriteString("format", "png");
            writer.WriteNumber("width", width);
            writer.WriteNumber("height", height);
            writer.WriteNumber("bytes", png.Length);
            writer.WriteBase64String("data", png);
            writer.WriteEndObject();
        };
    }

    /// <summary>The calls an action caused: from its start until none runs and none started for <see cref="Quiet"/>.</summary>
    private async Task<Action<Utf8JsonWriter>> AfterActionAsync(long first, string answer, CancellationToken cancellationToken)
    {
        var settle = Stopwatch.StartNew();
        while (settle.Elapsed < Settle)
        {
            var last = Calls.LastActivity;
            var quietFor = last == 0 ? TimeSpan.MaxValue : Stopwatch.GetElapsedTime(last);
            if (!Calls.Running(first) && quietFor >= Quiet)
            {
                break;
            }

            await Task.Delay(50, cancellationToken);
        }

        var calls = Calls.Since(first);
        return writer =>
        {
            writer.WriteStartObject();
            using var parsed = JsonDocument.Parse(answer);
            var root = parsed.RootElement;
            if (Text(root, "element") is { } element)
            {
                writer.WriteString("element", element);
            }

            if (root.TryGetProperty("synthetic", out var synthetic) && synthetic.ValueKind == JsonValueKind.True)
            {
                writer.WriteBoolean("synthetic", true);
            }

            writer.WriteStartArray("calls");
            foreach (var call in calls)
            {
                call.Write(writer);
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        };
    }

    /// <summary>
    /// Tries an engine step until it answers <c>ok</c> or <paramref name="wait"/> runs out: a <c>retry</c> waits and
    /// tries again, <c>strict</c> is <c>ambiguous</c>, <c>error</c> is <c>invalid</c>, and running out is <c>timeout</c>.
    /// </summary>
    private static async Task<string> RetryAsync(string tool, TimeSpan wait, CancellationToken cancellationToken, Func<TimeSpan, Task<string?>> step)
    {
        var clock = Stopwatch.StartNew();
        var last = "the page did not answer";
        while (true)
        {
            var left = wait - clock.Elapsed;
            if (left <= TimeSpan.Zero)
            {
                throw new DevtoolsError("timeout", $"{tool} did not finish within {wait.TotalMilliseconds:0} ms: {last}.");
            }

            var answer = await step(left);
            if (answer is not null)
            {
                using var parsed = JsonDocument.Parse(answer);
                var root = parsed.RootElement;
                if (root.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.True)
                {
                    return answer;
                }

                if (Text(root, "error") is { } error)
                {
                    throw new DevtoolsError("invalid", error);
                }

                if (Text(root, "stale") is { } stale)
                {
                    throw new DevtoolsError("stale_target", stale);
                }

                if (root.TryGetProperty("strict", out var strict) && strict.TryGetInt32(out var count))
                {
                    var matches = root.TryGetProperty("matches", out var found) ? found.Clone() : default;
                    throw new DevtoolsError("ambiguous", $"The target matches {count} elements; name one (a tree reference, nth, or a closer locator).")
                    {
                        Details = writer =>
                        {
                            if (matches.ValueKind == JsonValueKind.Array)
                            {
                                writer.WritePropertyName("matches");
                                matches.WriteTo(writer);
                            }
                        },
                    };
                }

                last = Text(root, "retry") ?? last;
                if (root.TryGetProperty("explanation", out var explanation) && explanation.ValueKind == JsonValueKind.String)
                {
                    last += " (" + explanation.GetString() + ")";
                }
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);
        }
    }

    /// <summary>Two animation frames after each new document, before its first input (the WebView2 skill: else up to 5 s).</summary>
    private async Task BeforeInputAsync(IDevtoolsPage page, TimeSpan wait)
    {
        var document = page.Document;
        lock (_pageGate)
        {
            if (_framedDocument == document)
            {
                return;
            }
        }

        await EngineAsync(page, Request("frames", _ => { }), wait);
        lock (_pageGate)
        {
            _framedDocument = document;
        }
    }

    /// <summary>
    /// The target as the engine's locator chain: <c>{"ref":"e12"}</c> from the last <c>tree</c> of this document, or a
    /// locator, <c>role</c> (with <c>name</c>), <c>text</c>, <c>label</c>, <c>placeholder</c>, <c>alt</c>, <c>title</c>,
    /// <c>testId</c> or <c>css</c>, with <c>exact</c>, and <c>nth</c> to pick one of several.
    /// </summary>
    private byte[] Chain(JsonElement parameters, IDevtoolsPage page)
    {
        if (parameters.ValueKind != JsonValueKind.Object || !parameters.TryGetProperty("target", out var target) || target.ValueKind != JsonValueKind.Object)
        {
            throw Invalid("The tool needs \"target\": {\"ref\":\"e12\"} from tree, or a locator such as {\"role\":\"button\",\"name\":\"Save\"}.");
        }

        var output = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(output))
        {
            writer.WriteStartArray();
            if (Text(target, "ref") is { } reference)
            {
                lock (_pageGate)
                {
                    if (_treeDocument != page.Document)
                    {
                        throw new DevtoolsError("stale_target", _treeDocument < 0
                            ? $"{reference} names an element of a tree; take tree first."
                            : $"{reference} is from the tree of an earlier document; the page changed, so take tree again.");
                    }
                }

                writer.WriteStartObject();
                writer.WriteString("kind", "ref");
                writer.WriteString("ref", reference);
                writer.WriteEndObject();
            }
            else
            {
                WriteLocator(writer, target);
            }

            if (Number(target, "nth") is { } nth)
            {
                writer.WriteStartObject();
                writer.WriteString("kind", "nth");
                writer.WriteNumber("index", nth);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        }

        return output.WrittenSpan.ToArray();
    }

    private static void WriteLocator(Utf8JsonWriter writer, JsonElement target)
    {
        var exact = target.TryGetProperty("exact", out var flag) && flag.ValueKind == JsonValueKind.True;
        writer.WriteStartObject();
        if (Text(target, "role") is { } role)
        {
            writer.WriteString("kind", "role");
            writer.WriteString("role", role);
            if (Text(target, "name") is { } name)
            {
                writer.WriteString("name", name);
            }
        }
        else if (Text(target, "testId") is { } testId)
        {
            writer.WriteString("kind", "css");
            writer.WriteString("selector", $"[data-testid=\"{testId.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)}\"]");
        }
        else if (Text(target, "css") is { } css)
        {
            writer.WriteString("kind", "css");
            writer.WriteString("selector", css);
        }
        else if (FirstOf(target, TextKinds) is { } found)
        {
            writer.WriteString("kind", found.Kind);
            writer.WriteString("text", found.Text);
        }
        else
        {
            throw Invalid("The target names no element: give ref, role (and name), text, label, placeholder, alt, title, testId or css.");
        }

        if (exact)
        {
            writer.WriteBoolean("exact", true);
        }

        writer.WriteEndObject();
    }

    private static (string Kind, string Text)? FirstOf(JsonElement target, string[] kinds)
    {
        foreach (var kind in kinds)
        {
            if (Text(target, kind) is { } text)
            {
                return (kind, text);
            }
        }

        return null;
    }

    private static string Request(string op, Action<Utf8JsonWriter> fields)
    {
        var output = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(output))
        {
            writer.WriteStartObject();
            writer.WriteString("op", op);
            fields(writer);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(output.WrittenSpan);
    }

    private static void WriteChain(Utf8JsonWriter writer, byte[] chain)
    {
        writer.WritePropertyName("chain");
        writer.WriteRawValue(chain, skipInputValidation: true);
    }

    private static Task<string?> EngineAsync(IDevtoolsPage page, string request, TimeSpan wait) => page.EngineAsync(request, wait);

    private static bool IsOk(string answer, out (double X, double Y) point)
    {
        using var parsed = JsonDocument.Parse(answer);
        var root = parsed.RootElement;
        point = (Double(root, "x"), Double(root, "y"));
        return root.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.True;
    }

    /// <summary>Why a trusted press must be sent again, as the window tests' click decides it; null when it landed.</summary>
    private static string? LandedRetry(string? landed)
    {
        if (landed is null)
        {
            return null;
        }

        using var parsed = JsonDocument.Parse(landed);
        var root = parsed.RootElement;
        bool Flag(string name) => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
        if (Flag("seen") && !Flag("onTarget"))
        {
            return $"{Text(root, "hit")} received the press instead";
        }

        if (Flag("armed") && !Flag("seen"))
        {
            return "the press did not reach the page";
        }

        return Flag("moved") ? "it moved between the press and the release" : null;
    }

    private static string Retry(string why) => $"{{\"retry\":{JsonSerializerString(why)}}}";

    private static string JsonSerializerString(string text)
    {
        var output = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(output))
        {
            writer.WriteStringValue(text);
        }

        return Encoding.UTF8.GetString(output.WrittenSpan);
    }

    private static double Double(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.TryGetDouble(out var number) ? number : 0;

    /// <summary>The PNG's size from its IHDR chunk; zero when it is not a PNG.</summary>
    private static (int Width, int Height) PngSize(byte[] png) =>
        png.Length >= 24 ? (BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16)), BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20))) : (0, 0);
}
