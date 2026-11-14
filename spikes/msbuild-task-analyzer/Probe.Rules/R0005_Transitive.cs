// MSBuildTask0005: a multithreadable task reaches an unsafe API through a helper. Reported at the unsafe call site.
using Microsoft.Build.Framework;
using Task = Microsoft.Build.Utilities.Task;

namespace Probe.Rules;

[MSBuildMultiThreadableTask]
public sealed class R0005_Transitive : Task, IMultiThreadableTask // expect: MSBuildTask0011
{
    public TaskEnvironment TaskEnvironment { get; set; } = null!;

    public override bool Execute()
    {
        Log.LogMessage(MessageImportance.Low, TransitiveHelper.Describe());
        return true;
    }
}

internal static class TransitiveHelper
{
    public static string Describe() => Inner() + (File.Exists("relative.txt") ? "1" : "0"); // expect: MSBuildTask0005

    private static string Inner()
    {
        Console.WriteLine("from helper"); // expect: MSBuildTask0005
        return Environment.GetEnvironmentVariable("PATH") ?? ""; // expect: MSBuildTask0005
    }

    // Not reached from any task: not reported.
    public static string Unreached() => Environment.CurrentDirectory; // expect: none
}

/// <summary>Helper opted into direct analysis with an attribute the project declares itself (18.12.2 does not ship it).</summary>
[Microsoft.Build.Framework.MSBuildMultiThreadableTaskAnalyzed]
internal static class AnalyzedHelper
{
    public static string Cwd() => Directory.GetCurrentDirectory(); // expect: MSBuildTask0002
}
