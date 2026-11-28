using System.Text;
using System.Text.Json;
using Spar.Files;
using Spar.Runtime;

namespace Spar.AppWindows;

/// <summary>
/// The page's own window commands, under the host contract <c>spar</c> (decision "M3d's decide-first questions",
/// items 3 and 4):
/// <list type="bullet">
/// <item><c>openWindow</c>, <c>{"kind":"…","params":…}</c>: opens a window of a declared kind, or focuses the open one
/// of a single-instance kind; the declared error <c>notOpened</c> carries the reason (a host without windows of its
/// own, the app closing, the host failing). <c>params</c> is any JSON value, at most <see cref="MaxParamsBytes"/>.</item>
/// <item><c>showModal</c>, the same request for a modal kind: it answers once the modal closes, with the value its page
/// gave <c>closeWindow</c>, or the declared error <c>dismissed</c>; <c>notOpened</c> as for <c>openWindow</c>.</item>
/// <item><c>closeWindow</c>, <c>{"value":…}</c>: the calling window's page closes it, without its guards, and a modal's
/// opener gets the value (<c>windows.closeWith</c>); at most <see cref="MaxParamsBytes"/> too.</item>
/// <item><c>windowParams</c>: the JSON the window's opener gave, or <c>null</c>.</item>
/// <item><c>windowAction</c>, <c>{"action":"…"}</c>: the calling window's own <see cref="IAppWindow"/> operations (item 8):
/// <c>focus</c>, <c>minimize</c>, <c>maximize</c>, <c>restore</c>, <c>close</c> (its close request, answered at once),
/// <c>setSize</c> with <c>width</c> and <c>height</c>, <c>setTitle</c> with <c>title</c>.</item>
/// <item><c>setZoom</c>, <c>{"factor":…}</c>: the page's own zoom, within its window's limits.</item>
/// <item><c>titleBar</c>: the layout of a page-drawn title bar (<see cref="TitleBarLayout"/>), which the host hit-tests
/// from.</item>
/// </list>
/// An unknown kind, a modal kind given to <c>openWindow</c> or another to <c>showModal</c>, or a request that is not
/// this shape, is refused as <c>rejected</c> / <c>invalid-request</c>: the generated <c>WindowKind</c> and
/// <c>ModalKind</c> unions keep those from compiling.
/// </summary>
internal static class WindowCommands
{
    public const string Open = "openWindow";
    public const string ShowModal = "showModal";
    public const string Close = "closeWindow";
    public const string Params = "windowParams";
    public const string Action = "windowAction";
    public const string SetZoom = "setZoom";
    public const string TitleBarLayoutCommand = "titleBar";

    /// <summary>The host event a page hears its zoom from: <c>{"factor":…}</c>, at its start when it is not 1, and at each change.</summary>
    public const string ZoomEvent = "zoom";

    /// <summary>The host event a page-drawn title bar hears its window's state from: <c>{"maximized":…}</c>, at its start and at each change.</summary>
    public const string TitleBarEvent = "titleBar";

    /// <summary>The host event a caption button hears the pointer from where the host gets it (Windows): <c>{"hover":…,"pressed":…}</c>.</summary>
    public const string CaptionEvent = "caption";

    /// <summary>The most JSON a window's params, or a modal's result, may be.</summary>
    public const int MaxParamsBytes = 64 * 1024;

    private const string NoWindows = "This page's host opens no windows of the app: windows open in the app's own desktop windows, not in a browser.";

    private static readonly byte[] Null = "null"u8.ToArray();

    public static bool Handles(string command) => command is Open or ShowModal or Close or Params or Action or SetZoom or TitleBarLayoutCommand;

    public static async ValueTask<SparOutcome> InvokeAsync(HostWindow? window, string command, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        switch (command)
        {
            case Params:
                return SparOutcome.FromValue(window is null || window.Params.IsEmpty ? Null : window.Params);
            case SetZoom:
                return Zoom(window, payload);
            case TitleBarLayoutCommand:
                return TitleBarLayoutPosted(window, payload);
            case Action:
                return WindowAction(window, payload);
            case Close:
                return CloseWith(window, payload);
        }

        if (!TryRead(payload, out var kind, out var parameters, out var problem))
        {
            return SparOutcome.FromFailure(SparFailure.InvalidRequest(problem.Replace(Open, command, StringComparison.Ordinal)));
        }

        if (window?.Windows is not { } windows)
        {
            return Declared("notOpened", NoWindows);
        }

        if (command == ShowModal)
        {
            var (opened, result) = await windows.ShowModalAsync(kind, parameters, window.Id, cancellationToken);
            return result is null ? OpenFailed(opened, kind) : ModalAnswer(result);
        }

        var outcome = await windows.OpenAsync(kind, parameters, window.Id, cancellationToken);
        return outcome.Outcome is OpenOutcome.Opened or OpenOutcome.Focused
            ? SparOutcome.FromValue(ReadOnlyMemory<byte>.Empty)
            : OpenFailed(outcome, kind);
    }

    private static SparOutcome OpenFailed(OpenResult result, string kind) => result.Outcome switch
    {
        OpenOutcome.UnknownKind => SparOutcome.FromFailure(SparFailure.InvalidRequest($"The app declares no window kind '{kind}'.")),
        OpenOutcome.WrongKind => SparOutcome.FromFailure(SparFailure.InvalidRequest(result.Reason!)),
        _ => Declared("notOpened", result.Reason ?? "The window could not be opened."),
    };

    private static SparOutcome ModalAnswer(ModalResult result) => result.Outcome == ModalOutcome.Value
        ? SparOutcome.FromValue(result.Value.IsEmpty ? Null : result.Value)
        : Declared("dismissed", reason: null);

    private static SparOutcome CloseWith(HostWindow? window, ReadOnlyMemory<byte> payload)
    {
        if (!TryReadValue(payload, out var value, out var problem))
        {
            return SparOutcome.FromFailure(SparFailure.InvalidRequest(problem));
        }

        if (window?.Windows is not { } windows)
        {
            return SparOutcome.FromFailure(SparFailure.InvalidRequest(NoWindows));
        }

        return windows.CloseWith(window.Id, value)
            ? SparOutcome.FromValue(ReadOnlyMemory<byte>.Empty)
            : SparOutcome.FromFailure(SparFailure.InvalidRequest(
                "windows.closeWith closes a window of the app's kinds; the main window closes through its close request, which asks its guards."));
    }

    /// <summary>The page's own window does what it asked; a window without a handle (loopback) is refused.</summary>
    private static SparOutcome WindowAction(HostWindow? window, ReadOnlyMemory<byte> payload)
    {
        if (!TryReadAction(payload, out var action, out var problem))
        {
            return SparOutcome.FromFailure(SparFailure.InvalidRequest(problem));
        }

        if (window?.Handle is not { } handle)
        {
            return SparOutcome.FromFailure(SparFailure.InvalidRequest(NoWindows));
        }

        switch (action.Name)
        {
            case "focus":
                handle.Focus();
                break;
            case "minimize":
                handle.Minimize();
                break;
            case "maximize":
                handle.Maximize();
                break;
            case "restore":
                handle.Restore();
                break;
            case "setSize":
                handle.SetSize(action.Width, action.Height);
                break;
            case "setTitle":
                handle.SetTitle(action.Title!);
                break;
            case "close":
                // The page's own guard is asked too; a window that closes takes its page with it, so it is answered now.
                _ = handle.CloseAsync();
                break;
        }

        return SparOutcome.FromValue(ReadOnlyMemory<byte>.Empty);
    }

    /// <summary><c>{"factor":…}</c>, as the page hears its zoom.</summary>
    public static ReadOnlyMemory<byte> ZoomPayload(double factor)
    {
        var buffer = new System.Buffers.ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteNumber("factor"u8, Math.Round(factor, 4));
            writer.WriteEndObject();
        }

        return buffer.WrittenMemory;
    }

    /// <summary>
    /// <c>setZoom</c>, <c>{"factor":…}</c>: the page's own zoom, a finite factor the host keeps within the window's
    /// limits; refused where the window does not zoom.
    /// </summary>
    private static SparOutcome Zoom(HostWindow? window, ReadOnlyMemory<byte> payload)
    {
        if (!TryReadFactor(payload, out var factor))
        {
            return SparOutcome.FromFailure(SparFailure.InvalidRequest(SetZoom + " takes {\"factor\":…}, a number such as 1.25."));
        }

        if (window?.Handle is not AppWindowHandle handle)
        {
            return SparOutcome.FromFailure(SparFailure.InvalidRequest(NoWindows));
        }

        if (!window.ZoomEnabled)
        {
            return SparOutcome.FromFailure(SparFailure.InvalidRequest("This window does not zoom: Program.cs turns its zoom off with Zoom(false)."));
        }

        handle.SetZoom(factor);
        return SparOutcome.FromValue(ReadOnlyMemory<byte>.Empty);
    }

    /// <summary>
    /// <c>titleBar</c>: the layout of the page-drawn title bar (<see cref="TitleBarLayout"/>), which the SDK posts
    /// whenever it changes; refused for a window whose title bar is the system's.
    /// </summary>
    private static SparOutcome TitleBarLayoutPosted(HostWindow? window, ReadOnlyMemory<byte> payload)
    {
        if (window?.TitleBar is not { } titleBar)
        {
            return SparOutcome.FromFailure(SparFailure.InvalidRequest(
                "This window has the system's title bar: Program.cs gives it TitleBar(TitleBar.Custom) for the page to draw one."));
        }

        if (!TitleBarLayout.TryRead(payload, out var layout, out var problem))
        {
            return SparOutcome.FromFailure(SparFailure.InvalidRequest(problem));
        }

        titleBar.Update(layout);
        return SparOutcome.FromValue(ReadOnlyMemory<byte>.Empty);
    }

    internal static bool TryReadFactor(ReadOnlyMemory<byte> payload, out double factor)
    {
        factor = 0;
        try
        {
            using var document = JsonDocument.Parse(payload, new JsonDocumentOptions { MaxDepth = 4 });
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("factor"u8, out var value)
                && value.TryGetDouble(out factor)
                && double.IsFinite(factor)
                && factor > 0;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    internal readonly record struct WindowActionRequest(string Name, int Width, int Height, string? Title);

    /// <summary><c>windowAction</c>'s request: a known action, with a size of 1 to 100,000 or a title of at most 1,024 characters.</summary>
    internal static bool TryReadAction(ReadOnlyMemory<byte> payload, out WindowActionRequest action, out string problem)
    {
        action = default;
        problem = Action + " takes {\"action\":\"focus\"|\"minimize\"|\"maximize\"|\"restore\"|\"close\"|\"setSize\"|\"setTitle\"}, with width and height, or title.";
        string? name = null;
        string? title = null;
        int? width = null;
        int? height = null;
        try
        {
            var reader = new Utf8JsonReader(payload.Span, new JsonReaderOptions { MaxDepth = 4 });
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            {
                return false;
            }

            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                var property = reader.GetString();
                reader.Read();
                switch (property)
                {
                    case "action" when reader.TokenType == JsonTokenType.String:
                        name = reader.GetString();
                        break;
                    case "title" when reader.TokenType == JsonTokenType.String:
                        title = reader.GetString();
                        break;
                    case "width" when reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var w):
                        width = w;
                        break;
                    case "height" when reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var h):
                        height = h;
                        break;
                    default:
                        reader.Skip();
                        break;
                }
            }

            if (reader.TokenType != JsonTokenType.EndObject || reader.Read())
            {
                return false;
            }
        }
        catch (JsonException)
        {
            return false;
        }

        var valid = name switch
        {
            "focus" or "minimize" or "maximize" or "restore" or "close" => true,
            "setSize" => width is >= 1 and <= 100_000 && height is >= 1 and <= 100_000,
            "setTitle" => title is { Length: <= 1_024 },
            _ => false,
        };
        if (!valid)
        {
            return false;
        }

        action = new WindowActionRequest(name!, width ?? 0, height ?? 0, title);
        return true;
    }

    /// <summary>A declared error: <c>{"code":…}</c>, with the reason when there is one.</summary>
    private static SparOutcome Declared(string code, string? reason)
    {
        var buffer = new System.Buffers.ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("code"u8, code);
            if (reason is not null)
            {
                writer.WriteString("reason"u8, reason);
            }

            writer.WriteEndObject();
        }

        return SparOutcome.FromError(buffer.WrittenMemory);
    }

    /// <summary>The request's kind and its params as the page sent them (their own bytes), or why it is not one.</summary>
    internal static bool TryRead(ReadOnlyMemory<byte> payload, out string kind, out ReadOnlyMemory<byte> parameters, out string problem)
    {
        kind = "";
        problem = Open + " takes {\"kind\":\"…\",\"params\":…}.";
        if (!TryReadObject(payload, "kind", "params", out var found, out parameters, ref problem) || found is null)
        {
            return false;
        }

        if (parameters.Length > MaxParamsBytes)
        {
            problem = $"A window's params are at most {MaxParamsBytes / 1024} KB of JSON; these are {parameters.Length} bytes. Keep larger data in the backend and pass its id.";
            return false;
        }

        kind = found;
        return true;
    }

    /// <summary><c>closeWindow</c>'s value as the page sent it (empty for <c>null</c> or none), or why it is not one.</summary>
    internal static bool TryReadValue(ReadOnlyMemory<byte> payload, out ReadOnlyMemory<byte> value, out string problem)
    {
        problem = Close + " takes {\"value\":…}.";
        if (!TryReadObject(payload, kindName: null, "value", out _, out value, ref problem))
        {
            return false;
        }

        if (value.Length > MaxParamsBytes)
        {
            problem = $"A modal's result is at most {MaxParamsBytes / 1024} KB of JSON; this is {value.Length} bytes. Keep larger data in the backend and return its id.";
            return false;
        }

        return true;
    }

    /// <summary>
    /// One JSON object: its string <paramref name="kindName"/> (when asked for), and the raw bytes of
    /// <paramref name="valueName"/> (empty for <c>null</c> or none); other properties are skipped.
    /// </summary>
    private static bool TryReadObject(
        ReadOnlyMemory<byte> payload, string? kindName, string valueName, out string? kind, out ReadOnlyMemory<byte> value, ref string problem)
    {
        kind = null;
        value = ReadOnlyMemory<byte>.Empty;
        try
        {
            var reader = new Utf8JsonReader(payload.Span, new JsonReaderOptions { MaxDepth = 64 });
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            {
                return false;
            }

            var seenValue = false;
            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                var name = reader.GetString();
                reader.Read();
                if (kindName is not null && name == kindName && reader.TokenType == JsonTokenType.String && kind is null)
                {
                    kind = reader.GetString();
                }
                else if (name == valueName && !seenValue)
                {
                    seenValue = true;
                    var start = (int)reader.TokenStartIndex;
                    reader.Skip();
                    var slice = payload[start..(int)reader.BytesConsumed];
                    value = slice.Span.SequenceEqual("null"u8) ? ReadOnlyMemory<byte>.Empty : slice;
                }
                else
                {
                    reader.Skip();
                }
            }

            return reader.TokenType == JsonTokenType.EndObject && !reader.Read();
        }
        catch (JsonException error)
        {
            problem = $"The request is not JSON: {error.Message}";
            return false;
        }
    }

    /// <summary>The params as text, for logs and tests.</summary>
    internal static string Text(ReadOnlyMemory<byte> parameters) => parameters.IsEmpty ? "null" : Encoding.UTF8.GetString(parameters.Span);
}
