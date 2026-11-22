// Added by Spar.Sdk to xUnit test projects in the NativeAOT lane (decision "Fixture rules: the teardown hook").
using Spar.Testing;
using Xunit.v3;

[assembly: SparTestHooks.SparTeardownHook]

namespace SparTestHooks;

/// <summary>
/// The NativeAOT lane's copy of the teardown hook: xUnit's source-generated flavour gives the hook the test itself
/// instead of the method. It raises, after each test, the failures to stop an app that <c>SparTestApp</c> kept, and tells
/// the test host the test's Surface trait.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly)]
internal sealed class SparTeardownHookAttribute : BeforeAfterTestAttribute
{
    public override void Before(ICodeGenTest test) =>
        SparTestCleanup.Begin(test.UniqueID, static () => Xunit.TestContext.Current.Test?.UniqueID, Surfaces(test.Traits));

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
