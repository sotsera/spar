// MSBuildTask0001: APIs that are never safe in a task. Reported for every task, multithreadable or not.
// Each probe line ends with "expect:" and the IDs the default configuration should report there.
using System.Diagnostics;
using System.Globalization;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Task = Microsoft.Build.Utilities.Task;

namespace Probe.Rules;

[MSBuildMultiThreadableTask]
public sealed class R0001_NeverSafe : Task, IMultiThreadableTask // expect: MSBuildTask0011
{
    public TaskEnvironment TaskEnvironment { get; set; } = null!;

    public override bool Execute()
    {
        Console.WriteLine("hello"); // expect: MSBuildTask0001
        var width = Console.BufferWidth; // expect: MSBuildTask0001
        Console.Out.Flush(); // expect: MSBuildTask0001
        _ = Console.ReadLine(); // expect: MSBuildTask0001
        ThreadPool.SetMinThreads(4, 4); // expect: MSBuildTask0001
        ThreadPool.SetMaxThreads(8, 8); // expect: MSBuildTask0001
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture; // expect: MSBuildTask0001
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture; // expect: MSBuildTask0001
        Directory.SetCurrentDirectory(TaskEnvironment.ProjectDirectory); // expect: MSBuildTask0001
        using (var self = Process.GetCurrentProcess())
        {
            self.Kill(); // expect: MSBuildTask0001
        }

        if (width < 0)
        {
            Environment.Exit(1); // expect: MSBuildTask0001
            Environment.FailFast("boom"); // expect: MSBuildTask0001
        }

        return true;
    }
}

/// <summary>A task that has not opted into multithreading still gets MSBuildTask0001.</summary>
public sealed class R0001_RegularTask : Task
{
    public override bool Execute()
    {
        Console.Error.WriteLine("regular"); // expect: MSBuildTask0001
        return true;
    }
}

/// <summary>A class that is not a task is not analyzed at all.</summary>
public static class R0001_NotATask
{
    public static void Write() => Console.WriteLine("not a task"); // expect: none
}
