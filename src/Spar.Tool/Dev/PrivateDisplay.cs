using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.StationsAndDesktops;

namespace Spar.Tool.Dev;

/// <summary>
/// Where <c>spar dev --private</c> runs the app (decision "M2b's decide-first questions", item 4): somewhere the user
/// cannot see it and that never takes their focus or input, while the devtools still read and drive the page. On
/// Windows a desktop of its own in the tool's window station, never switched to, freed when the last process on it and
/// the tool close it; on Linux a private Xvfb on an abstract socket only, started in the session's group.
/// </summary>
internal sealed class PrivateDisplay : IDisposable
{
    private readonly Action? _close;

    private PrivateDisplay(string? desktop, IReadOnlyDictionary<string, string?> environment, string description, Action? close)
    {
        Desktop = desktop;
        Environment = environment;
        Description = description;
        _close = close;
    }

    /// <summary>The desktop's full name for <c>STARTUPINFO.lpDesktop</c>, on Windows.</summary>
    public string? Desktop { get; }

    /// <summary>The variables the app gets: on Linux the display and the X11 backend.</summary>
    public IReadOnlyDictionary<string, string?> Environment { get; }

    public string Description { get; }

    [SupportedOSPlatform("windows10.0.17763")]
    public static unsafe PrivateDisplay CreateDesktop()
    {
        var name = "spar-dev-" + Convert.ToHexStringLower(System.Security.Cryptography.RandomNumberGenerator.GetBytes(6));
        HDESK desktop;
        fixed (char* text = name)
        {
            // GENERIC_ALL: the creator keeps every right on its own desktop.
            desktop = PInvoke.CreateDesktop(text, null, null, 0, 0x10000000, null);
        }

        if (desktop.IsNull)
        {
            throw new InvalidOperationException($"CreateDesktop failed ({Marshal.GetLastPInvokeError()}).");
        }

        var handle = new CloseDesktopSafeHandle(desktop, ownsHandle: true);
        return new PrivateDisplay(StationName() + "\\" + name, new Dictionary<string, string?>(), $"a desktop of its own ({name})", handle.Dispose);
    }

    /// <summary>
    /// Starts Xvfb on a display number it picks itself (<c>-displayfd</c>), with no TCP and no socket file (an abstract
    /// socket only), and waits until it accepts clients.
    /// </summary>
    [SupportedOSPlatform("linux")]
    public static async Task<PrivateDisplay> StartXvfbAsync(IChildProcesses children, CancellationToken cancellationToken)
    {
        var xvfb = Machine.Processes.Resolve("Xvfb")
            ?? throw new InvalidOperationException("spar dev --private needs Xvfb, which is not on PATH: install the xvfb package (sudo apt install xvfb).");
        var number = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var child = children.Start(
            new ChildStart(xvfb, ["-displayfd", "1", "-screen", "0", "1920x1080x24", "-nolisten", "tcp", "-nolisten", "unix"], "/", new Dictionary<string, string?>()),
            (line, error) =>
            {
                if (!error && line.Trim() is { Length: > 0 } text && text.All(char.IsAsciiDigit))
                {
                    number.TrySetResult(text);
                }
            });

        var ready = await Task.WhenAny(number.Task, child.Exited, Task.Delay(TimeSpan.FromSeconds(15), cancellationToken));
        if (ready != number.Task)
        {
            child.Kill();
            throw new InvalidOperationException("Xvfb did not start a display within 15 s.");
        }

        var display = ":" + number.Task.Result;
        var environment = new Dictionary<string, string?>
        {
            ["DISPLAY"] = display,
            ["GDK_BACKEND"] = "x11",
            ["WAYLAND_DISPLAY"] = null,
        };
        return new PrivateDisplay(null, environment, $"a private Xvfb on display {display}", child.Kill);
    }

    public void Dispose() => _close?.Invoke();

    [SupportedOSPlatform("windows10.0.17763")]
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
}
