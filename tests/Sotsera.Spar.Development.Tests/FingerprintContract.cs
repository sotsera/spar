using Sotsera.Spar.Bridge;

namespace DevelopmentFingerprintFixture;

[SparContract]
public interface IFingerprintContract
{
    [SparOperation("echo", "echo.read")]
    Task<EchoReply> EchoAsync(EchoRequest request, CancellationToken cancellationToken);
}

public sealed record EchoRequest(string Value, string? Optional, int Count, bool Enabled);
public sealed record EchoReply(string Result);
public sealed record UnusedDto(string? Value);
