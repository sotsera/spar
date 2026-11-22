// Added by Spar.Sdk to xUnit test projects (decision "Fixture rules: the teardown hook"); not part of the app.
using System.Reflection;
using Spar.Testing;
using Xunit.v3;

[assembly: SparTestHooks.SparTeardownHook]

namespace SparTestHooks;

/// <summary>
/// Raises, after each test, the failures to stop an app that <c>SparTestApp</c> kept during it, so xUnit reports them
/// beside the test's own failure instead of losing one of the two, and a test whose app did not stop cleanly never
/// passes; and tells the test host the test's Surface trait, so a Window or Browser test without it fails at its start.
/// Compiled into the test project against its xUnit; the NativeAOT lane has its own copy.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly)]
internal sealed class SparTeardownHookAttribute : BeforeAfterTestAttribute
{
    public override void Before(MethodInfo methodUnderTest, IXunitTest test) =>
        SparTestCleanup.Begin(test.UniqueID, static () => Xunit.TestContext.Current.Test?.UniqueID, Surfaces(test.Traits));

    public override void After(MethodInfo methodUnderTest, IXunitTest test)
    {
        var failures = SparTestCleanup.End(test.UniqueID);
        if (failures.Count == 1)
        {
            throw new InvalidOperationException("The app did not stop cleanly after the test.", failures[0]);
        }

        if (failures.Count > 1)
        {
            throw new AggregateException("The apps did not stop cleanly after the test.", failures);
        }
    }

    /// <summary>
    /// The values of the test's <c>Surface</c> trait, which xUnit merges from the method, its class and the assembly; the
    /// test host refuses to start the Window or Browser surface for a test without the matching one.
    /// </summary>
    private static IReadOnlyCollection<string> Surfaces(IReadOnlyDictionary<string, IReadOnlyCollection<string>> traits)
    {
        foreach (var (name, values) in traits)
        {
            if (string.Equals(name, "Surface", StringComparison.OrdinalIgnoreCase))
            {
                return values;
            }
        }

        return [];
    }
}
