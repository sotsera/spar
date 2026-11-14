// MSBuildTask0004: APIs that need review in a shared process (assembly loading). Multithreadable code only, by default.
using System.Reflection;
using Microsoft.Build.Framework;
using Task = Microsoft.Build.Utilities.Task;

namespace Probe.Rules;

[MSBuildMultiThreadableTask]
public sealed class R0004_Review : Task, IMultiThreadableTask // expect: MSBuildTask0011
{
    public TaskEnvironment TaskEnvironment { get; set; } = null!;

    public override bool Execute()
    {
        var path = TaskEnvironment.GetAbsolutePath("x.dll");
        var a1 = Assembly.Load("System.Runtime"); // expect: MSBuildTask0004
        var a2 = Assembly.LoadFrom(path); // expect: MSBuildTask0004
        var a3 = Assembly.LoadFile(path); // expect: MSBuildTask0004
        var a4 = Assembly.Load([]); // expect: MSBuildTask0004
#pragma warning disable CS0618 // LoadWithPartialName is obsolete; the probe calls it on purpose.
        var a5 = Assembly.LoadWithPartialName("System.Runtime"); // expect: MSBuildTask0004
#pragma warning restore CS0618
        var h1 = Activator.CreateInstance("System.Runtime", "System.Object"); // expect: MSBuildTask0004
        var h2 = Activator.CreateInstanceFrom(path, "Some.Type"); // expect: MSBuildTask0004
        var d1 = AppDomain.CurrentDomain.Load("System.Runtime"); // expect: MSBuildTask0004
        var d2 = AppDomain.CurrentDomain.CreateInstance("System.Runtime", "System.Object"); // expect: MSBuildTask0004
        var d3 = AppDomain.CurrentDomain.CreateInstanceFrom(path, "Some.Type"); // expect: MSBuildTask0004

        // Process-global hooks that the documentation does not list.
        AppDomain.CurrentDomain.AssemblyResolve += (_, _) => null; // expect: ?
        var ctx = System.Runtime.Loader.AssemblyLoadContext.Default.LoadFromAssemblyPath(path); // expect: ?

        Log.LogMessage(MessageImportance.Low, $"{a1}{a2}{a3}{a4}{a5}{h1}{h2}{d1}{d2}{d3}{ctx}");
        return true;
    }
}
