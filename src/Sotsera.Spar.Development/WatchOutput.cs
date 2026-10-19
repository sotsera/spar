using System.Text;

namespace Sotsera.Spar.Development;

/// <summary>Bounded tail of the public CLI output, not a compiler/debugger protocol.</summary>
internal sealed class WatchOutput(string path) : IDisposable
{
    private static readonly UTF8Encoding _utf8 = new(false, true);
    private readonly FileStream _stream = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
    private readonly byte[] _line = new byte[BoundedLineReader.MaximumBytes];
    private int _count;

    internal IReadOnlyList<string> ReadAvailable()
    {
        var lines = new List<string>();
        var remaining = 64 * 1024;
        while (remaining-- > 0)
        {
            var value = _stream.ReadByte();
            if (value < 0)
            {
                break;
            }

            if (value == '\n')
            {
                lines.Add(_utf8.GetString(_line, 0, _count).TrimEnd('\r'));
                _count = 0;
            }
            else
            {
                if (_count == _line.Length)
                {
                    throw new InvalidDataException("A watcher output line exceeds 16 KiB; inspect its retained log.");
                }

                _line[_count++] = (byte)value;
            }
        }

        return lines;
    }

    public void Dispose()
    {
        _stream.Dispose();
    }
}
