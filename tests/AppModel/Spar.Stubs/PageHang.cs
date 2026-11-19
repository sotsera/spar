namespace Spar;

/// <summary>What changed about a window's page that stopped answering (3.16).</summary>
public enum PageHangState
{
    Unresponsive,
    Recovered,
    Reloaded,
    ClosedAnyway,
}

/// <summary>A change of a window's page that stopped answering, as <c>spar.OnPageUnresponsive</c> receives it.</summary>
public sealed record PageHang(string Window, PageHangState State, TimeSpan Duration);
