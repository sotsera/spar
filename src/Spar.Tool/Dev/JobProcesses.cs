using System.Collections;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Microsoft.Win32.SafeHandles;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Security;
using Windows.Win32.System.JobObjects;
using Windows.Win32.System.Threading;

namespace Spar.Tool.Dev;

/// <summary>
/// <see cref="IChildProcesses"/> on Windows: one job object for the session, which each process joins at creation
/// (<c>PROC_THREAD_ATTRIBUTE_JOB_LIST</c>) with everything it starts, and which Windows ends when its last handle, the
/// tool's, closes (<c>JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE</c>). Each process inherits three handles only
/// (<c>PROC_THREAD_ATTRIBUTE_HANDLE_LIST</c>): <c>NUL</c> as its input and the two pipes of its output. It is created in
/// a process group of its own, so the terminal's Ctrl+C reaches the tool alone, and on the session's private desktop
/// when there is one.
/// </summary>
[SupportedOSPlatform("windows10.0.17763")]
internal sealed class JobProcesses : IChildProcesses
{
    private const ushort ShowNoActivate = 4;

    private readonly SafeFileHandle _job;
    private readonly string? _desktop;
    private readonly Lock _gate = new();
    private readonly List<JobChild> _children = [];

    public JobProcesses(string? desktop)
    {
        _desktop = desktop;
        _job = CreateJob();
    }

    public IChild Start(ChildStart start, Action<string, bool> line)
    {
        CreatePipe(out var outputRead, out var outputWrite);
        CreatePipe(out var errorRead, out var errorWrite);
        using var input = OpenNull();
        try
        {
            var info = Create(start, input, outputWrite, errorWrite);
            var child = new JobChild(info, outputRead, errorRead, line);
            lock (_gate)
            {
                _children.Add(child);
            }

            return child;
        }
        catch
        {
            outputRead.Dispose();
            errorRead.Dispose();
            throw;
        }
        finally
        {
            // The child has its own copies; the pipes end when it, and whatever it handed them to, exits.
            outputWrite.Dispose();
            errorWrite.Dispose();
        }
    }

    public void EndAll() => PInvoke.TerminateJobObject((HANDLE)_job.DangerousGetHandle(), 1);

    public void Dispose()
    {
        EndAll();
        _job.Dispose();
        lock (_gate)
        {
            foreach (var child in _children)
            {
                child.Dispose();
            }

            _children.Clear();
        }
    }

    private static unsafe SafeFileHandle CreateJob()
    {
        var handle = PInvoke.CreateJobObject((SECURITY_ATTRIBUTES*)null, default(PCWSTR));
        if (handle.IsNull)
        {
            throw new InvalidOperationException($"CreateJobObject failed ({Marshal.GetLastPInvokeError()}).");
        }

        var job = new SafeFileHandle(handle, ownsHandle: true);
        var limits = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
        limits.BasicLimitInformation.LimitFlags = JOB_OBJECT_LIMIT.JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;
        if (!PInvoke.SetInformationJobObject(handle, JOBOBJECTINFOCLASS.JobObjectExtendedLimitInformation, &limits, (uint)sizeof(JOBOBJECT_EXTENDED_LIMIT_INFORMATION)))
        {
            var error = Marshal.GetLastPInvokeError();
            job.Dispose();
            throw new InvalidOperationException($"SetInformationJobObject failed ({error}).");
        }

        return job;
    }

    /// <summary>A pipe whose write end may be inherited and whose read end never is.</summary>
    private static unsafe void CreatePipe(out SafeFileHandle read, out SafeFileHandle write)
    {
        var attributes = new SECURITY_ATTRIBUTES { nLength = (uint)sizeof(SECURITY_ATTRIBUTES), bInheritHandle = true };
        HANDLE readEnd;
        HANDLE writeEnd;
        if (!PInvoke.CreatePipe(&readEnd, &writeEnd, &attributes, 0))
        {
            throw new InvalidOperationException($"CreatePipe failed ({Marshal.GetLastPInvokeError()}).");
        }

        read = new SafeFileHandle(readEnd, ownsHandle: true);
        write = new SafeFileHandle(writeEnd, ownsHandle: true);
        PInvoke.SetHandleInformation(readEnd, (uint)HANDLE_FLAGS.HANDLE_FLAG_INHERIT, 0);
    }

    /// <summary>The closed input: <c>NUL</c>, inheritable, so a child reading its stdin gets its end at once.</summary>
    private static SafeFileHandle OpenNull()
    {
        var handle = File.OpenHandle("NUL", FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        PInvoke.SetHandleInformation((HANDLE)handle.DangerousGetHandle(), (uint)HANDLE_FLAGS.HANDLE_FLAG_INHERIT, (HANDLE_FLAGS)(uint)HANDLE_FLAGS.HANDLE_FLAG_INHERIT);
        return handle;
    }

    private unsafe PROCESS_INFORMATION Create(ChildStart start, SafeFileHandle input, SafeFileHandle output, SafeFileHandle error)
    {
        nuint size = 0;
        PInvoke.InitializeProcThreadAttributeList(default, 2, 0, &size);
        var list = (LPPROC_THREAD_ATTRIBUTE_LIST)NativeMemory.Alloc(size);
        try
        {
            if (!PInvoke.InitializeProcThreadAttributeList(list, 2, 0, &size))
            {
                throw new InvalidOperationException($"InitializeProcThreadAttributeList failed ({Marshal.GetLastPInvokeError()}).");
            }

            try
            {
                var job = (HANDLE)_job.DangerousGetHandle();
                var inherited = stackalloc HANDLE[3]
                {
                    (HANDLE)input.DangerousGetHandle(), (HANDLE)output.DangerousGetHandle(), (HANDLE)error.DangerousGetHandle(),
                };
                Update(list, PInvoke.PROC_THREAD_ATTRIBUTE_JOB_LIST, &job, (nuint)sizeof(HANDLE));
                Update(list, PInvoke.PROC_THREAD_ATTRIBUTE_HANDLE_LIST, inherited, (nuint)(3 * sizeof(HANDLE)));
                return Create(start, list, inherited);
            }
            finally
            {
                PInvoke.DeleteProcThreadAttributeList(list);
            }
        }
        finally
        {
            NativeMemory.Free(list);
        }
    }

    private unsafe PROCESS_INFORMATION Create(ChildStart start, LPPROC_THREAD_ATTRIBUTE_LIST list, HANDLE* inherited)
    {
        var commandLine = Spar.Testing.CommandLine.Build(start.Executable, start.Arguments) + '\0';
        var block = EnvironmentBlock(start.Environment);
        var desktop = _desktop is null ? null : _desktop + '\0';

        var startup = new STARTUPINFOEXW();
        startup.StartupInfo.cb = (uint)sizeof(STARTUPINFOEXW);
        startup.StartupInfo.dwFlags = STARTUPINFOW_FLAGS.STARTF_USESTDHANDLES;
        startup.StartupInfo.hStdInput = inherited[0];
        startup.StartupInfo.hStdOutput = inherited[1];
        startup.StartupInfo.hStdError = inherited[2];
        startup.lpAttributeList = list;
        if (start.Window && !start.Activate)
        {
            // The app's first ShowWindow(SW_SHOWDEFAULT) takes this: shown, the focus left where it is.
            startup.StartupInfo.dwFlags |= STARTUPINFOW_FLAGS.STARTF_USESHOWWINDOW;
            startup.StartupInfo.wShowWindow = ShowNoActivate;
        }

        // A process group of its own: the terminal's Ctrl+C is the tool's. A console program gets a hidden console of
        // its own rather than the tool's.
        var flags = PROCESS_CREATION_FLAGS.CREATE_UNICODE_ENVIRONMENT | PROCESS_CREATION_FLAGS.EXTENDED_STARTUPINFO_PRESENT
            | PROCESS_CREATION_FLAGS.CREATE_NEW_PROCESS_GROUP;
        if (!start.Window)
        {
            flags |= PROCESS_CREATION_FLAGS.CREATE_NO_WINDOW;
        }

        PROCESS_INFORMATION info;
        fixed (char* application = start.Executable)
        fixed (char* command = commandLine)
        fixed (char* variables = block)
        fixed (char* directory = start.WorkingDirectory)
        fixed (char* desktopName = desktop)
        {
            startup.StartupInfo.lpDesktop = desktopName;
            if (!PInvoke.CreateProcess(application, command, null, null, true, flags, variables, directory, (STARTUPINFOW*)&startup, &info))
            {
                var code = Marshal.GetLastPInvokeError();
                throw new InvalidOperationException($"{Path.GetFileName(start.Executable)} could not be started: CreateProcess failed ({code}).");
            }
        }

        return info;
    }

    private static unsafe void Update(LPPROC_THREAD_ATTRIBUTE_LIST list, uint attribute, void* value, nuint size)
    {
        if (!PInvoke.UpdateProcThreadAttribute(list, 0, attribute, value, size, null, (nuint*)null))
        {
            throw new InvalidOperationException($"UpdateProcThreadAttribute failed ({Marshal.GetLastPInvokeError()}).");
        }
    }

    /// <summary>The tool's environment with <paramref name="changes"/>, as <c>CreateProcess</c> takes it: sorted <c>name=value</c> strings.</summary>
    private static string EnvironmentBlock(IReadOnlyDictionary<string, string?> changes)
    {
        var variables = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            variables[(string)entry.Key] = (string?)entry.Value ?? "";
        }

        foreach (var (name, value) in changes)
        {
            if (value is null)
            {
                variables.Remove(name);
            }
            else
            {
                variables[name] = value;
            }
        }

        var text = new StringBuilder();
        foreach (var (name, value) in variables)
        {
            text.Append(name).Append('=').Append(value).Append('\0');
        }

        return text.Append('\0').ToString();
    }

    /// <summary>One process of the job: its handle, its exit, and its two output pipes read line by line.</summary>
    private sealed class JobChild : IChild
    {
        private readonly SafeFileHandle _process;
        private readonly ManualResetEvent _exitEvent;
        private readonly RegisteredWaitHandle _exitWait;
        private readonly TaskCompletionSource<int> _exited = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public JobChild(PROCESS_INFORMATION info, SafeFileHandle output, SafeFileHandle error, Action<string, bool> line)
        {
            _process = new SafeFileHandle(info.hProcess, ownsHandle: true);
            PInvoke.CloseHandle(info.hThread);
            ProcessId = (int)info.dwProcessId;
            OutputLines.Read(new FileStream(output, FileAccess.Read, 1, isAsync: false), text => line(text, false));
            OutputLines.Read(new FileStream(error, FileAccess.Read, 1, isAsync: false), text => line(text, true));
            _exitEvent = new ManualResetEvent(false) { SafeWaitHandle = new SafeWaitHandle(info.hProcess, ownsHandle: false) };
            _exitWait = ThreadPool.RegisterWaitForSingleObject(_exitEvent, static (state, _) => ((JobChild)state!).OnExited(), this, Timeout.Infinite, executeOnlyOnce: true);
        }

        public int ProcessId { get; }

        public Task<int> Exited => _exited.Task;

        public void Kill() => PInvoke.TerminateProcess((HANDLE)_process.DangerousGetHandle(), 1);

        public void Dispose()
        {
            _exitWait.Unregister(null);
            _exitEvent.Dispose();
            _process.Dispose();
        }

        private unsafe void OnExited()
        {
            uint code;
            _exited.TrySetResult(PInvoke.GetExitCodeProcess((HANDLE)_process.DangerousGetHandle(), &code) ? (int)code : -1);
        }
    }
}

/// <summary>A child's output stream read to its end on a thread of its own, one line at a time.</summary>
internal static class OutputLines
{
    public static void Read(Stream stream, Action<string> line)
    {
        var thread = new Thread(() =>
        {
            try
            {
                using var reader = new StreamReader(stream, Encoding.UTF8);
                while (reader.ReadLine() is { } text)
                {
                    line(text);
                }
            }
            catch (Exception exception) when (exception is IOException or ObjectDisposedException)
            {
                // The pipe broke: what was read went out already.
            }
        })
        {
            IsBackground = true,
            Name = "spar dev output",
        };
        thread.Start();
    }
}
