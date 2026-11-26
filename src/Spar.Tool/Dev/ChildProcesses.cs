namespace Spar.Tool.Dev;

/// <summary>
/// What <c>spar dev</c> starts (a build, the app, the private Xvfb): the program, its arguments and folder, and the
/// variables it changes in the tool's own environment (a null value removes one).
/// </summary>
/// <param name="Window">Whether it opens a window (the app): on Windows its first show is then <paramref name="Activate"/>'s.</param>
/// <param name="Activate">False on a restart: the window is shown without taking the focus (<c>SW_SHOWNOACTIVATE</c>).</param>
internal sealed record ChildStart(
    string Executable,
    IReadOnlyList<string> Arguments,
    string WorkingDirectory,
    IReadOnlyDictionary<string, string?> Environment,
    bool Window = false,
    bool Activate = true);

/// <summary>One started process: its id, its exit, and a kill of that process alone.</summary>
internal interface IChild : IDisposable
{
    int ProcessId { get; }

    /// <summary>Its exit code once it exits.</summary>
    Task<int> Exited { get; }

    /// <summary>Ends this process alone, at once: what it started (the browser's processes end with their host; the
    /// development server is kept, decision "M2's decide-first questions", item 5) stays in the session's job or group.</summary>
    void Kill();
}

/// <summary>
/// Everything <c>spar dev</c> starts, from creation (research review, "Child processes"): on Windows one job object
/// with <c>JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE</c> that each process joins as it is created, so the whole tree ends with
/// the tool however the tool ends; on Linux a process group per child (<c>setsid</c>), each ended at the tool's end, and
/// the child itself when the tool dies (<c>PR_SET_PDEATHSIG</c>). Every child gets a closed stdin and never the
/// terminal's Ctrl+C (its own process group), and its output comes line by line to the caller.
/// </summary>
internal interface IChildProcesses : IDisposable
{
    /// <summary>Starts <paramref name="start"/>; each line of its output or error goes to <paramref name="line"/>.</summary>
    IChild Start(ChildStart start, Action<string, bool> line);

    /// <summary>Ends every process still running that this session started.</summary>
    void EndAll();

    public static IChildProcesses Create(string? desktop)
    {
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763))
        {
            return new JobProcesses(desktop);
        }

        if (OperatingSystem.IsLinux())
        {
            return new GroupProcesses();
        }

        throw new PlatformNotSupportedException("spar dev runs on Windows 10 1809 or later and on Linux.");
    }
}
