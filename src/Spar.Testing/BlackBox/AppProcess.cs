using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Microsoft.Win32.SafeHandles;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Security;
using Windows.Win32.System.JobObjects;
using Windows.Win32.System.Threading;

namespace Spar.Testing;

/// <summary>
/// A launched app's process and everything it starts (the browser's processes included), in a job object of its own
/// with <c>JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE</c>, which the process joins at creation (<c>PROC_THREAD_ATTRIBUTE_JOB_LIST</c>):
/// when the test process ends, however it ends, Windows closes the job's handle and ends the whole tree. Standard output
/// and error are pipes read on threads of their own; only those two handles are inherited
/// (<c>PROC_THREAD_ATTRIBUTE_HANDLE_LIST</c>).
/// </summary>
[SupportedOSPlatform("windows10.0.17763")]
internal sealed class AppProcess : IBlackBoxProcess
{
    private const uint StillActive = 259;

    private readonly SafeFileHandle _job;
    private readonly SafeFileHandle _process;
    private readonly OutputReader _output;
    private readonly OutputReader _error;
    private readonly TaskCompletionSource<int> _exited = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly RegisteredWaitHandle _exitWait;
    private readonly ManualResetEvent _exitEvent;

    public int ProcessId { get; }

    public string StandardOutput => _output.Text;

    public string StandardError => _error.Text;

    public Task<int> Exited => _exited.Task;

    /// <summary>The exit code once the process has exited; null while it runs.</summary>
    public unsafe int? ExitCode
    {
        get
        {
            uint code;
            if (!PInvoke.GetExitCodeProcess((HANDLE)_process.DangerousGetHandle(), &code) || code == StillActive && !_exited.Task.IsCompleted)
            {
                return null;
            }

            return (int)code;
        }
    }

    /// <summary>How many processes of the job are still running: the app and what it started.</summary>
    public int RunningProcesses => (int)Accounting().ActiveProcesses;

    /// <summary>How many processes the job has had in all, the exited ones included.</summary>
    public int StartedProcesses => (int)Accounting().TotalProcesses;

    /// <summary>The ids of the job's running processes: the app and what it started (at most 256 are listed).</summary>
    public unsafe IReadOnlyList<int> ProcessIds
    {
        get
        {
            const int Capacity = 256;
            var size = sizeof(JOBOBJECT_BASIC_PROCESS_ID_LIST) + (Capacity * sizeof(nuint));
            var buffer = stackalloc byte[size];
            var list = (JOBOBJECT_BASIC_PROCESS_ID_LIST*)buffer;
            uint returned;
            var job = (HANDLE)_job.DangerousGetHandle();
            if (!PInvoke.QueryInformationJobObject(job, JOBOBJECTINFOCLASS.JobObjectBasicProcessIdList, list, (uint)size, &returned))
            {
                return [];
            }

            var ids = new int[list->NumberOfProcessIdsInList];
            var first = &list->ProcessIdList.e0;
            for (var index = 0; index < ids.Length; index++)
            {
                ids[index] = (int)first[index];
            }

            return ids;
        }
    }

    private AppProcess(SafeFileHandle job, PROCESS_INFORMATION info, OutputReader output, OutputReader error)
    {
        _job = job;
        _process = new SafeFileHandle(info.hProcess, ownsHandle: true);
        PInvoke.CloseHandle(info.hThread);
        ProcessId = (int)info.dwProcessId;
        _output = output;
        _error = error;
        _exitEvent = new ManualResetEvent(false) { SafeWaitHandle = new SafeWaitHandle(info.hProcess, ownsHandle: false) };
        _exitWait = ThreadPool.RegisterWaitForSingleObject(
            _exitEvent, static (state, _) => ((AppProcess)state!).OnExited(), this, Timeout.Infinite, executeOnlyOnce: true);
    }

    /// <summary>
    /// Starts <paramref name="executable"/> with <paramref name="environment"/> as its whole environment, on
    /// the desktop named <paramref name="desktop"/> in the caller's window station, or the caller's own, shown as
    /// <paramref name="showWindow"/> says
    /// (<c>STARTF_USESHOWWINDOW</c>, which the app's first <c>ShowWindow(SW_SHOWDEFAULT)</c> takes), or as it chooses.
    /// </summary>
    public static AppProcess Start(
        string executable,
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string> environment,
        string workingDirectory,
        string? desktop,
        ushort? showWindow)
    {
        var job = CreateJob();
        CreateOutputPipe(out var outputRead, out var outputWrite);
        CreateOutputPipe(out var errorRead, out var errorWrite);
        try
        {
            var info = Create(executable, arguments, environment, workingDirectory, desktop, showWindow, job, outputWrite, errorWrite);
            return new AppProcess(job, info, new OutputReader(outputRead), new OutputReader(errorRead));
        }
        catch
        {
            outputRead.Dispose();
            errorRead.Dispose();
            job.Dispose();
            throw;
        }
        finally
        {
            // The child has its own copies; the pipes end when it (and everything it gave them to) exits.
            outputWrite.Dispose();
            errorWrite.Dispose();
        }
    }

    /// <summary>Ends the app and everything it started, at once (<c>TerminateJobObject</c>), with exit code 1.</summary>
    public void Kill() => PInvoke.TerminateJobObject((HANDLE)_job.DangerousGetHandle(), 1);

    /// <summary>Waits until no process of the job is left, or <paramref name="limit"/> passes; true when none is left.</summary>
    public async Task<bool> WaitForTreeAsync(TimeSpan limit)
    {
        var deadline = DateTime.UtcNow + limit;
        while (RunningProcesses > 0)
        {
            if (DateTime.UtcNow >= deadline)
            {
                return false;
            }

            await Task.Delay(25, CancellationToken.None);
        }

        return true;
    }

    /// <summary>Waits until both output pipes ended (every process holding them exited), up to <paramref name="limit"/>.</summary>
    public Task<bool> WaitForOutputAsync(TimeSpan limit) =>
        Task.WhenAll(_output.Ended, _error.Ended).WaitAsync(limit).ContinueWith(static task => task.IsCompletedSuccessfully, TaskScheduler.Default);

    /// <summary>
    /// Suspends every thread of the app's own process (not of the browser's), as a hung process: for Spar's own tests.
    /// </summary>
    public void SuspendProcess() => ThreadControl.SuspendAll(ProcessId);

    /// <summary>Closes the job's handle: with no other handle open, Windows ends every process still in it.</summary>
    public void Dispose()
    {
        _exitWait.Unregister(null);
        _exitEvent.Dispose();
        _job.Dispose();
        _process.Dispose();
        _output.Dispose();
        _error.Dispose();
    }

    /// <summary>The job: kill on close, so a crashed test run leaves no app behind.</summary>
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
        var size = (uint)sizeof(JOBOBJECT_EXTENDED_LIMIT_INFORMATION);
        if (!PInvoke.SetInformationJobObject(handle, JOBOBJECTINFOCLASS.JobObjectExtendedLimitInformation, &limits, size))
        {
            var error = Marshal.GetLastPInvokeError();
            job.Dispose();
            throw new InvalidOperationException($"SetInformationJobObject failed ({error}).");
        }

        return job;
    }

    /// <summary>A pipe whose write end the child inherits and whose read end it never sees.</summary>
    private static unsafe void CreateOutputPipe(out SafeFileHandle read, out SafeFileHandle write)
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

    private static unsafe PROCESS_INFORMATION Create(
        string executable,
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string> environment,
        string workingDirectory,
        string? desktop,
        ushort? showWindow,
        SafeFileHandle job,
        SafeFileHandle output,
        SafeFileHandle error)
    {
        nuint size = 0;
        PInvoke.InitializeProcThreadAttributeList(default, 2, 0, &size);
        var attributeList = (LPPROC_THREAD_ATTRIBUTE_LIST)NativeMemory.Alloc(size);
        try
        {
            if (!PInvoke.InitializeProcThreadAttributeList(attributeList, 2, 0, &size))
            {
                throw new InvalidOperationException($"InitializeProcThreadAttributeList failed ({Marshal.GetLastPInvokeError()}).");
            }

            try
            {
                var jobHandle = (HANDLE)job.DangerousGetHandle();
                var inherited = stackalloc HANDLE[2] { (HANDLE)output.DangerousGetHandle(), (HANDLE)error.DangerousGetHandle() };
                Update(attributeList, PInvoke.PROC_THREAD_ATTRIBUTE_JOB_LIST, &jobHandle, (nuint)sizeof(HANDLE));
                Update(attributeList, PInvoke.PROC_THREAD_ATTRIBUTE_HANDLE_LIST, inherited, (nuint)(2 * sizeof(HANDLE)));
                return Create(executable, arguments, environment, workingDirectory, desktop, showWindow, attributeList, inherited);
            }
            finally
            {
                PInvoke.DeleteProcThreadAttributeList(attributeList);
            }
        }
        finally
        {
            NativeMemory.Free(attributeList);
        }
    }

    private static unsafe PROCESS_INFORMATION Create(
        string executable,
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string> environment,
        string workingDirectory,
        string? desktop,
        ushort? showWindow,
        LPPROC_THREAD_ATTRIBUTE_LIST attributeList,
        HANDLE* inherited)
    {
        var commandLine = CommandLine.Build(executable, arguments) + '\0';
        var block = EnvironmentBlock(environment);
        var desktopName = desktop is null ? null : desktop + '\0';

        var startup = new STARTUPINFOEXW();
        startup.StartupInfo.cb = (uint)sizeof(STARTUPINFOEXW);
        startup.StartupInfo.dwFlags = STARTUPINFOW_FLAGS.STARTF_USESTDHANDLES;
        startup.StartupInfo.hStdOutput = inherited[0];
        startup.StartupInfo.hStdError = inherited[1];
        startup.lpAttributeList = attributeList;
        if (showWindow is { } show)
        {
            startup.StartupInfo.dwFlags |= STARTUPINFOW_FLAGS.STARTF_USESHOWWINDOW;
            startup.StartupInfo.wShowWindow = show;
        }

        PROCESS_INFORMATION info;
        fixed (char* application = executable)
        fixed (char* command = commandLine)
        fixed (char* variables = block)
        fixed (char* directory = workingDirectory)
        fixed (char* desktopText = desktopName)
        {
            startup.StartupInfo.lpDesktop = desktopText;
            var flags = PROCESS_CREATION_FLAGS.CREATE_UNICODE_ENVIRONMENT | PROCESS_CREATION_FLAGS.EXTENDED_STARTUPINFO_PRESENT;
            if (!PInvoke.CreateProcess(application, command, null, null, true, flags, variables, directory, (STARTUPINFOW*)&startup, &info))
            {
                var error = Marshal.GetLastPInvokeError();
                throw new InvalidOperationException($"The app could not be started: CreateProcess failed ({error}) for '{executable}'.");
            }
        }

        return info;
    }

    private unsafe JOBOBJECT_BASIC_ACCOUNTING_INFORMATION Accounting()
    {
        JOBOBJECT_BASIC_ACCOUNTING_INFORMATION accounting;
        uint returned;
        var ok = PInvoke.QueryInformationJobObject(
            (HANDLE)_job.DangerousGetHandle(),
            JOBOBJECTINFOCLASS.JobObjectBasicAccountingInformation,
            &accounting,
            (uint)sizeof(JOBOBJECT_BASIC_ACCOUNTING_INFORMATION),
            &returned);
        return ok ? accounting : default;
    }

    private static unsafe void Update(LPPROC_THREAD_ATTRIBUTE_LIST list, uint attribute, void* value, nuint size)
    {
        if (!PInvoke.UpdateProcThreadAttribute(list, 0, attribute, value, size, null, (nuint*)null))
        {
            throw new InvalidOperationException($"UpdateProcThreadAttribute failed ({Marshal.GetLastPInvokeError()}).");
        }
    }

    /// <summary>The environment as <c>CreateProcess</c> takes it: <c>name=value</c> strings sorted by name, then an empty one.</summary>
    private static string EnvironmentBlock(IReadOnlyDictionary<string, string> environment)
    {
        var text = new StringBuilder();
        foreach (var (name, value) in environment.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            text.Append(name).Append('=').Append(value).Append('\0');
        }

        return text.Append('\0').ToString();
    }

    private void OnExited()
    {
        var code = ExitCode ?? -1;
        _exited.TrySetResult(code);
    }

    /// <summary>One output pipe, read to its end on a thread of its own; the text so far is always readable.</summary>
    private sealed class OutputReader : IDisposable
    {
        private readonly FileStream _stream;
        private readonly StringBuilder _text = new();
        private readonly Lock _gate = new();
        private readonly TaskCompletionSource _ended = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public OutputReader(SafeFileHandle read)
        {
            _stream = new FileStream(read, FileAccess.Read, 1, isAsync: false);
            var thread = new Thread(Read) { IsBackground = true, Name = "Spar black-box output" };
            thread.Start();
        }

        public string Text
        {
            get
            {
                lock (_gate)
                {
                    return _text.ToString();
                }
            }
        }

        public Task Ended => _ended.Task;

        public void Dispose() => _stream.Dispose();

        private void Read()
        {
            try
            {
                using var reader = new StreamReader(_stream, Encoding.UTF8);
                var buffer = new char[4096];
                int count;
                while ((count = reader.Read(buffer, 0, buffer.Length)) > 0)
                {
                    lock (_gate)
                    {
                        _text.Append(buffer, 0, count);
                    }
                }
            }
            catch (Exception exception) when (exception is IOException or ObjectDisposedException)
            {
                // The pipe broke or the fixture closed it: what was read stays.
            }
            finally
            {
                _ended.TrySetResult();
            }
        }
    }
}
