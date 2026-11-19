using System.Text;
using System.Text.RegularExpressions;

namespace Spar.Testing;

/// <summary>
/// The host's <c>ui-ready</c> phase line in the app's log file, the black-box fixture's ready signal (decision "Step 8b's
/// shape: the black-box fixture"): the page connected its bridge. A format Spar keeps stable, pinned by a test in
/// Spar.Tests against the line the Windows host writes:
/// <c>2026-11-19T07:41:07.318Z INFO  Spar.Host  [0f3a9c21] ui-ready at 412.5 ms</c>, the log file's UTC time, the level
/// in five characters, the category, the run id in brackets, the phase and its time from the host's start.
/// </summary>
internal static partial class ReadyLine
{
    public const string Phase = Spar.Hosting.HostPhases.UiReady;

    public static bool IsReady(string line) => Pattern().IsMatch(line);

    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{3}Z INFO  Spar\.Host  \[[0-9a-f]{8}\] ui-ready at \d+(\.\d+)? ms$", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();
}

/// <summary>
/// Reads the app's log file as it grows, from where it last stopped, without keeping it open between reads and with
/// every sharing mode, so the app's writer and its deletion are never in the way.
/// </summary>
internal sealed class LogWatch(string path)
{
    private readonly StringBuilder _partial = new();
    private readonly Decoder _decoder = Encoding.UTF8.GetDecoder();
    private long _position;

    public string Path { get; } = path;

    /// <summary>Whether a complete line read so far matched <paramref name="match"/>.</summary>
    public bool Seen { get; private set; }

    /// <summary>Reads what was added since the last read; true once a line matched.</summary>
    public bool Poll(Func<string, bool> match)
    {
        if (Seen || !File.Exists(Path))
        {
            return Seen;
        }

        try
        {
            using var stream = new FileStream(Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length <= _position)
            {
                return false;
            }

            stream.Position = _position;
            var bytes = new byte[stream.Length - _position];
            stream.ReadExactly(bytes);
            _position += bytes.Length;
            var characters = new char[_decoder.GetCharCount(bytes, flush: false)];
            var count = _decoder.GetChars(bytes, characters, flush: false);
            _partial.Append(characters, 0, count);
        }
        catch (IOException)
        {
            return false;
        }

        var text = _partial.ToString();
        var end = text.LastIndexOf('\n');
        if (end < 0)
        {
            return false;
        }

        _partial.Clear().Append(text, end + 1, text.Length - end - 1);
        foreach (var line in text[..end].Split('\n'))
        {
            if (match(line.TrimEnd('\r')))
            {
                Seen = true;
                break;
            }
        }

        return Seen;
    }
}
