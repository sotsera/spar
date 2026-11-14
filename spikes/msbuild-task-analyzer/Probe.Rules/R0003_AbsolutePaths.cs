// MSBuildTask0003: file system APIs given a path that is not proven absolute. Multithreadable code only, by default.
using Microsoft.Build.Framework;
using Task = Microsoft.Build.Utilities.Task;

namespace Probe.Rules;

[MSBuildMultiThreadableTask]
public sealed class R0003_AbsolutePaths : Task, IMultiThreadableTask // expect: MSBuildTask0011
{
    public TaskEnvironment TaskEnvironment { get; set; } = null!;

    public ITaskItem[] Items { get; set; } = [];

    public override bool Execute()
    {
        const string relative = "obj/spike.txt";
        var exists = File.Exists(relative); // expect: MSBuildTask0003
        File.WriteAllText(relative, "contents"); // expect: MSBuildTask0003
        File.Copy(relative, "obj/copy.txt"); // expect: MSBuildTask0003 (one per call, not per argument)
        Directory.CreateDirectory("obj/dir"); // expect: MSBuildTask0003
        var files = Directory.GetFiles("obj", "*.txt"); // expect: MSBuildTask0003
        var fileInfo = new FileInfo(relative); // expect: MSBuildTask0003
        var dirInfo = new DirectoryInfo("obj"); // expect: MSBuildTask0003
        using (var stream = new FileStream(relative, FileMode.Open)) // expect: MSBuildTask0003
        using (var reader = new StreamReader(relative)) // expect: MSBuildTask0003
        using (var writer = new StreamWriter(relative)) // expect: MSBuildTask0003
        using (var watcher = new FileSystemWatcher("obj")) // expect: MSBuildTask0003
        {
            Log.LogMessage(MessageImportance.Low, $"{stream.Length}{reader.Peek()}{writer.Encoding}{watcher.Path}");
        }

        // Safe patterns from the analyzer's documentation.
        var safe1 = File.Exists(TaskEnvironment.GetAbsolutePath(relative)); // expect: none
        AbsolutePath absolute = TaskEnvironment.GetAbsolutePath(relative);
        var safe2 = File.Exists(absolute); // expect: none
        var safe3 = File.Exists(new FileInfo(absolute).FullName); // expect: none
        var safe5 = Exists(absolute); // expect: none
        foreach (var item in Items)
        {
            var safe4 = File.Exists(item.GetMetadata("FullPath")); // expect: MSBuildTask0007
            var itemSpec = File.Exists(item.ItemSpec); // expect: MSBuildTask0003 MSBuildTask0007
            Log.LogMessage(MessageImportance.Low, $"{safe4}{itemSpec}");
        }

        // Common shapes the documentation does not list.
        var combined = File.Exists(Path.Combine(TaskEnvironment.ProjectDirectory, "obj", "x.txt")); // expect: ?
        var combinedRelative = File.Exists(Path.Combine("obj", "x.txt")); // expect: ?
        var valueOnly = File.Exists(absolute.Value); // expect: MSBuildTask0003
        string viaLocal = absolute;
        var viaString = File.Exists(viaLocal); // expect: none

        Log.LogMessage(MessageImportance.Low, $"{exists}{files.Length}{fileInfo}{dirInfo}{safe1}{safe2}{safe3}{safe5}{combined}{combinedRelative}{valueOnly}{viaString}");
        return true;
    }

    private static bool Exists(AbsolutePath path) => File.Exists(path); // expect: none
}
