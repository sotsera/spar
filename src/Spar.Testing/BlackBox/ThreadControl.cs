using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Diagnostics.ToolHelp;
using Windows.Win32.System.Threading;

namespace Spar.Testing;

/// <summary>
/// Suspends every thread of one process (a Toolhelp snapshot, then <c>SuspendThread</c> on each), so Spar's own tests
/// can make a launched app hang the way a deadlocked one does: its window and its relaunch pipe stop answering.
/// </summary>
[SupportedOSPlatform("windows10.0.17763")]
internal static unsafe class ThreadControl
{
    public static void SuspendAll(int processId)
    {
        var snapshot = PInvoke.CreateToolhelp32Snapshot(CREATE_TOOLHELP_SNAPSHOT_FLAGS.TH32CS_SNAPTHREAD, 0);
        if (snapshot.IsNull || snapshot == (HANDLE)(-1))
        {
            throw new InvalidOperationException($"CreateToolhelp32Snapshot failed ({Marshal.GetLastPInvokeError()}).");
        }

        try
        {
            var entry = new THREADENTRY32 { dwSize = (uint)sizeof(THREADENTRY32) };
            for (var more = PInvoke.Thread32First(snapshot, &entry); more; more = PInvoke.Thread32Next(snapshot, &entry))
            {
                if (entry.th32OwnerProcessID != (uint)processId)
                {
                    continue;
                }

                var thread = PInvoke.OpenThread(THREAD_ACCESS_RIGHTS.THREAD_SUSPEND_RESUME, false, entry.th32ThreadID);
                if (thread.IsNull)
                {
                    continue;
                }

                PInvoke.SuspendThread(thread);
                PInvoke.CloseHandle(thread);
            }
        }
        finally
        {
            PInvoke.CloseHandle(snapshot);
        }
    }
}
