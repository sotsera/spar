namespace Spar.Windows;

/// <summary>
/// Where a view gets its browser when it is not an app's own (plan section 5, the <c>Window</c> surface): the test
/// process's shared environment, an InPrivate profile of the window's own, and the test host's hooks. An app's view has
/// none, and every member is read only behind the test-host feature switch.
/// </summary>
internal sealed class BrowserSource
{
    /// <summary>The shared environment for the view's browser arguments: a pointer with one reference the view owns.</summary>
    public Func<string?, Task<nint>>? SharedEnvironment { get; init; }

    /// <summary>The InPrivate profile's name, unique among the windows open at the same time.</summary>
    public string? PrivateProfile { get; init; }

    /// <summary>Runs before the first navigation, after the host's own calls (the page driver's set-up).</summary>
    public Func<Task>? BeforeFirstNavigation { get; init; }

    /// <summary>Each phase as it is reached (UI thread).</summary>
    public Action<string>? OnPhase { get; init; }

    public bool IsShared => SharedEnvironment is not null;
}
