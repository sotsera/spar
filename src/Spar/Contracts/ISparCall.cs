namespace Spar;

/// <summary>The current call, for the contract and for any scoped service of the call.</summary>
public interface ISparCall
{
    /// <summary>The call's id within its document (the page's id, or one the test host or the C# caller chose).</summary>
    long CallId { get; }

    /// <summary>Ends when the caller cancels, the document goes away, or the command's own time limit passes.</summary>
    CancellationToken CancellationToken { get; }

    string Contract { get; }

    string Command { get; }
}
