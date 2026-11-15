using System.Collections.Concurrent;
using System.ComponentModel;

namespace Spar.Testing;

/// <summary>
/// Lets a failure to stop an app reach the test framework beside the test's own failure (decision "Fixture rules: the
/// teardown hook"). <c>await using</c> runs the disposal in a <c>finally</c>, where a thrown exception would replace
/// the assertion that failed first. So when the test project has the hook <c>Spar.Sdk</c> adds to xUnit test
/// projects, <see cref="SparTestApp.DisposeAsync"/> keeps the failure here and the hook raises it after the test body,
/// where xUnit reports both. Without the hook, the disposal throws as before: a failure to stop is never a pass.
/// This library stays free of any test framework; the hook is compiled into the test project.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class SparTestCleanup
{
    /// <summary>The failures of each test the hook is watching, by the test's id.</summary>
    private static readonly ConcurrentDictionary<string, ConcurrentQueue<Exception>> Watched = new(StringComparer.Ordinal);

    /// <summary>How the hook names the test running on the current flow; set once, by the first test the hook sees.</summary>
    private static Func<string?>? s_currentTest;

    /// <summary>The hook, before a test: start keeping that test's stop failures.</summary>
    public static void Begin(string testId, Func<string?> currentTest)
    {
        Interlocked.CompareExchange(ref s_currentTest, currentTest, null);
        Watched[testId] = new ConcurrentQueue<Exception>();
    }

    /// <summary>The hook, after a test: the stop failures it kept, which the hook throws; the test is forgotten.</summary>
    public static IReadOnlyList<Exception> End(string testId) =>
        Watched.TryRemove(testId, out var failures) ? [.. failures] : [];

    /// <summary>Keeps <paramref name="failure"/> for the current test when the hook watches it; false when nothing does.</summary>
    internal static bool TryKeep(Exception failure)
    {
        var testId = Volatile.Read(ref s_currentTest)?.Invoke();
        if (testId is null || !Watched.TryGetValue(testId, out var failures))
        {
            return false;
        }

        failures.Enqueue(failure);
        return true;
    }
}
