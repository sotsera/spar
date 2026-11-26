using Spar.Telemetry;

namespace Spar.Tests.Telemetry;

/// <summary>A relay that keeps what it was given and answers as told, for the route's and the hosts' tests.</summary>
internal sealed class FakeTelemetryRelay : ITelemetryRelay
{
    private readonly List<(byte[] Body, string ContentType)> _forwarded = [];

    public bool Enabled { get; init; } = true;

    public string PageServiceName { get; init; } = "com.example.App-page";

    public int PageDelayMilliseconds => 5000;

    public TelemetryRelayAnswer Answer { get; init; } = new(200, "OK");

    public int Forwarded
    {
        get
        {
            lock (_forwarded)
            {
                return _forwarded.Count;
            }
        }
    }

    public (byte[] Body, string ContentType) Last
    {
        get
        {
            lock (_forwarded)
            {
                return _forwarded[^1];
            }
        }
    }

    public Task<TelemetryRelayAnswer> ForwardTracesAsync(ReadOnlyMemory<byte> body, string contentType, CancellationToken cancellationToken)
    {
        lock (_forwarded)
        {
            _forwarded.Add((body.ToArray(), contentType));
        }

        return Task.FromResult(Answer);
    }
}
