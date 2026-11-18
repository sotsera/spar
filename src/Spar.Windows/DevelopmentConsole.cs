using System.Buffers;
using System.Globalization;
using System.Runtime.InteropServices.Marshalling;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Spar.Hosting;
using Spar.Logging;
using Spar.Windows.Interop;

namespace Spar.Windows;

/// <summary>
/// The page's <c>console.error</c> calls in a Development build (decision "Logging's follow-up choices"), from the
/// in-process DevTools Protocol's <c>Runtime.consoleAPICalled</c> event rather than a wrapper in the injected script, so
/// the console's links point at the page's own code. That event carries the call's own stack, whose first frame is the
/// page's caller; <c>Log.entryAdded</c> no longer reports console calls. The events reach the UI thread for every
/// console call; there only the error ones are copied out and queued, and a task of their own reads them and posts them
/// to the <see cref="PageErrorLog"/> as the script's batches would be: grouped by text and place, at most one batch a
/// second, with the script's bounds per document. Created only behind <see cref="SparDevelopment.IsSupported"/>, so a
/// Production image carries neither this class nor its method and event names (the production boundary test).
/// </summary>
[GeneratedComClass]
internal sealed partial class DevelopmentConsole : ICoreWebView2DevToolsProtocolEventReceivedEventHandler
{
    /// <summary>The DevTools Protocol event read.</summary>
    public const string EventName = "Runtime.consoleAPICalled";

    /// <summary>How many raw events may wait to be read; more are dropped (Development only).</summary>
    private const int QueueLength = 4_096;

    private const int MaxArguments = 10;

    private readonly WebViewBridge _bridge;
    private readonly HostLog _log;
    private readonly Channel<Raw> _queue = Channel.CreateBounded<Raw>(
        new BoundedChannelOptions(QueueLength) { SingleReader = true, FullMode = BoundedChannelFullMode.DropWrite });
    private readonly Dictionary<string, DocumentCount> _documents = new(StringComparer.Ordinal);
    private readonly Task _reader;

    // The last document past its bound, so the UI thread stops queueing its events (written by the reader).
    private volatile string? _stoppedDocument;

    private DevelopmentConsole(ICoreWebView2DevToolsProtocolEventReceiver receiver, WebViewBridge bridge, HostLog log)
    {
        Receiver = receiver;
        _bridge = bridge;
        _log = log;
        _reader = Task.Run(ReadAsync);
    }

    /// <summary>The event's receiver, to subscribe this handler with (the host removes it with the WebView's events).</summary>
    public ICoreWebView2DevToolsProtocolEventReceiver Receiver { get; }

    /// <summary>Completes once <c>Runtime.enable</c> did; a failure is logged and leaves the console unread.</summary>
    public Task Enabled { get; private set; } = Task.CompletedTask;

    /// <summary>On the UI thread, before the first navigation: the receiver, and <c>Runtime.enable</c>.</summary>
    public static DevelopmentConsole Start(ICoreWebView2_22 webView, WebViewBridge bridge, HostLog log)
    {
        var receiver = Com.Take<ICoreWebView2DevToolsProtocolEventReceiver>(webView.GetDevToolsProtocolEventReceiver(EventName));
        var console = new DevelopmentConsole(receiver, bridge, log);
        var enabled = new DevToolsCallCompleted();
        webView.CallDevToolsProtocolMethod("Runtime.enable", "{}", enabled);
        console.Enabled = console.LogEnabledAsync(enabled.Task);
        return console;
    }

    /// <summary>On the UI thread, for every console call of the page: error calls are copied and queued.</summary>
    public void Invoke(nint sender, nint args)
    {
        ICoreWebView2DevToolsProtocolEventReceivedEventArgs? received = null;
        try
        {
            received = Com.Borrow<ICoreWebView2DevToolsProtocolEventReceivedEventArgs>(args);
            var json = received.GetParameterObjectAsJson();
            // A quick look on the UI thread (the keys come sorted, "type" last); the reader checks the parsed type.
            if (json.Contains("\"type\":\"error\"", StringComparison.Ordinal) && _bridge.CurrentDocument is { } document
                && document != _stoppedDocument)
            {
                _queue.Writer.TryWrite(new Raw(document, json));
            }
        }
        catch (Exception exception)
        {
            _log.Error("Development console: an event could not be read", exception);
        }
        finally
        {
            Com.Release(received);
        }
    }

    private async Task LogEnabledAsync(Task<string?> enabled)
    {
        try
        {
            await enabled;
            _log.Info("Development console: the page's console errors come from the DevTools Protocol.");
        }
        catch (Exception exception)
        {
            _log.Error("Development console: Runtime.enable failed; the page's console errors are not logged", exception);
        }
    }

    /// <summary>Reads the queue, groups per document, and posts a batch at most once a second.</summary>
    private async Task ReadAsync()
    {
        var reader = _queue.Reader;
        while (await reader.WaitToReadAsync())
        {
            while (reader.TryRead(out var raw))
            {
                try
                {
                    Count(raw);
                }
                catch (Exception exception) when (exception is JsonException or InvalidOperationException or KeyNotFoundException)
                {
                    _log.Warn($"Development console: an event could not be parsed ({exception.Message}).");
                }
            }

            Flush();
            await Task.Delay(PageScript.ReportIntervalMilliseconds);
        }
    }

    private void Count(Raw raw)
    {
        var error = Parse(raw.Json);
        if (error is null)
        {
            return;
        }

        if (!_documents.TryGetValue(raw.Document, out var document))
        {
            document = new DocumentCount();
            _documents[raw.Document] = document;
        }

        if (document.Stopped)
        {
            return;
        }

        if (++document.Counted > PageScript.ErrorMaxErrors)
        {
            document.Stopped = true;
            document.Dirty = true;
            _stoppedDocument = raw.Document;
            return;
        }

        var key = string.Create(CultureInfo.InvariantCulture, $"{error.Message}\n{error.Source}:{error.Line}:{error.Column}");
        document.Dirty = true;
        if (document.Pending.TryGetValue(key, out var group))
        {
            group.Count++;
        }
        else if (document.Seen.Contains(key) || document.Seen.Count < PageScript.ErrorMaxGroups)
        {
            document.Seen.Add(key);
            document.Pending[key] = error;
        }
        else
        {
            document.Overflow++;
        }
    }

    private void Flush()
    {
        foreach (var (id, document) in _documents)
        {
            if (!document.Dirty)
            {
                continue;
            }

            _bridge.PageErrors.PostBatch(id, Batch(document));
            document.Pending.Clear();
            document.Overflow = 0;
            document.Dirty = false;
        }
    }

    /// <summary>The batch the injected script would have posted, in its shape (<see cref="PageErrorReader"/>).</summary>
    private static byte[] Batch(DocumentCount document)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteStartArray("groups");
            foreach (var error in document.Pending.Values)
            {
                writer.WriteStartObject();
                writer.WriteString("kind", PageErrorReader.Console);
                writer.WriteString("message", error.Message);
                writer.WriteString("source", error.Source);
                writer.WriteNumber("line", error.Line);
                writer.WriteNumber("column", error.Column);
                writer.WriteString("stack", error.Stack);
                writer.WriteNumber("count", error.Count);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteNumber("overflow", document.Overflow);
            if (document.Stopped)
            {
                writer.WriteBoolean("stopped", true);
            }

            writer.WriteEndObject();
        }

        return buffer.WrittenSpan.ToArray();
    }

    /// <summary>
    /// The call's text (its arguments as the console shows them: strings as they are, other values by their
    /// description), the place of its first stack frame, and the stack: the first error argument's, else the call's;
    /// null for a console call that is not an error.
    /// </summary>
    private static Group? Parse(string json)
    {
        using var parsed = JsonDocument.Parse(json);
        var root = parsed.RootElement;
        if (!root.TryGetProperty("type", out var type) || type.GetString() != "error")
        {
            return null;
        }

        var message = new StringBuilder();
        var stack = "";
        if (root.TryGetProperty("args", out var arguments) && arguments.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var argument in arguments.EnumerateArray())
            {
                if (index++ == MaxArguments || message.Length >= PageErrorReader.MaxMessage)
                {
                    break;
                }

                var (text, errorStack) = Describe(argument);
                message.Append(message.Length > 0 ? " " : "").Append(text);
                if (stack.Length == 0)
                {
                    stack = errorStack;
                }
            }
        }

        string source = "", calls = "";
        int line = 0, column = 0;
        if (root.TryGetProperty("stackTrace", out var trace) && trace.TryGetProperty("callFrames", out var frames))
        {
            var text = new StringBuilder();
            foreach (var frame in frames.EnumerateArray())
            {
                var url = frame.GetProperty("url").GetString() ?? "";
                var frameLine = frame.GetProperty("lineNumber").GetInt32() + 1;
                var frameColumn = frame.GetProperty("columnNumber").GetInt32() + 1;
                var function = frame.GetProperty("functionName").GetString() is { Length: > 0 } name ? name : "<anonymous>";
                if (source.Length == 0 && url.Length > 0)
                {
                    (source, line, column) = (url, frameLine, frameColumn);
                }

                text.Append(CultureInfo.InvariantCulture, $"    at {function} ({url}:{frameLine}:{frameColumn})\n");
            }

            calls = text.ToString().TrimEnd('\n');
        }

        return new Group(
            Cut(message.ToString(), PageErrorReader.MaxMessage),
            Cut(source, PageErrorReader.MaxSource),
            line,
            column,
            Cut(stack.Length > 0 ? stack : calls, PageErrorReader.MaxStack));
    }

    /// <summary>One argument as the console shows it, and its stack when it is an error.</summary>
    private static (string Text, string Stack) Describe(JsonElement argument)
    {
        var type = argument.TryGetProperty("type", out var kind) ? kind.GetString() : null;
        if (type == "string" && argument.TryGetProperty("value", out var value))
        {
            return (value.GetString() ?? "", "");
        }

        if (type == "undefined")
        {
            return ("undefined", "");
        }

        var description = argument.TryGetProperty("description", out var text) ? text.GetString() ?? "" :
            argument.TryGetProperty("value", out var raw) ? raw.ToString() :
            argument.TryGetProperty("unserializableValue", out var special) ? special.GetString() ?? "" : type ?? "";
        var isError = argument.TryGetProperty("subtype", out var subtype) && subtype.GetString() == "error";
        if (!isError)
        {
            return (description, "");
        }

        // An error's description is its stack: the first line is its name and message.
        var end = description.IndexOf('\n', StringComparison.Ordinal);
        return (end < 0 ? description : description[..end], description);
    }

    private static string Cut(string text, int limit) => text.Length <= limit ? text : text[..limit];

    private sealed record Raw(string Document, string Json);

    private sealed class Group(string message, string source, int line, int column, string stack)
    {
        public string Message { get; } = message;

        public string Source { get; } = source;

        public int Line { get; } = line;

        public int Column { get; } = column;

        public string Stack { get; } = stack;

        public long Count { get; set; } = 1;
    }

    private sealed class DocumentCount
    {
        public Dictionary<string, Group> Pending { get; } = new(StringComparer.Ordinal);

        public HashSet<string> Seen { get; } = new(StringComparer.Ordinal);

        public long Counted { get; set; }

        public long Overflow { get; set; }

        public bool Stopped { get; set; }

        public bool Dirty { get; set; }
    }
}
