namespace Spar;

/// <summary>The current call, for the contract and for any scoped service of the call.</summary>
public interface ISparCall
{
    string CallId { get; }

    CancellationToken CancellationToken { get; }

    string Contract { get; }

    string Command { get; }

    SparCaller Caller { get; }
}

/// <summary>Who made a call: the page, a test through the test host, or the devtools of a Development build.</summary>
public enum SparCaller
{
    Page,
    Test,
    Devtools,
}
