using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Spar.Spikes.GpuProbe;

internal static partial class DrmNative
{
    // drmVersion (xf86drm.h): int major, minor, patchlevel; int name_len; char* name; int date_len; char* date;
    // int desc_len; char* desc. On x64 the name pointer is at offset 16 and desc at 48.
    [LibraryImport("libdrm.so.2")]
    public static partial nint drmGetVersion(int fd);

    [LibraryImport("libdrm.so.2")]
    public static partial void drmFreeVersion(nint version);
}

internal sealed class NodeInfo
{
    public string Path = "";
    public bool Openable;
    public string? OpenError;
    public string? SysfsDriver;
    public string? PciVendor;
    public string? DrmName;
    public string? DrmDesc;
}

/// <summary>Is any /dev/dri/renderD* openable read-write, and which kernel driver backs it.</summary>
internal static class RenderNodeProbe
{
    // Kernel DRM drivers that render nothing on a GPU: a render node backed by one of these is no hardware signal.
    private static readonly string[] s_virtualDrivers = ["vgem", "vkms", "faux_driver", "faux"];

    public static List<NodeInfo> Run(bool drmVersion)
    {
        var nodes = new List<NodeInfo>();
        if (!Directory.Exists("/dev/dri"))
        {
            return nodes;
        }
        foreach (var path in Directory.EnumerateFiles("/dev/dri", "renderD*").Order(StringComparer.Ordinal))
        {
            var info = new NodeInfo { Path = path };
            var name = System.IO.Path.GetFileName(path);
            info.SysfsDriver = LinkName($"/sys/class/drm/{name}/device/driver");
            info.PciVendor = ReadTrim($"/sys/class/drm/{name}/device/vendor");
            try
            {
                using SafeFileHandle handle = File.OpenHandle(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
                info.Openable = true;
                if (drmVersion)
                {
                    ReadDrmVersion((int)handle.DangerousGetHandle(), info);
                }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                info.OpenError = ex.GetType().Name + ": " + ex.Message;
            }
            nodes.Add(info);
        }
        return nodes;
    }

    /// <summary>A node that is openable and not backed by a virtual driver.</summary>
    public static bool IsHardwareCandidate(NodeInfo n) =>
        n.Openable && !IsVirtual(n.DrmName) && !IsVirtual(n.SysfsDriver);

    private static bool IsVirtual(string? driver) =>
        driver is not null && s_virtualDrivers.Contains(driver, StringComparer.Ordinal);

    private static unsafe void ReadDrmVersion(int fd, NodeInfo info)
    {
        try
        {
            var v = DrmNative.drmGetVersion(fd);
            if (v == 0)
            {
                return;
            }
            info.DrmName = Marshal.PtrToStringUTF8(*(nint*)(v + 16));
            info.DrmDesc = Marshal.PtrToStringUTF8(*(nint*)(v + 48));
            DrmNative.drmFreeVersion(v);
        }
        catch (DllNotFoundException)
        {
            info.DrmName = "(no libdrm)";
        }
    }

    private static string? LinkName(string path)
    {
        try
        {
            var target = new FileInfo(path).LinkTarget;
            return target is null ? null : System.IO.Path.GetFileName(target);
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static string? ReadTrim(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path).Trim() : null;
        }
        catch (IOException)
        {
            return null;
        }
    }
}
