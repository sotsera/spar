using System.Buffers;
using System.Text.Json;

namespace Spar.AppWindows;

/// <summary>
/// A window's remembered place, in the host's own units: <see cref="Left"/> and <see cref="Top"/> are where the window's
/// frame is on the screen (Windows: physical pixels of the virtual screen; Linux: the X11 root window's), and
/// <see cref="Width"/> and <see cref="Height"/> the size of its page in CSS pixels at a zoom of 1. No position on
/// Wayland, and none for kinds that keep only their size.
/// </summary>
internal sealed record SavedPlacement(int? Left, int? Top, int Width, int Height, bool Maximized, double Zoom)
{
    public bool HasPosition => Left is not null && Top is not null;
}

/// <summary>What a window keeps of its placement (decision "M3d's decide-first questions", item 5).</summary>
internal enum PlacementRule
{
    /// <summary><c>RememberPlacement(false)</c>: nothing is read or written.</summary>
    None,

    /// <summary>A kind with several windows, or a modal: its size and zoom; where it opens is the host's rule.</summary>
    SizeAndZoom,

    /// <summary>The main window and a single-instance kind: size, position, maximized state and zoom.</summary>
    Full,
}

/// <summary>
/// <c>windows.json</c> in the app's data folder (decision "M3d's decide-first questions", item 5): each kind's place,
/// keyed by kind (<c>main</c> for the main window), read once at start, and written on the thread pool once the changes
/// have been quiet for 500 ms, only when its content changed, through a temporary file so a crash leaves the old one.
/// A file that cannot be read is logged and treated as empty. Any thread.
/// </summary>
internal sealed class PlacementStore
{
    /// <summary>How long the changes must be quiet before the file is written.</summary>
    public static readonly TimeSpan Quiet = TimeSpan.FromMilliseconds(500);

    private const string FileName = "windows.json";

    private readonly Lock _gate = new();
    private readonly Dictionary<string, SavedPlacement> _kinds = new(StringComparer.Ordinal);
    private readonly string _path;
    private readonly Action<string>? _warn;
    private readonly Timer _timer;
    private byte[] _written = [];
    private bool _pending;

    private PlacementStore(string dataRoot, Action<string>? warn)
    {
        _path = Path.Combine(dataRoot, FileName);
        _warn = warn;
        _timer = new Timer(_ => Write(), null, Timeout.Infinite, Timeout.Infinite);
    }

    /// <summary>The file's path.</summary>
    public string FilePath => _path;

    /// <summary>The store of the app whose data folder is <paramref name="dataRoot"/>, read now (off the UI thread).</summary>
    public static PlacementStore Open(string dataRoot, Action<string>? warn = null)
    {
        var store = new PlacementStore(dataRoot, warn);
        store.Read();
        return store;
    }

    /// <summary>What <paramref name="options"/>' window keeps.</summary>
    public static PlacementRule RuleFor(WindowOptions options)
    {
        if (!options.RemembersPlacement)
        {
            return PlacementRule.None;
        }

        var full = options.Kind is null || (options.IsSingleInstance && !options.IsModal);
        return full ? PlacementRule.Full : PlacementRule.SizeAndZoom;
    }

    /// <summary>The place remembered for <paramref name="options"/>' kind, as its rule keeps it; null for none.</summary>
    public SavedPlacement? Get(WindowOptions options)
    {
        var rule = RuleFor(options);
        if (rule == PlacementRule.None)
        {
            return null;
        }

        SavedPlacement? saved;
        lock (_gate)
        {
            saved = _kinds.GetValueOrDefault(Key(options));
        }

        return saved is null || rule == PlacementRule.Full ? saved : saved with { Left = null, Top = null, Maximized = false };
    }

    /// <summary>
    /// The window of <paramref name="options"/>' kind changed: its new place, as its rule keeps it, is written once the
    /// changes settle. A size of zero (a window not laid out yet) is ignored.
    /// </summary>
    public void Update(WindowOptions options, SavedPlacement placement)
    {
        var rule = RuleFor(options);
        if (rule == PlacementRule.None || placement.Width <= 0 || placement.Height <= 0)
        {
            return;
        }

        var kept = rule == PlacementRule.Full ? placement : placement with { Left = null, Top = null, Maximized = false };
        lock (_gate)
        {
            if (_kinds.TryGetValue(Key(options), out var old) && old == kept)
            {
                return;
            }

            _kinds[Key(options)] = kept;
            _pending = true;
            _timer.Change(Quiet, Timeout.InfiniteTimeSpan);
        }
    }

    /// <summary>Writes what is pending now (the app's end), on the calling thread.</summary>
    public void Flush()
    {
        _timer.Change(Timeout.Infinite, Timeout.Infinite);
        Write();
    }

    private static string Key(WindowOptions options) => options.Kind ?? WindowRoutes.Main;

    private void Read()
    {
        byte[] bytes;
        try
        {
            if (!File.Exists(_path))
            {
                return;
            }

            bytes = File.ReadAllBytes(_path);
        }
        catch (IOException error)
        {
            _warn?.Invoke($"{FileName} could not be read ({error.Message}); windows open at their declared size.");
            return;
        }
        catch (UnauthorizedAccessException error)
        {
            _warn?.Invoke($"{FileName} could not be read ({error.Message}); windows open at their declared size.");
            return;
        }

        try
        {
            foreach (var (kind, placement) in Parse(bytes))
            {
                _kinds[kind] = placement;
            }

            _written = bytes;
        }
        catch (JsonException error)
        {
            _warn?.Invoke($"{FileName} is not the file Spar writes ({error.Message}); windows open at their declared size, and it is written anew.");
            _kinds.Clear();
        }
    }

    private void Write()
    {
        byte[] bytes;
        lock (_gate)
        {
            if (!_pending)
            {
                return;
            }

            _pending = false;
            bytes = Serialize(_kinds);
        }

        if (bytes.AsSpan().SequenceEqual(_written))
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temporary = _path + ".tmp";
            File.WriteAllBytes(temporary, bytes);
            File.Move(temporary, _path, overwrite: true);
            _written = bytes;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            _warn?.Invoke($"{FileName} could not be written ({error.Message}); the windows' places are kept for this run only.");
        }
    }

    /// <summary>The file's content: <c>{"version":1,"windows":{"main":{…},…}}</c>, kinds in name order.</summary>
    internal static byte[] Serialize(IReadOnlyDictionary<string, SavedPlacement> kinds)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("version"u8, 1);
            writer.WriteStartObject("windows"u8);
            foreach (var (kind, placement) in kinds.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                writer.WriteStartObject(kind);
                if (placement.HasPosition)
                {
                    writer.WriteNumber("left"u8, placement.Left!.Value);
                    writer.WriteNumber("top"u8, placement.Top!.Value);
                }

                writer.WriteNumber("width"u8, placement.Width);
                writer.WriteNumber("height"u8, placement.Height);
                writer.WriteBoolean("maximized"u8, placement.Maximized);
                writer.WriteNumber("zoom"u8, placement.Zoom);
                writer.WriteEndObject();
            }

            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        return buffer.WrittenSpan.ToArray();
    }

    /// <summary>The kinds of a file <see cref="Serialize"/> wrote; a kind whose place is not sensible is left out.</summary>
    internal static IEnumerable<(string Kind, SavedPlacement Placement)> Parse(byte[] bytes)
    {
        using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 8 });
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("windows"u8, out var windows)
            || windows.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("it has no \"windows\" object");
        }

        var found = new List<(string, SavedPlacement)>();
        foreach (var kind in windows.EnumerateObject())
        {
            if (Placement(kind.Value) is { } placement)
            {
                found.Add((kind.Name, placement));
            }
        }

        return found;
    }

    private static SavedPlacement? Placement(JsonElement entry)
    {
        if (entry.ValueKind != JsonValueKind.Object
            || !Int(entry, "width", out var width) || width is < 1 or > 100_000
            || !Int(entry, "height", out var height) || height is < 1 or > 100_000)
        {
            return null;
        }

        int? left = Int(entry, "left", out var x) ? x : null;
        int? top = Int(entry, "top", out var y) ? y : null;
        var maximized = entry.TryGetProperty("maximized"u8, out var max) && max.ValueKind == JsonValueKind.True;
        var zoom = entry.TryGetProperty("zoom"u8, out var z) && z.TryGetDouble(out var factor) && double.IsFinite(factor) && factor > 0 ? factor : 1;
        return new SavedPlacement(left is null || top is null ? null : left, left is null || top is null ? null : top, width, height, maximized, zoom);
    }

    private static bool Int(JsonElement entry, string name, out int value)
    {
        value = 0;
        return entry.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out value);
    }
}
