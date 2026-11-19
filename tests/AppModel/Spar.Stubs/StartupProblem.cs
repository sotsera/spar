namespace Spar;

/// <summary>What kind of problem stopped the app from starting; each has a fixed exit code.</summary>
public enum StartupProblemKind
{
    Missing,
    TooOld,
    Broken,
    NoGraphicalSession,
    OsTooOld,
    NotResponding,
    OtherUser,
}

/// <summary>Why the app cannot start, as <c>spar.OnStartupProblem</c> receives it, before the app's services exist.</summary>
public sealed record StartupProblem(
    StartupProblemKind Kind,
    string Component,
    string? Found,
    string? Required,
    string Details,
    string? HelpUrl);
