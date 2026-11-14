using System.Diagnostics;
using System.Globalization;
using Microsoft.Build.Utilities;

namespace Probe.Tasks;

/// <summary>Writes one machine-readable line per task execution, so the build script can see where each task ran.</summary>
internal static class ProbeReport
{
    private static int s_executions;

    public static void Log(TaskLoggingHelper log, string kind, string label, string projectDirectory, string environmentValue, DateTime start)
    {
        var executions = Interlocked.Increment(ref s_executions);
        using var process = Process.GetCurrentProcess();
        var end = DateTime.UtcNow;
        var line = string.Join('|',
            "PROBE",
            kind,
            label,
            Environment.ProcessId.ToString(CultureInfo.InvariantCulture),
            Environment.CurrentManagedThreadId.ToString(CultureInfo.InvariantCulture),
            process.ProcessName,
            NodeMode(),
            executions.ToString(CultureInfo.InvariantCulture),
            start.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture),
            end.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture),
            projectDirectory,
            environmentValue);
        log.LogMessage(Microsoft.Build.Framework.MessageImportance.High, line);
    }

    /// <summary>MSBuild worker processes carry a "nodemode" switch: 1 is a worker node, 2 a task host. None is the entry process.</summary>
    private static string NodeMode()
    {
        foreach (var arg in Environment.GetCommandLineArgs())
        {
            var index = arg.IndexOf("nodemode:", StringComparison.OrdinalIgnoreCase);
            if (index >= 0)
            {
                return "nodemode:" + arg[(index + "nodemode:".Length)..];
            }
        }

        return "entry";
    }
}
