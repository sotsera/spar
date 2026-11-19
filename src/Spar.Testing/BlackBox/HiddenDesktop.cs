using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.StationsAndDesktops;

namespace Spar.Testing;

/// <summary>
/// A desktop of its own in the test process's window station (<c>CreateDesktop</c>), never switched to: windows there are
/// never on the monitor and can never take the user's focus or input. A process is started on it through
/// <c>STARTUPINFO.lpDesktop</c> (<see cref="Name"/>, with no window station, so the caller's: <c>WinSta0</c> on a
/// desktop session, whatever a CI service runs in otherwise); a thread reaches its windows only after
/// <c>SetThreadDesktop</c>. Windows frees it when the last handle closes: this one's and those of the processes on it.
/// It registers nothing with the system.
/// </summary>
[SupportedOSPlatform("windows10.0.17763")]
internal sealed class HiddenDesktop : IDisposable
{
    /// <summary><c>GENERIC_ALL</c>: the creator keeps every right on its own desktop.</summary>
    private const uint GenericAll = 0x10000000;

    private CloseDesktopSafeHandle? _handle;

    public string Name { get; }

    public CloseDesktopSafeHandle Handle => _handle ?? throw new ObjectDisposedException(nameof(HiddenDesktop));

    private HiddenDesktop(CloseDesktopSafeHandle handle, string name)
    {
        _handle = handle;
        Name = name;
    }

    public static unsafe HiddenDesktop Create(string name)
    {
        HDESK desktop;
        fixed (char* text = name)
        {
            desktop = PInvoke.CreateDesktop(text, null, null, 0, GenericAll, null);
        }

        if (desktop.IsNull)
        {
            throw new InvalidOperationException($"CreateDesktop failed ({Marshal.GetLastPInvokeError()}).");
        }

        // The full name: a desktop name alone is resolved by the window-station connection rules, which pick WinSta0
        // for a process in the interactive user's logon session even when the test process runs in another station.
        var handle = new CloseDesktopSafeHandle(desktop, ownsHandle: true);
        return new HiddenDesktop(handle, StationName() + "\\" + name);
    }

    /// <summary>The name of the test process's window station: <c>WinSta0</c> on a desktop session, a service's own otherwise.</summary>
    private static unsafe string StationName()
    {
        var station = PInvoke.GetProcessWindowStation();
        const int Capacity = 256;
        var buffer = stackalloc char[Capacity];
        uint needed;
        if (!PInvoke.GetUserObjectInformation((HANDLE)station.Value, USER_OBJECT_INFORMATION_INDEX.UOI_NAME, buffer, Capacity * sizeof(char), &needed))
        {
            throw new InvalidOperationException($"GetUserObjectInformation failed ({Marshal.GetLastPInvokeError()}).");
        }

        return new string(buffer);
    }

    public void Dispose()
    {
        _handle?.Dispose();
        _handle = null;
    }
}
