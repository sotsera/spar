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

    /// <summary>Who made the call: the page, a test through the test host, or the devtools of a Development build.</summary>
    SparCaller Caller { get; }

    /// <summary>
    /// The window whose page made the call; null for a call without one (a test through the test host's client, the
    /// devtools, a browser on loopback).
    /// </summary>
    IAppWindow? Window { get; }
}

/// <summary>Who made a call (<see cref="ISparCall.Caller"/>).</summary>
public enum SparCaller
{
    /// <summary>The app's page, through the bridge.</summary>
    Page,

    /// <summary>A test, through the test host's typed client.</summary>
    Test,

    /// <summary>The devtools' <c>call</c> tool, in a Development build.</summary>
    Devtools,
}
