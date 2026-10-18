using Sotsera.Spar.Bridge;

namespace Sotsera.Spar.Bridge.Tests;

[SparContract]
public interface ITestBackend
{
    [SparOperation("change", "counter.change")]
    Task<TestReply> ChangeAsync(TestRequest request, CancellationToken cancellationToken);

    [SparOperation("wait", "work.run")]
    Task<TestReply> WaitAsync(TestRequest request, CancellationToken cancellationToken);

    [SparOperation("denied", "never.granted")]
    Task<TestReply> DeniedAsync(TestRequest request, CancellationToken cancellationToken);
}

public sealed record TestRequest(string Name, int Value);
public sealed record TestReply(string Text, int Value);
