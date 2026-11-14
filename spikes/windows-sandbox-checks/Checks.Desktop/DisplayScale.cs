using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Checks.Desktop.Interop;
using static Checks.Desktop.Interop.Native;

namespace Checks.Desktop;

// Reads and changes the display scale of the primary display path, as the Settings app does, through the
// undocumented DisplayConfig device-info types -3 (get DPI scale) and -4 (set DPI scale). Scales are relative steps
// from the recommended scale. Windows Sandbox only: it changes a display setting.
internal static unsafe class DisplayScale
{
    private const int GetDpiScale = -3;
    private const int SetDpiScale = -4;
    private const uint QdcOnlyActivePaths = 2;
    private const int PathSize = 72;
    private const int ModeSize = 64;
    private static readonly int[] s_steps = [100, 125, 150, 175, 200, 225, 250, 300, 350, 400, 450, 500];

    public static bool InSandbox => string.Equals(Environment.UserName, "WDAGUtilityAccount", StringComparison.OrdinalIgnoreCase);

    public static string Describe()
    {
        var (hr, min, cur, max, _, _) = Query();
        if (hr != 0)
        {
            return $"get failed {hr}";
        }

        var recommended = -min;
        return $"current={Percent(recommended + cur)}% recommended={s_steps[recommended]}% range={Percent(recommended + min)}..{Percent(recommended + max)}% primaryDpi={PrimaryDpi()}";
    }

    public static string Set(int percent, bool force = false)
    {
        if (!InSandbox)
        {
            return "refused: not the Windows Sandbox account";
        }

        var (hr, min, _, max, adapter, source) = Query();
        if (hr != 0)
        {
            return $"get failed {hr}";
        }

        var recommended = -min;
        var target = Array.IndexOf(s_steps, percent) - recommended;
        if (Array.IndexOf(s_steps, percent) < 0 || (!force && (target < min || target > max)))
        {
            return $"refused: {percent}% outside {Percent(recommended + min)}..{Percent(recommended + max)}%";
        }

        var packet = stackalloc byte[24];
        Header(packet, SetDpiScale, 24, adapter, source);
        BinaryPrimitives.WriteInt32LittleEndian(new Span<byte>(packet + 20, 4), target);
        var set = DisplayConfigSetDeviceInfo(packet);
        // The Settings app's other route: SPI_SETLOGICALDPIOVERRIDE with the same relative step.
        var spi = set == 0 ? -1 : SystemParametersInfo(0x009F, target, 0, 0x1 | 0x2);
        var spiError = spi == 0 ? Marshal.GetLastPInvokeError() : 0;
        return $"set={set} spi={spi} spiError={spiError} remoteSession={GetSystemMetrics(0x1000)} relative={target} now: {Describe()}";
    }

    private static int Percent(int index) => index >= 0 && index < s_steps.Length ? s_steps[index] : -1;

    private static uint PrimaryDpi()
    {
        var monitor = MonitorFromPoint(default, 1); // MONITOR_DEFAULTTOPRIMARY
        GetDpiForMonitor(monitor, 0, out var dpi, out _);
        return dpi;
    }

    private static void Header(byte* p, int type, int size, long adapter, uint source)
    {
        var span = new Span<byte>(p, size);
        span.Clear();
        BinaryPrimitives.WriteInt32LittleEndian(span, type);
        BinaryPrimitives.WriteInt32LittleEndian(span[4..], size);
        BinaryPrimitives.WriteInt64LittleEndian(span[8..], adapter);
        BinaryPrimitives.WriteUInt32LittleEndian(span[16..], source);
    }

    private static (int Hr, int Min, int Cur, int Max, long Adapter, uint Source) Query()
    {
        var hr = GetDisplayConfigBufferSizes(QdcOnlyActivePaths, out var paths, out var modes);
        if (hr != 0 || paths == 0)
        {
            return (hr == 0 ? -1 : hr, 0, 0, 0, 0, 0);
        }

        var pathBuffer = (byte*)NativeMemory.AllocZeroed(paths * PathSize);
        var modeBuffer = (byte*)NativeMemory.AllocZeroed(modes * ModeSize);
        try
        {
            hr = QueryDisplayConfig(QdcOnlyActivePaths, ref paths, pathBuffer, ref modes, modeBuffer, 0);
            if (hr != 0)
            {
                return (hr, 0, 0, 0, 0, 0);
            }

            // DISPLAYCONFIG_PATH_INFO starts with sourceInfo: LUID adapterId (8 bytes), UINT32 id.
            var adapter = BinaryPrimitives.ReadInt64LittleEndian(new ReadOnlySpan<byte>(pathBuffer, 8));
            var source = BinaryPrimitives.ReadUInt32LittleEndian(new ReadOnlySpan<byte>(pathBuffer + 8, 4));
            var packet = stackalloc byte[32];
            Header(packet, GetDpiScale, 32, adapter, source);
            hr = DisplayConfigGetDeviceInfo(packet);
            var data = new ReadOnlySpan<byte>(packet, 32);
            return (hr, BinaryPrimitives.ReadInt32LittleEndian(data[20..]), BinaryPrimitives.ReadInt32LittleEndian(data[24..]),
                BinaryPrimitives.ReadInt32LittleEndian(data[28..]), adapter, source);
        }
        finally
        {
            NativeMemory.Free(pathBuffer);
            NativeMemory.Free(modeBuffer);
        }
    }
}
