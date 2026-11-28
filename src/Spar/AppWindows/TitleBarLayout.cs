using System.Buffers;
using System.Text.Json;

namespace Spar.AppWindows;

/// <summary>What a point of a page-drawn title bar is, as the host hit-tests it.</summary>
internal enum TitleBarHit
{
    /// <summary>The page's own: a control in the bar, or the page below it.</summary>
    Client,

    /// <summary>The bar's empty part, which moves the window, maximizes it on a double click and shows its menu.</summary>
    Drag,

    Minimize,
    Maximize,
    Close,
}

/// <summary>A rectangle of the page's viewport, in CSS pixels.</summary>
internal readonly record struct LayoutRect(double X, double Y, double Width, double Height)
{
    public bool Contains(double x, double y) => x >= X && x < X + Width && y >= Y && y < Y + Height;
}

/// <summary>
/// The layout of a page-drawn title bar (<c>docs/app-model.md</c> 6.9), as the SDK posts it whenever it changes: the
/// drag areas (<c>data-spar-titlebar</c>), the controls inside them that stay the page's, and the caption buttons, in
/// CSS pixels of the viewport. The host hit-tests from it alone, without asking the page, so the answer never waits on
/// script (plan 3, "Title bar drawn by the page"). Immutable: the bridge swaps in a new one.
/// </summary>
internal sealed class TitleBarLayout
{
    /// <summary>The most drag areas a layout may hold.</summary>
    public const int MaxBars = 16;

    /// <summary>The most controls inside the drag areas a layout may hold.</summary>
    public const int MaxHoles = 256;

    /// <summary>The most JSON a layout may be.</summary>
    public const int MaxBytes = 32 * 1024;

    private const string Shape =
        "titleBar takes {\"bars\":[[x,y,width,height],…],\"holes\":[…],\"minimize\":[…],\"maximize\":[…],\"close\":[…]}, in CSS pixels";

    private TitleBarLayout(LayoutRect[] bars, LayoutRect[] holes, LayoutRect? minimize, LayoutRect? maximize, LayoutRect? close)
    {
        Bars = bars;
        Holes = holes;
        Minimize = minimize;
        Maximize = maximize;
        Close = close;
    }

    /// <summary>No title bar yet: every point is the page's.</summary>
    public static TitleBarLayout Empty { get; } = new([], [], null, null, null);

    public IReadOnlyList<LayoutRect> Bars { get; }

    public IReadOnlyList<LayoutRect> Holes { get; }

    public LayoutRect? Minimize { get; }

    public LayoutRect? Maximize { get; }

    public LayoutRect? Close { get; }

    /// <summary>The caption buttons that are there, for a host that keeps a window over them.</summary>
    public IEnumerable<LayoutRect> Buttons => new[] { Minimize, Maximize, Close }.OfType<LayoutRect>();

    /// <summary>What the point (<paramref name="x"/>, <paramref name="y"/>), in CSS pixels of the viewport, is.</summary>
    public TitleBarHit HitTest(double x, double y)
    {
        if (Close is { } close && close.Contains(x, y))
        {
            return TitleBarHit.Close;
        }

        if (Maximize is { } maximize && maximize.Contains(x, y))
        {
            return TitleBarHit.Maximize;
        }

        if (Minimize is { } minimize && minimize.Contains(x, y))
        {
            return TitleBarHit.Minimize;
        }

        if (!InAny(Bars, x, y))
        {
            return TitleBarHit.Client;
        }

        return InAny(Holes, x, y) ? TitleBarHit.Client : TitleBarHit.Drag;
    }

    /// <summary>Whether <paramref name="other"/> has the same rectangles, so posting it again changes nothing.</summary>
    public bool SameAs(TitleBarLayout other) =>
        Minimize == other.Minimize
        && Maximize == other.Maximize
        && Close == other.Close
        && Bars.SequenceEqual(other.Bars)
        && Holes.SequenceEqual(other.Holes);

    /// <summary>
    /// The layout the page posted: at most <see cref="MaxBars"/> drag areas and <see cref="MaxHoles"/> controls, each a
    /// rectangle of four finite numbers within ±100,000 with a size that is not negative; or why it is not one.
    /// </summary>
    public static bool TryRead(ReadOnlyMemory<byte> payload, out TitleBarLayout layout, out string problem)
    {
        layout = Empty;
        problem = Shape + ".";
        if (payload.Length > MaxBytes)
        {
            problem = $"A title bar's layout is at most {MaxBytes / 1024} KB of JSON; this one is {payload.Length} bytes.";
            return false;
        }

        try
        {
            return TryParse(payload.Span, out layout, ref problem);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryParse(ReadOnlySpan<byte> json, out TitleBarLayout layout, ref string problem)
    {
        layout = Empty;
        var reader = new Utf8JsonReader(json, new JsonReaderOptions { MaxDepth = 4 });
        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
        {
            return false;
        }

        List<LayoutRect> bars = [];
        List<LayoutRect> holes = [];
        LayoutRect? minimize = null;
        LayoutRect? maximize = null;
        LayoutRect? close = null;
        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            var property = reader.GetString();
            reader.Read();
            var read = property switch
            {
                "bars" => TryReadList(ref reader, bars, MaxBars),
                "holes" => TryReadList(ref reader, holes, MaxHoles),
                "minimize" => TryReadButton(ref reader, out minimize),
                "maximize" => TryReadButton(ref reader, out maximize),
                "close" => TryReadButton(ref reader, out close),
                _ => Skip(ref reader),
            };
            if (!read)
            {
                problem = $"{Shape}: '{property}' is not a list of at most {(property == "bars" ? MaxBars : MaxHoles)} rectangles or a rectangle.";
                return false;
            }
        }

        if (reader.TokenType != JsonTokenType.EndObject || reader.Read())
        {
            return false;
        }

        layout = new TitleBarLayout([.. bars], [.. holes], minimize, maximize, close);
        return true;
    }

    private static bool TryReadList(ref Utf8JsonReader reader, List<LayoutRect> rects, int max)
    {
        if (reader.TokenType != JsonTokenType.StartArray)
        {
            return false;
        }

        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            if (rects.Count == max || !TryReadRect(ref reader, out var rect))
            {
                return false;
            }

            rects.Add(rect);
        }

        return reader.TokenType == JsonTokenType.EndArray;
    }

    private static bool TryReadButton(ref Utf8JsonReader reader, out LayoutRect? button)
    {
        button = null;
        if (reader.TokenType == JsonTokenType.Null)
        {
            return true;
        }

        if (!TryReadRect(ref reader, out var rect))
        {
            return false;
        }

        button = rect;
        return true;
    }

    /// <summary>A rectangle <c>[x, y, width, height]</c>, at the reader's start of an array.</summary>
    private static bool TryReadRect(ref Utf8JsonReader reader, out LayoutRect rect)
    {
        rect = default;
        if (reader.TokenType != JsonTokenType.StartArray)
        {
            return false;
        }

        Span<double> values = stackalloc double[4];
        var count = 0;
        while (reader.Read() && reader.TokenType == JsonTokenType.Number)
        {
            if (count == 4 || !reader.TryGetDouble(out var value) || !double.IsFinite(value) || Math.Abs(value) > 100_000)
            {
                return false;
            }

            values[count++] = value;
        }

        if (reader.TokenType != JsonTokenType.EndArray || count != 4 || values[2] < 0 || values[3] < 0)
        {
            return false;
        }

        rect = new LayoutRect(values[0], values[1], values[2], values[3]);
        return true;
    }

    private static bool Skip(ref Utf8JsonReader reader)
    {
        reader.Skip();
        return true;
    }

    private static bool InAny(IReadOnlyList<LayoutRect> rects, double x, double y)
    {
        foreach (var rect in rects)
        {
            if (rect.Contains(x, y))
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>
/// A window's page-drawn title bar as its host and its bridge share it: the page's last layout, which the bridge's
/// thread swaps in and the UI thread hit-tests from, and whether the window is maximized, which the page hears as the
/// event <see cref="WindowCommands.TitleBarEvent"/>. Null on <see cref="Files.HostWindow.TitleBar"/> for a window with
/// the system's title bar.
/// </summary>
internal sealed class TitleBarState
{
    private TitleBarLayout _layout = TitleBarLayout.Empty;
    private volatile bool _maximized;

    /// <summary>The page's last layout; <see cref="TitleBarLayout.Empty"/> until it posts one.</summary>
    public TitleBarLayout Layout => Volatile.Read(ref _layout);

    /// <summary>Whether the window is maximized, as the host last saw it.</summary>
    public bool Maximized
    {
        get => _maximized;
        set => _maximized = value;
    }

    /// <summary>A new layout came, on the bridge's thread: a host that keeps native state from it posts its update.</summary>
    public Action? LayoutChanged { get; init; }

    /// <summary>Takes the page's layout; false when it is the one the host has already.</summary>
    public bool Update(TitleBarLayout layout)
    {
        var current = Volatile.Read(ref _layout);
        if (current.SameAs(layout))
        {
            return false;
        }

        Volatile.Write(ref _layout, layout);
        LayoutChanged?.Invoke();
        return true;
    }

    /// <summary>The event the page hears its window's state from: <c>{"maximized":…}</c>.</summary>
    public static ReadOnlyMemory<byte> StatePayload(bool maximized) =>
        maximized ? """{"maximized":true}"""u8.ToArray() : """{"maximized":false}"""u8.ToArray();

    /// <summary>
    /// The event the page hears a caption button's pointer from where the host, not the page, gets it (Windows):
    /// <c>{"hover":"close"|null,"pressed":…}</c>, so the button shows its hover and pressed look.
    /// </summary>
    public static ReadOnlyMemory<byte> CaptionPayload(TitleBarHit hover, TitleBarHit pressed)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            WriteButton(writer, "hover"u8, hover);
            WriteButton(writer, "pressed"u8, pressed);
            writer.WriteEndObject();
        }

        return buffer.WrittenMemory;
    }

    private static void WriteButton(Utf8JsonWriter writer, ReadOnlySpan<byte> name, TitleBarHit hit)
    {
        var button = hit switch
        {
            TitleBarHit.Minimize => "minimize",
            TitleBarHit.Maximize => "maximize",
            TitleBarHit.Close => "close",
            _ => null,
        };
        if (button is null)
        {
            writer.WriteNull(name);
        }
        else
        {
            writer.WriteString(name, button);
        }
    }
}
