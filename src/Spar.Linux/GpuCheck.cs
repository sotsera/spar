using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using Spar.Hosting;
using Spar.Linux.Interop;

namespace Spar.Linux;

/// <summary>
/// The software-GPU default (plan section 3, "Software GPUs"; decision "M1c: the software-GPU detection method"), on the
/// process's main thread before GTK, WebKit or GDBus start a thread: with <see cref="HardwareAcceleration.Auto"/>, the
/// render nodes first (none this user can open, or only a virtual driver's, means software, in about 0.1 ms), else a
/// throwaway surfaceless EGL display whose GLES context names its renderer (about 20 ms warm, most of which GTK then
/// reuses). On a software GPU, or with <see cref="HardwareAcceleration.Never"/>, WebKit's DMA-BUF renderer is turned
/// off (which also turns its acceleration off) and GTK draws with cairo, through libc <c>setenv</c> without overwriting:
/// a variable the user set wins, and every child process (WebKit's, the app's) inherits the result.
/// </summary>
internal static class GpuCheck
{
    public const string DisableDmabufVariable = "WEBKIT_DISABLE_DMABUF_RENDERER";
    public const string RendererVariable = "GSK_RENDERER";

    /// <summary>Kernel drivers of render nodes that draw nothing on a GPU.</summary>
    private static readonly string[] VirtualDrivers = ["vgem", "vkms"];

    /// <summary>What <c>GL_RENDERER</c> says for Mesa's and Google's software renderers, and Windows' basic driver.</summary>
    private static readonly string[] SoftwareRenderers =
        ["llvmpipe", "softpipe", "lavapipe", "swiftshader", "swrast", "software rasterizer", "basic render driver"];

    /// <summary>Decides, sets the environment, logs the choice with its cost, and returns whether GPU drawing is off.</summary>
    public static bool Apply(HardwareAcceleration option, HostLog log)
    {
        var clock = Stopwatch.StartNew();
        var userDmabuf = Libc.Variable(DisableDmabufVariable);
        var userRenderer = Libc.Variable(RendererVariable);
        bool software;
        string found;
        if (option == HardwareAcceleration.Always)
        {
            (software, found) = (false, "not checked");
        }
        else if (option == HardwareAcceleration.Never)
        {
            (software, found) = (true, "not checked");
        }
        else if (userDmabuf is not null && userRenderer is not null)
        {
            (software, found) = (false, $"not checked: the user set {DisableDmabufVariable} and {RendererVariable}");
        }
        else
        {
            (software, found) = Detect();
        }

        var checkedIn = clock.Elapsed.TotalMilliseconds;
        var applied = software ? Set(userDmabuf, userRenderer) : "WebKit's and GTK's own GPU drawing";
        var state = software ? "off" : "on";
        log.Info(string.Create(
            CultureInfo.InvariantCulture,
            $"Hardware acceleration {option}: {state}, GPU {found}, checked in {checkedIn:0.0} ms with {Threads()} threads; {applied}."));
        log.Phase("gpu checked");
        return software;
    }

    /// <summary>Software or not, and what told: the render nodes, then the renderer's name.</summary>
    internal static (bool Software, string Found) Detect()
    {
        var (candidate, nodes) = RenderNodes();
        if (candidate is null)
        {
            return (true, $"software ({nodes})");
        }

        var (renderer, failure) = Renderer();
        if (renderer is null)
        {
            return (true, $"software (EGL on {candidate}: {failure})");
        }

        return (IsSoftwareRenderer(renderer), $"'{renderer}' on {candidate}");
    }

    /// <summary>Whether a <c>GL_RENDERER</c> names a software renderer.</summary>
    internal static bool IsSoftwareRenderer(string renderer)
    {
        foreach (var name in SoftwareRenderers)
        {
            if (renderer.Contains(name, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether a render node's kernel driver is a virtual one, which renders nothing on a GPU.</summary>
    internal static bool IsVirtualDriver(string driver) => VirtualDrivers.Contains(driver, StringComparer.Ordinal);

    /// <summary>
    /// The first render node this user can open whose driver is not virtual, with its driver's name, or null and why
    /// none counts.
    /// </summary>
    private static (string? Candidate, string Nodes) RenderNodes()
    {
        if (!Directory.Exists("/dev/dri"))
        {
            return (null, "no /dev/dri");
        }

        var seen = new List<string>();
        foreach (var node in Directory.EnumerateFiles("/dev/dri", "renderD*").Order(StringComparer.Ordinal))
        {
            var name = Path.GetFileName(node);
            try
            {
                using var handle = File.OpenHandle(node, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
                var driver = DriverOf((int)handle.DangerousGetHandle());
                if (driver is not null && IsVirtualDriver(driver))
                {
                    seen.Add($"{name} {driver}");
                    continue;
                }

                return ($"{name} {driver ?? "(driver unknown)"}", "");
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                seen.Add($"{name} not openable");
            }
        }

        return (null, seen.Count == 0 ? "no render node" : string.Join(", ", seen));
    }

    /// <summary>The kernel driver's name from libdrm, or null without libdrm.</summary>
    private static unsafe string? DriverOf(int descriptor)
    {
        if (!NativeLibrary.TryLoad(Egl.DrmLibrary, out _))
        {
            return null;
        }

        var version = Egl.drmGetVersion(descriptor);
        if (version == 0)
        {
            return null;
        }

        try
        {
            return Marshal.PtrToStringUTF8(*(nint*)(version + 16));
        }
        finally
        {
            Egl.drmFreeVersion(version);
        }
    }

    /// <summary>
    /// <c>GL_RENDERER</c> of a GLES 2 context on a surfaceless EGL display, torn down again; or null and why not. Any
    /// failure counts as software (the spike's recommendation): a GPU that cannot even do this is no help to WebKit.
    /// </summary>
    internal static unsafe (string? Renderer, string Failure) Renderer()
    {
        if (!NativeLibrary.TryLoad(Egl.Library, out _))
        {
            return (null, "no libEGL.so.1");
        }

        var clientExtensions = Marshal.PtrToStringUTF8(Egl.eglQueryString(0, Egl.Extensions)) ?? "";
        var getPlatformDisplay = (delegate* unmanaged<uint, nint, int*, nint>)Egl.eglGetProcAddress("eglGetPlatformDisplayEXT");
        if (!clientExtensions.Contains("EGL_MESA_platform_surfaceless", StringComparison.Ordinal) || getPlatformDisplay == null)
        {
            return (null, "no surfaceless platform");
        }

        var display = getPlatformDisplay(Egl.PlatformSurfaceless, 0, null);
        int major;
        int minor;
        if (display == 0 || Egl.eglInitialize(display, &major, &minor) == 0)
        {
            return (null, $"no display (0x{Egl.eglGetError():X4})");
        }

        var context = (nint)0;
        try
        {
            Egl.eglBindAPI(Egl.OpenGlEsApi);

            // Mesa's surfaceless display offers a context without a configuration (EGL_KHR_no_config_context), and
            // may offer no configuration at all; elsewhere, a pbuffer configuration for GLES 2.
            var displayExtensions = Marshal.PtrToStringUTF8(Egl.eglQueryString(display, Egl.Extensions)) ?? "";
            nint config = 0;
            if (!displayExtensions.Contains("EGL_KHR_no_config_context", StringComparison.Ordinal))
            {
                var configAttributes = stackalloc int[] { Egl.SurfaceType, Egl.PbufferBit, Egl.RenderableType, Egl.OpenGlEs2Bit, Egl.None };
                int count;
                if (Egl.eglChooseConfig(display, configAttributes, &config, 1, &count) == 0 || count < 1)
                {
                    return (null, $"no GLES 2 configuration (0x{Egl.eglGetError():X4})");
                }
            }

            var contextAttributes = stackalloc int[] { Egl.ContextClientVersion, 2, Egl.None };
            context = Egl.eglCreateContext(display, config, 0, contextAttributes);
            if (context == 0 || Egl.eglMakeCurrent(display, 0, 0, context) == 0)
            {
                return (null, $"no context (0x{Egl.eglGetError():X4})");
            }

            var getString = (delegate* unmanaged<uint, nint>)Egl.eglGetProcAddress("glGetString");
            var renderer = getString == null ? null : Marshal.PtrToStringUTF8(getString(Egl.GlRenderer));
            return renderer is null ? (null, "no renderer string") : (renderer, "");
        }
        finally
        {
            Egl.eglMakeCurrent(display, 0, 0, 0);
            if (context != 0)
            {
                Egl.eglDestroyContext(display, context);
            }

            Egl.eglTerminate(display);
            Egl.eglReleaseThread();
        }
    }

    /// <summary>Sets both variables where the user did not, and says what was done.</summary>
    private static string Set(string? userDmabuf, string? userRenderer)
    {
        Libc.SetEnv(DisableDmabufVariable, "1", overwrite: 0);
        Libc.SetEnv(RendererVariable, "cairo", overwrite: 0);
        var dmabuf = userDmabuf is null ? $"{DisableDmabufVariable}=1" : $"the user's {DisableDmabufVariable}={userDmabuf} kept";
        var renderer = userRenderer is null ? $"{RendererVariable}=cairo" : $"the user's {RendererVariable}={userRenderer} kept";
        return $"{dmabuf}, {renderer}";
    }

    /// <summary>The process's threads now, for the log: <c>setenv</c> is only safe while nothing else reads the environment.</summary>
    private static int Threads()
    {
        try
        {
            return Directory.GetDirectories("/proc/self/task").Length;
        }
        catch (IOException)
        {
            return -1;
        }
    }
}
