// Added by Spar.Sdk to xUnit test projects in the NativeAOT lane (decision "Fixture rules: the teardown hook").
using Spar.Testing;
using Xunit.v3;

[assembly: SparTestHooks.SparTeardownHook]

namespace SparTestHooks;

/// <summary>
/// The NativeAOT lane's copy of the teardown hook: xUnit's source-generated flavour gives the hook the test itself
/// instead of the method. It raises, after each test, the failures to stop an app that <c>SparTestApp</c> kept.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly)]
internal sealed class SparTeardownHookAttribute : BeforeAfterTestAttribute
{
    public override void Before(ICodeGenTest test) =>
        SparTestCleanup.Begin(test.UniqueID, static () => Xunit.TestContext.Current.Test?.UniqueID);

    public override void After(ICodeGenTest test)
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
