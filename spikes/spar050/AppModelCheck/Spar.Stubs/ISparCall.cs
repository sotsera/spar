namespace Spar;

/// <summary>The current call, for the contract and for any scoped service of the call.</summary>
public interface ISparCall
{
    string CallId { get; }

    CancellationToken CancellationToken { get; }

    string Contract { get; }

    string Command { get; }
}
