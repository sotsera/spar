using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Spar.Tool.Dev;

/// <summary>
/// <see cref="IChildProcesses"/> on Linux: each child starts a session and process group of its own (<c>setsid</c>,
/// .NET's <see cref="ProcessStartInfo.StartDetached"/>), so the terminal's signals reach the tool alone and what the
/// child starts stays in its group; <see cref="EndAll"/> sends <c>SIGKILL</c> to every group, and the child itself ends
/// if the tool dies first (<see cref="ProcessStartInfo.KillOnParentExit"/>). Its input is <c>/dev/null</c>.
/// </summary>
[SupportedOSPlatform("linux")]
internal sealed partial class GroupProcesses : IChildProcesses
{
    private const int SigKill = 9;

    private readonly Lock _gate = new();
    private readonly List<GroupChild> _children = [];

    public IChild Start(ChildStart start, Action<string, bool> line)
    {
        var info = new ProcessStartInfo(start.Executable, start.Arguments)
        {
            WorkingDirectory = start.WorkingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StartDetached = true,
            KillOnParentExit = true,
        };
        foreach (var (name, value) in start.Environment)
        {
            if (value is null)
            {
                info.Environment.Remove(name);
            }
            else
            {
                info.Environment[name] = value;
            }
        }

        using var input = File.OpenHandle("/dev/null", FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        info.StandardInputHandle = input;
        var process = Process.Start(info) ?? throw new InvalidOperationException($"{Path.GetFileName(start.Executable)} could not be started.");
        var child = new GroupChild(process, line);
        lock (_gate)
        {
            _children.Add(child);
        }

        return child;
    }

    public void EndAll()
    {
        lock (_gate)
        {
            foreach (var child in _children)
            {
                // The group's id is the child's (setsid made it the leader); a group already gone is ESRCH.
                _ = Kill(-child.ProcessId, SigKill);
            }
        }
    }

    public void Dispose()
    {
        EndAll();
        lock (_gate)
        {
            foreach (var child in _children)
            {
                child.Dispose();
            }

            _children.Clear();
        }
    }

    [LibraryImport("libc", EntryPoint = "kill", SetLastError = true)]
    private static partial int Kill(int processId, int signal);

    private sealed class GroupChild : IChild
    {
        private readonly Process _process;
        private readonly TaskCompletionSource<int> _exited = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public GroupChild(Process process, Action<string, bool> line)
        {
            _process = process;
            ProcessId = process.Id;
            OutputLines.Read(process.StandardOutput.BaseStream, text => line(text, false));
            OutputLines.Read(process.StandardError.BaseStream, text => line(text, true));
            _ = WatchAsync();
        }

        public int ProcessId { get; }

        public Task<int> Exited => _exited.Task;

        public void Kill() => _ = GroupProcesses.Kill(ProcessId, SigKill);

        public void Dispose() => _process.Dispose();

        private async Task WatchAsync()
        {
            await _process.WaitForExitAsync(CancellationToken.None);
            _exited.TrySetResult(_process.ExitCode);
        }
    }
}
