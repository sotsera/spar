using System.Collections.Concurrent;
using System.ComponentModel;

namespace Spar.Testing;

/// <summary>
/// The test framework's side of the test host, through the hook <c>Spar.Sdk</c> compiles into xUnit test projects (one
/// file per xUnit flavour; this library stays free of any test framework).
/// <para>
/// It lets a failure to stop an app reach the test framework beside the test's own failure (decision "Fixture rules:
/// the teardown hook"). <c>await using</c> runs the disposal in a <c>finally</c>, where a thrown exception would replace
/// the assertion that failed first. So when the hook watches the test, <see cref="SparTestApp.DisposeAsync"/> keeps the
/// failure here and the hook raises it after the test body, where xUnit reports both. Without the hook, the disposal
/// throws as before: a failure to stop is never a pass.
/// </para>
/// <para>
/// It also holds the values of the test's <c>Surface</c> trait, merged by xUnit from the method, its class and the
/// assembly, so <see cref="SparTestApp.StartAsync"/> can refuse a Window or Browser test that lacks its trait (decision
/// "M1d step 4's shape: tests in the template"): the trait is what keeps such a test out of the fast invocation.
/// </para>
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class SparTestCleanup
{
    /// <summary>The name of the trait that says which surface a test drives.</summary>
    internal const string SurfaceTrait = "Surface";

    /// <summary>Each test the hook is watching, by the test's id.</summary>
    private static readonly ConcurrentDictionary<string, WatchedTest> Watched = new(StringComparer.Ordinal);

    /// <summary>How the hook names the test running on the current flow; set once, by the first test the hook sees.</summary>
    private static Func<string?>? s_currentTest;

    /// <summary>
    /// The hook, before a test: start keeping that test's stop failures, and remember the values of its
    /// <c>Surface</c> trait (<paramref name="surfaces"/>, empty when it has none).
    /// </summary>
    public static void Begin(string testId, Func<string?> currentTest, IReadOnlyCollection<string> surfaces)
    {
        Interlocked.CompareExchange(ref s_currentTest, currentTest, null);
        Watched[testId] = new WatchedTest(surfaces);
    }

    /// <summary>The hook, after a test: the stop failures it kept, which the hook throws; the test is forgotten.</summary>
    public static IReadOnlyList<Exception> End(string testId) =>
        Watched.TryRemove(testId, out var watched) ? [.. watched.Failures] : [];

    /// <summary>The id of the test running on the current flow, when the hook names it; null without the hook.</summary>
    internal static string? CurrentTest() => Volatile.Read(ref s_currentTest)?.Invoke();

    /// <summary>
    /// The values of the current test's <c>Surface</c> trait, or null when no hook watches the current flow (no hook in
    /// the test project, or an app started outside a test, such as in a fixture).
    /// </summary>
    internal static IReadOnlyCollection<string>? CurrentSurfaces() =>
        CurrentTest() is { } testId && Watched.TryGetValue(testId, out var watched) ? watched.Surfaces : null;

    /// <summary>Keeps <paramref name="failure"/> for the current test when the hook watches it; false when nothing does.</summary>
    internal static bool TryKeep(Exception failure)
    {
        var testId = CurrentTest();
        if (testId is null || !Watched.TryGetValue(testId, out var watched))
        {
            return false;
        }

        watched.Failures.Enqueue(failure);
        return true;
    }

    /// <summary>What the hook knows of one running test.</summary>
    private sealed class WatchedTest(IReadOnlyCollection<string> surfaces)
    {
        public ConcurrentQueue<Exception> Failures { get; } = new();

        public IReadOnlyCollection<string> Surfaces { get; } = surfaces;
    }
}
