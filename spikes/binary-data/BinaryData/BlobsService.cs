using Spar;

namespace BinaryData;

/// <summary>The spike's commands: bytes to the page, bytes from the page, and the page's measurements.</summary>
[SparContract]
public sealed class BlobsService(BenchResults results)
{
    [SparCommand]
    public Task<byte[]> GetAsync(int size) => Task.FromResult(Payload.Of(size));

    [SparCommand]
    public Task<int> PutAsync(byte[] data) => Task.FromResult(data.Length);

    [SparCommand]
    public Task ReportAsync(string json)
    {
        results.Set(json);
        return Task.CompletedTask;
    }
}

/// <summary>The same bytes the page fetches from its origin (frontend/public/bin), so both paths carry equal data.</summary>
public static class Payload
{
    public static byte[] Of(int size)
    {
        var bytes = new byte[size];
        var state = 2463534242u;
        for (var i = 0; i < size; i++)
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            bytes[i] = (byte)state;
        }

        return bytes;
    }
}

/// <summary>The page's report, for the window test that waits for it.</summary>
public sealed class BenchResults
{
    private readonly TaskCompletionSource<string> _report = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public void Set(string json) => _report.TrySetResult(json);

    public Task<string> WaitAsync(TimeSpan limit) => _report.Task.WaitAsync(limit);
}
