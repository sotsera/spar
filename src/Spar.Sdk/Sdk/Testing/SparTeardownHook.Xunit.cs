// Added by Spar.Sdk to xUnit test projects (decision "Fixture rules: the teardown hook"); not part of the app.
using System.Reflection;
using Spar.Testing;
using Xunit.v3;

[assembly: SparTestHooks.SparTeardownHook]

namespace SparTestHooks;

/// <summary>
/// Raises, after each test, the failures to stop an app that <c>SparTestApp</c> kept during it, so xUnit reports them
/// beside the test's own failure instead of losing one of the two, and a test whose app did not stop cleanly never
/// passes. Compiled into the test project against its xUnit; the NativeAOT lane has its own copy.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly)]
internal sealed class SparTeardownHookAttribute : BeforeAfterTestAttribute
{
    public override void Before(MethodInfo methodUnderTest, IXunitTest test) =>
        SparTestCleanup.Begin(test.UniqueID, static () => Xunit.TestContext.Current.Test?.UniqueID);

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
}
