using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Spar.Linux.Interop;

namespace Spar.Linux;

/// <summary>
/// A short-lived child process of the start-up (the trial <c>bwrap</c> run, a fallback message program), started with
/// <c>posix_spawnp</c> and the process's current environment, its input from <c>/dev/null</c>. .NET's
/// <c>Process</c> is not used: nothing else in the image needs it. Runs on the process's main thread before any window
/// exists, so its waits block nothing that draws.
/// </summary>
internal static class ChildProcess
{
    /// <summary>How much of the child's output is kept, for a message.</summary>
    private const int OutputLimitBytes = 4096;

    private const int ReadOnly = 0;
    private const int WriteOnly = 1;

    /// <summary>
    /// Runs <paramref name="file"/> (found on <c>PATH</c> unless it is a path) with <paramref name="arguments"/>. With
    /// <paramref name="captureOutput"/>, its standard output and error are kept; otherwise they go to <c>/dev/null</c>.
    /// With a <paramref name="timeoutMilliseconds"/>, a child still running then is killed.
    /// </summary>
    public static unsafe ChildResult Run(string file, IReadOnlyList<string> arguments, bool captureOutput, int? timeoutMilliseconds)
    {
        var strings = new nint[arguments.Count + 2];
        var actions = stackalloc byte[Libc.FileActionsBytes];
        var pipe = stackalloc int[2] { -1, -1 };
        var clock = Stopwatch.StartNew();
        try
        {
            strings[0] = Marshal.StringToCoTaskMemUTF8(file);
            for (var index = 0; index < arguments.Count; index++)
            {
                strings[index + 1] = Marshal.StringToCoTaskMemUTF8(arguments[index]);
            }

            Libc.FileActionsInit(actions);
            Libc.FileActionsAddOpen(actions, 0, "/dev/null", ReadOnly, 0);
            if (captureOutput)
            {
                if (Libc.Pipe2(pipe, Libc.OCloexec) != 0)
                {
                    return ChildResult.NotStarted($"pipe2 failed (errno {Marshal.GetLastPInvokeError()})");
                }

                // dup2 clears close-on-exec on the copies the child keeps.
                Libc.FileActionsAddDup2(actions, pipe[1], 1);
                Libc.FileActionsAddDup2(actions, pipe[1], 2);
            }
            else
            {
                Libc.FileActionsAddOpen(actions, 1, "/dev/null", WriteOnly, 0);
                Libc.FileActionsAddOpen(actions, 2, "/dev/null", WriteOnly, 0);
            }

            int processId;
            int spawned;
            fixed (nint* argv = strings)
            {
                spawned = Libc.SpawnP(&processId, file, actions, null, argv, Libc.Environment());
            }

            if (spawned != 0)
            {
                return ChildResult.NotStarted($"{file} could not be started (error {spawned})");
            }

            var output = "";
            if (captureOutput)
            {
                Libc.Close(pipe[1]);
                pipe[1] = -1;
                output = ReadAll(pipe[0], clock, timeoutMilliseconds);
            }

            var exitCode = Wait(processId, clock, timeoutMilliseconds);
            return exitCode is { } code
                ? new ChildResult(true, code, output, false, clock.Elapsed.TotalMilliseconds)
                : new ChildResult(true, -1, output, true, clock.Elapsed.TotalMilliseconds);
        }
        finally
        {
            Libc.FileActionsDestroy(actions);
            foreach (var text in strings)
            {
                Marshal.FreeCoTaskMem(text);
            }

            for (var index = 0; index < 2; index++)
            {
                if (pipe[index] >= 0)
                {
                    Libc.Close(pipe[index]);
                }
            }
        }
    }

    /// <summary>The child's output until it closes its end or the time is up; at most <see cref="OutputLimitBytes"/> kept.</summary>
    private static unsafe string ReadAll(int descriptor, Stopwatch clock, int? timeoutMilliseconds)
    {
        var kept = new byte[OutputLimitBytes];
        var length = 0;
        var buffer = stackalloc byte[1024];
        while (true)
        {
            var left = timeoutMilliseconds is { } limit ? Math.Max(0, limit - (int)clock.ElapsedMilliseconds) : -1;
            var poll = new Libc.PollDescriptor { Descriptor = descriptor, Events = Libc.PollIn };
            var ready = Libc.Poll(&poll, 1, left);
            if (ready < 0 && Marshal.GetLastPInvokeError() == Libc.EIntr)
            {
                continue;
            }

            if (ready <= 0)
            {
                break;
            }

            var read = Libc.Read(descriptor, buffer, 1024);
            if (read < 0 && Marshal.GetLastPInvokeError() == Libc.EIntr)
            {
                continue;
            }

            if (read <= 0)
            {
                break;
            }

            var take = Math.Min((int)read, kept.Length - length);
            new ReadOnlySpan<byte>(buffer, take).CopyTo(kept.AsSpan(length));
            length += take;
        }

        return Encoding.UTF8.GetString(kept, 0, length).Trim();
    }

    /// <summary>The exit code (128 plus the signal for a child a signal ended), or null when it was killed at the limit.</summary>
    private static unsafe int? Wait(int processId, Stopwatch clock, int? timeoutMilliseconds)
    {
        int status;
        while (true)
        {
            var options = timeoutMilliseconds is null ? 0 : Libc.WNoHang;
            var waited = Libc.WaitPid(processId, &status, options);
            if (waited == processId)
            {
                return ExitCodeOf(status);
            }

            if (waited < 0 && Marshal.GetLastPInvokeError() != Libc.EIntr)
            {
                // Reaped by someone else, which nothing in the host does: report it as a failure.
                return -1;
            }

            if (timeoutMilliseconds is { } limit && clock.ElapsedMilliseconds >= limit)
            {
                Libc.Kill(processId, Libc.SigKill);
                Libc.WaitPid(processId, &status, 0);
                return null;
            }

            if (waited == 0)
            {
                Thread.Sleep(1);
            }
        }
    }

    private static int ExitCodeOf(int status)
    {
        var signal = status & 0x7F;
        return signal == 0 ? (status >> 8) & 0xFF : 128 + signal;
    }
}

/// <summary>What a child process did: whether it started, its exit code, its output when kept, and how long it took.</summary>
internal readonly record struct ChildResult(bool Started, int ExitCode, string Output, bool TimedOut, double Milliseconds)
{
    public static ChildResult NotStarted(string reason) => new(false, -1, reason, false, 0);
}
