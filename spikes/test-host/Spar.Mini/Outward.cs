namespace Spar;

/// <summary>One Spar-owned service that reaches outside the app. The real one would write the OS clipboard.</summary>
public interface IClipboard
{
    Task SetTextAsync(string text, CancellationToken cancellationToken);
}

internal sealed class SystemClipboard : IClipboard
{
    // Spike: stands for the OS call. The isolation test checks that no test-host app ever constructs this type.
    internal static int Constructed;

    public SystemClipboard()
    {
        Interlocked.Increment(ref Constructed);
    }

    public Task SetTextAsync(string text, CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>The test host's default clipboard: it records what the app wrote.</summary>
public sealed class FakeClipboard : IClipboard
{
    private readonly List<string> _writes = [];

    public IReadOnlyList<string> Writes
    {
        get
        {
            lock (_writes)
            {
                return [.. _writes];
            }
        }
    }

    public Task SetTextAsync(string text, CancellationToken cancellationToken)
    {
        lock (_writes)
        {
            _writes.Add(text);
        }

        return Task.CompletedTask;
    }
}
