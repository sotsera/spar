namespace Spar;

/// <summary>
/// Implemented by the app's <c>Program</c>, in the Test variant only, by Spar's generator. The test host calls
/// <see cref="Run"/> to run the app's own top-level statements inside the test process.
/// </summary>
public interface ISparEntryPoint
{
    static abstract int Run(string[] args);
}
