using System.Collections;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using Sotsera.Spar.Development.Interop;

namespace Sotsera.Spar.Development;

/// <summary>Owns only explicitly created children. The non-inherited job handle kills their descendants on owner exit.</summary>
public sealed class OwnedProcessJob : IDisposable
{
    private readonly SafeJobHandle _handle;
    private readonly OwnedProcessJob? _parent;

    public OwnedProcessJob() : this(null)
    {
    }

    internal OwnedProcessJob(OwnedProcessJob? parent)
    {
        _parent = parent;
        _handle = ProcessNative.CreateJobObject(0, 0);
        if (_handle.IsInvalid)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        var limits = new ExtendedLimits { Basic = new BasicLimits { Flags = 0x2000 } }; // KILL_ON_JOB_CLOSE
        if (!ProcessNative.SetInformationJobObject(_handle, 9, in limits, (uint)Marshal.SizeOf<ExtendedLimits>()))
        {
            var error = Marshal.GetLastPInvokeError();
            _handle.Dispose();
            throw new Win32Exception(error);
        }
    }

    /// <summary>Job membership and the stdio handle whitelist apply atomically at process creation, before user code.</summary>
    public unsafe Process Start(string executable, IReadOnlyList<string> arguments, string directory,
        IReadOnlyDictionary<string, string> environment, string stdout, string stderr)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executable);
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(environment);
        executable = ResolveExecutable(executable);
        using var output = new FileStream(stdout, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
        using var error = new FileStream(stderr, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
        using var input = new FileStream("NUL", FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        nint* inherited = stackalloc nint[] { input.SafeFileHandle.DangerousGetHandle(), output.SafeFileHandle.DangerousGetHandle(),
            error.SafeFileHandle.DangerousGetHandle() };
        for (var index = 0; index < 3; index++)
        {
            Check(ProcessNative.SetHandleInformation(inherited[index], 1, 1));
        }

        nuint size = 0;
        ProcessNative.InitializeProcThreadAttributeList(0, 2, 0, ref size);
        var list = (nint)NativeMemory.Alloc(size);
        var initialized = false;
        try
        {
            Check(ProcessNative.InitializeProcThreadAttributeList(list, 2, 0, ref size));
            initialized = true;
            nint* jobs = stackalloc nint[2];
            var jobCount = _parent is null ? 1 : 2;
            jobs[0] = _parent?._handle.DangerousGetHandle() ?? _handle.DangerousGetHandle();
            jobs[jobCount - 1] = _handle.DangerousGetHandle();
            // PROC_THREAD_ATTRIBUTE_JOB_LIST (13 | INPUT), HANDLE_LIST (2 | INPUT), pinned SDK definitions.
            Check(ProcessNative.UpdateProcThreadAttribute(list, 0, 0x2000d, (nint)jobs,
                (nuint)(jobCount * sizeof(nint)), 0, 0));
            Check(ProcessNative.UpdateProcThreadAttribute(list, 0, 0x20002, (nint)inherited, (nuint)(3 * sizeof(nint)), 0, 0));
            var startup = new StartupInfoEx
            {
                Attributes = list,
                Startup = new StartupInfo
                {
                    Size = (uint)sizeof(StartupInfoEx),
                    Flags = 0x100,
                    Input = inherited[0],
                    Output = inherited[1],
                    Error = inherited[2]
                }
            };
            var command = (Quote(executable) + " " + string.Join(' ', arguments.Select(Quote)) + '\0').ToCharArray();
            var variables = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
            {
                var name = (string)entry.Key;
                if (!name.StartsWith("SPAR_DEV_", StringComparison.OrdinalIgnoreCase))
                {
                    variables[name] = (string)entry.Value!;
                }
            }

            foreach (var entry in environment)
            {
                variables[entry.Key] = entry.Value;
            }
            var block = string.Concat(variables.Select(pair => pair.Key + "=" + pair.Value + '\0')) + '\0';
            fixed (char* commandPointer = command)
            fixed (char* environmentPointer = block)
            {
                // EXTENDED_STARTUPINFO_PRESENT | CREATE_UNICODE_ENVIRONMENT | CREATE_SUSPENDED | CREATE_NO_WINDOW
                Check(ProcessNative.CreateProcess(executable, commandPointer, 0, 0, true, 0x08080404,
                    environmentPointer, directory, in startup, out var information));
                using var nativeProcess = new SafeProcessHandle(information.Process, true);
                using var nativeThread = new SafeWaitHandle(information.Thread, true);
                Process? process = null;
                try
                {
                    process = Process.GetProcessById((int)information.ProcessId);
                    _ = process.Handle; // Retain the creation identity before any application code executes.
                    if (ProcessNative.ResumeThread(nativeThread) == uint.MaxValue)
                    {
                        throw new Win32Exception(Marshal.GetLastPInvokeError());
                    }

                    GC.KeepAlive(_handle);
                    GC.KeepAlive(_parent);
                    return process;
                }
                catch
                {
                    ProcessNative.TerminateProcess(nativeProcess, 1);
                    process?.Dispose();
                    throw;
                }
            }
        }
        finally
        {
            if (initialized)
            {
                ProcessNative.DeleteProcThreadAttributeList(list);
            }
            NativeMemory.Free((void*)list);
        }
    }

    public bool Contains(Process process)
    {
        ArgumentNullException.ThrowIfNull(process);
        Check(ProcessNative.IsProcessInJob(process.Handle, _handle, out var result));
        return result != 0;
    }

    public unsafe int[] ActiveProcessIds()
    {
        var bytes = new byte[8 + 1024 * sizeof(nuint)];
        fixed (byte* buffer = bytes)
        {
            Check(ProcessNative.QueryInformationJobObject(_handle, 3, buffer, (uint)bytes.Length, out _));
            var count = ((uint*)buffer)[1];
            var ids = new int[count];
            for (var index = 0; index < count; index++)
            {
                ids[index] = checked((int)((nuint*)(buffer + 8))[index]);
            }
            return ids;
        }
    }

    public void Terminate()
    {
        Check(ProcessNative.TerminateJobObject(_handle, 1));
    }

    public void Dispose() => _handle.Dispose();

    private static string ResolveExecutable(string value)
    {
        var name = Path.HasExtension(value) ? value : value + ".exe";
        if (Path.IsPathRooted(name) && File.Exists(name))
        {
            return name;
        }
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            var path = Path.Combine(directory, name);
            if (File.Exists(path))
            {
                return Path.GetFullPath(path);
            }
        }

        throw new FileNotFoundException("Executable was not found. Use an .exe, or invoke cmd.exe explicitly for package scripts.", value);
    }

    private static string Quote(string value)
    {
        if (value.Length != 0 && !value.Any(character => char.IsWhiteSpace(character) || character == '"'))
        {
            return value;
        }
        var result = new StringBuilder("\"");
        var slashes = 0;
        foreach (var character in value)
        {
            if (character == '\\')
            {
                slashes++;
                continue;
            }
            result.Append('\\', character == '"' ? slashes * 2 + 1 : slashes);
            result.Append(character);
            slashes = 0;
        }

        return result.Append('\\', slashes * 2).Append('"').ToString();
    }

    private static void Check(bool result)
    {
        if (!result)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }
    }
}
