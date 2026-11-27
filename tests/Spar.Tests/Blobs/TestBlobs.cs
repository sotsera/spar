using System.Text;
using Spar.Runtime;

namespace Spar.Tests.Blobs;

/// <summary>Hand-written blob commands, standing in for what the generator writes for a result or a resource.</summary>
internal static class TestBlobs
{
    public static IEnumerable<SparCommandEntry> Entries =>
    [
        new("files", "make", 0, MakeAsync),
        new("files", "photo", 0, PhotoAsync) { IsResource = true },
    ];

    /// <summary><paramref name="size"/> bytes, each its index modulo 251, so a test can tell every byte arrived in place.</summary>
    public static byte[] Pattern(int size)
    {
        var bytes = new byte[size];
        for (var i = 0; i < size; i++)
        {
            bytes[i] = (byte)(i % 251);
        }

        return bytes;
    }

    /// <summary><c>files.make(size)</c>: a <c>byte[]</c> handle of that size, written as the generator writes a result.</summary>
    private static ValueTask<SparOutcome> MakeAsync(SparCallContext call)
    {
        var size = int.Parse(Encoding.UTF8.GetString(call.Request.Span), System.Globalization.CultureInfo.InvariantCulture);
        var blob = SparBlob.FromBytes(Pattern(size), "application/octet-stream", "made.bin");
        try
        {
            using var blobs = SparBlobWrite.Begin(call);
            var json = SparJson.Write(blob, SparBlobWrite.Write);
            blobs.Commit();
            return ValueTask.FromResult(SparOutcome.FromValue(json));
        }
        catch (Exception exception)
        {
            return ValueTask.FromResult(SparOutcome.FromFailure(SparFailure.ResponseSerialization(exception)));
        }
    }

    /// <summary><c>files.photo(n)</c>: a resource; the PNG signature then <c>n</c> as text, or none for a negative one.</summary>
    private static ValueTask<SparOutcome> PhotoAsync(SparCallContext call)
    {
        // As generated code refuses a request its reader cannot read.
        if (!int.TryParse(Encoding.UTF8.GetString(call.Request.Span), System.Globalization.CultureInfo.InvariantCulture, out var n))
        {
            return ValueTask.FromResult(SparOutcome.FromFailure(SparFailure.InvalidRequest("files.photo takes a number.")));
        }

        call.Resource = n < 0 ? null : SparBlob.FromBytes([0x89, .. "PNG"u8, .. Encoding.ASCII.GetBytes($"#{n}")], "image/png");
        return ValueTask.FromResult(SparOutcome.FromValue(ReadOnlyMemory<byte>.Empty));
    }
}
