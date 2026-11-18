using System.Buffers;
using System.Text;
using System.Text.Json;

namespace Spar.WindowAutomation.Automation;

/// <summary>
/// The page's in-process DevTools session (<c>CallDevToolsProtocolMethod</c>), with JSON written by hand so the
/// harness stays NativeAOT-clean. The session is the host's own: the host's <c>Page.enable</c> and file-chooser
/// interception live in it, so nothing here ever calls <c>Page.disable</c> or turns the interception off.
/// </summary>
internal sealed class Cdp(View view)
{
    public View View { get; } = view;

    public async Task<JsonElement> CallAsync(string method, Action<Utf8JsonWriter>? write = null)
    {
        var json = await View.CallAsync(method, write is null ? "{}" : Json.Object(write));
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    /// <summary>
    /// <c>Runtime.callFunctionOn</c> with <c>this</c> bound to <paramref name="objectId"/>. <paramref name="arguments"/>
    /// are raw JSON values. Returns the value (<c>returnByValue</c>) or, with <paramref name="byValue"/> false, the
    /// result object (its <c>objectId</c>).
    /// </summary>
    public async Task<JsonElement> CallFunctionAsync(string objectId, string function, string group, bool awaitPromise = false, bool byValue = true, params string[] arguments)
    {
        var result = await CallAsync("Runtime.callFunctionOn", w =>
        {
            w.WriteString("objectId", objectId);
            w.WriteString("functionDeclaration", function);
            w.WriteStartArray("arguments");
            foreach (var argument in arguments)
            {
                w.WriteStartObject();
                w.WritePropertyName("value");
                w.WriteRawValue(argument);
                w.WriteEndObject();
            }

            w.WriteEndArray();
            w.WriteBoolean("returnByValue", byValue);
            w.WriteBoolean("awaitPromise", awaitPromise);
            w.WriteString("objectGroup", group);
        });
        ThrowOnException(result);
        var remote = result.GetProperty("result");
        return byValue ? (remote.TryGetProperty("value", out var value) ? value : default) : remote;
    }

    /// <summary><c>Runtime.evaluate</c>; returns the value or, with <paramref name="byValue"/> false, the remote object.</summary>
    public async Task<JsonElement> EvaluateAsync(string expression, string? group = null, bool awaitPromise = true, bool byValue = true)
    {
        var result = await CallAsync("Runtime.evaluate", w =>
        {
            w.WriteString("expression", expression);
            w.WriteBoolean("returnByValue", byValue);
            w.WriteBoolean("awaitPromise", awaitPromise);
            if (group is not null)
            {
                w.WriteString("objectGroup", group);
            }
        });
        ThrowOnException(result);
        var remote = result.GetProperty("result");
        return byValue ? (remote.TryGetProperty("value", out var value) ? value : default) : remote;
    }

    public Task ReleaseGroupAsync(string group) => CallAsync("Runtime.releaseObjectGroup", w => w.WriteString("objectGroup", group));

    private static void ThrowOnException(JsonElement result)
    {
        if (result.TryGetProperty("exceptionDetails", out var details))
        {
            var text = details.TryGetProperty("exception", out var exception) && exception.TryGetProperty("description", out var description)
                ? description.GetString()
                : details.GetProperty("text").GetString();
            throw new PageException(text ?? "page exception");
        }
    }
}

internal static class Json
{
    public static string Object(Action<Utf8JsonWriter> write)
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

    /// <summary>A JSON string literal (also a valid JavaScript string literal).</summary>
    public static string String(string value) => "\"" + JsonEncodedText.Encode(value).ToString() + "\"";

    public static string Bool(bool value) => value ? "true" : "false";
}

/// <summary>An exception thrown by page script that the harness ran.</summary>
internal sealed class PageException(string message) : Exception(message);

/// <summary>A wait that ran out of time, with what was found at the last attempt.</summary>
internal sealed class WaitTimeoutException(string message) : TimeoutException(message);
