using System.ComponentModel;
using Spar.Runtime;

namespace Spar
{
    /// <summary>
    /// Implemented by the app's <c>Program</c>, in the Test variant only, by Spar's generator. The test host calls
    /// <see cref="Run"/> to run the app's own top-level statements inside the test process.
    /// </summary>
    public interface ISparEntryPoint
    {
        static abstract int Run(string[] args);
    }

    /// <summary>Thrown by a typed test client when a call ends in an infrastructure failure instead of a result.</summary>
    public sealed class SparCallFailedException(string contract, string command, SparFailure failure)
        : Exception($"{contract}.{command} failed: {failure}")
    {
        public string Contract { get; } = contract;

        public string Command { get; } = command;

        public SparFailure Failure { get; } = failure;
    }
}

namespace Spar.Runtime
{
    /// <summary>Runs one command by name; the generated typed test clients call it.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public interface ISparInvoker
    {
        ValueTask<SparOutcome> InvokeAsync(string contract, string command, ReadOnlyMemory<byte> request, CancellationToken cancellationToken);
    }
}
