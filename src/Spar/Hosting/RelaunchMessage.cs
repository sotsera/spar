using System.Buffers.Binary;
using System.Text;

namespace Spar.Hosting;

/// <summary>
/// A relaunch on the wire between two processes of the app (a named pipe on Windows, a Unix socket on Linux): a version
/// byte, the working directory, the argument count, then each argument, every string as a 32-bit little-endian length
/// and its UTF-8 bytes. The sender is any process of the same user, so the reader checks every length against the
/// bytes it has and against <see cref="MaximumBytes"/>.
/// </summary>
internal static class RelaunchMessage
{
    /// <summary>The largest message either side accepts: far above any real command line (32,767 UTF-16 units).</summary>
    public const int MaximumBytes = 1 << 20;

    private const byte Version = 1;

    /// <summary>Invalid UTF-8 makes the message malformed instead of turning into replacement characters.</summary>
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public static byte[] Encode(Relaunch relaunch)
    {
        var strings = new List<string>(relaunch.Arguments.Count + 1) { relaunch.WorkingDirectory };
        strings.AddRange(relaunch.Arguments);

        // The version byte and the argument count; each string adds its length and bytes below.
        var size = 1 + 4;
        foreach (var text in strings)
        {
            size += 4 + Encoding.UTF8.GetByteCount(text);
        }

        if (size > MaximumBytes)
        {
            throw new ArgumentException($"The relaunch's arguments take {size} bytes, more than {MaximumBytes}.", nameof(relaunch));
        }

        var buffer = new byte[size];
        buffer[0] = Version;
        var position = 1;
        position += WriteString(buffer.AsSpan(position), relaunch.WorkingDirectory);
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(position), relaunch.Arguments.Count);
        position += 4;
        foreach (var argument in relaunch.Arguments)
        {
            position += WriteString(buffer.AsSpan(position), argument);
        }

        return buffer;
    }

    /// <summary>Reads a whole message; false for anything malformed, never an exception.</summary>
    public static bool TryDecode(ReadOnlySpan<byte> message, out Relaunch? relaunch)
    {
        relaunch = null;
        if (message.Length < 9 || message.Length > MaximumBytes || message[0] != Version)
        {
            return false;
        }

        var rest = message[1..];
        if (!TryReadString(ref rest, out var workingDirectory) || !Path.IsPathFullyQualified(workingDirectory))
        {
            return false;
        }

        if (rest.Length < 4)
        {
            return false;
        }

        var count = BinaryPrimitives.ReadInt32LittleEndian(rest);
        rest = rest[4..];

        // Each argument takes at least its 4-byte length, so the count cannot exceed what is left.
        if (count < 0 || count > rest.Length / 4)
        {
            return false;
        }

        var arguments = new string[count];
        for (var index = 0; index < count; index++)
        {
            if (!TryReadString(ref rest, out arguments[index]))
            {
                return false;
            }
        }

        if (!rest.IsEmpty)
        {
            return false;
        }

        relaunch = new Relaunch(arguments, workingDirectory);
        return true;
    }

    private static int WriteString(Span<byte> destination, string text)
    {
        var length = Encoding.UTF8.GetBytes(text, destination[4..]);
        BinaryPrimitives.WriteInt32LittleEndian(destination, length);
        return 4 + length;
    }

    private static bool TryReadString(ref ReadOnlySpan<byte> source, out string text)
    {
        text = string.Empty;
        if (source.Length < 4)
        {
            return false;
        }

        var length = BinaryPrimitives.ReadInt32LittleEndian(source);
        if (length < 0 || length > source.Length - 4)
        {
            return false;
        }

        try
        {
            text = StrictUtf8.GetString(source.Slice(4, length));
        }
        catch (DecoderFallbackException)
        {
            return false;
        }

        source = source[(4 + length)..];
        return true;
    }
}
