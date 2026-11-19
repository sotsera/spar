using System.Runtime.InteropServices;

namespace Spar.Spikes.GpuProbe;

/// <summary>
/// libEGL (glvnd) imports. EGL 1.4 core through LibraryImport; EGL 1.5 and extension entry points through
/// eglGetProcAddress. Types follow eglplatform.h on Linux x64: EGLint is int32, EGLBoolean and EGLenum are uint32,
/// EGLAttrib is pointer-sized, handles are pointers.
/// </summary>
internal static unsafe partial class EglNative
{
    private const string Lib = "libEGL.so.1";

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial nint eglGetProcAddress(string name);

    [LibraryImport(Lib)]
    public static partial nint eglQueryString(nint display, int name);

    [LibraryImport(Lib)]
    public static partial nint eglGetDisplay(nint nativeDisplay);

    [LibraryImport(Lib)]
    public static partial uint eglInitialize(nint display, int* major, int* minor);

    [LibraryImport(Lib)]
    public static partial uint eglTerminate(nint display);

    [LibraryImport(Lib)]
    public static partial uint eglBindAPI(uint api);

    [LibraryImport(Lib)]
    public static partial uint eglChooseConfig(nint display, int* attribs, nint* configs, int size, int* count);

    [LibraryImport(Lib)]
    public static partial nint eglCreateContext(nint display, nint config, nint share, int* attribs);

    [LibraryImport(Lib)]
    public static partial uint eglDestroyContext(nint display, nint context);

    [LibraryImport(Lib)]
    public static partial uint eglMakeCurrent(nint display, nint draw, nint read, nint context);

    [LibraryImport(Lib)]
    public static partial uint eglReleaseThread();

    [LibraryImport(Lib)]
    public static partial int eglGetError();

    public const int EGL_SUCCESS = 0x3000;
    public const int EGL_VENDOR = 0x3053;
    public const int EGL_VERSION = 0x3054;
    public const int EGL_EXTENSIONS = 0x3055;
    public const int EGL_NONE = 0x3038;
    public const int EGL_SURFACE_TYPE = 0x3033;
    public const int EGL_PBUFFER_BIT = 0x0001;
    public const int EGL_RENDERABLE_TYPE = 0x3040;
    public const int EGL_OPENGL_ES2_BIT = 0x0004;
    public const int EGL_CONTEXT_MAJOR_VERSION = 0x3098;
    public const uint EGL_OPENGL_ES_API = 0x30A0;
    public const uint EGL_PLATFORM_SURFACELESS_MESA = 0x31DD;
    public const uint EGL_PLATFORM_DEVICE_EXT = 0x313F;
    public const int EGL_DRM_DEVICE_FILE_EXT = 0x3233;
    public const int EGL_DRM_RENDER_NODE_FILE_EXT = 0x3377;
    public const uint GL_VENDOR = 0x1F00;
    public const uint GL_RENDERER = 0x1F01;
    public const uint GL_VERSION = 0x1F02;

    public static string? Str(nint p) => p == 0 ? null : Marshal.PtrToStringUTF8(p);
}

internal sealed class EglResult
{
    public string Platform = "";
    public bool LibraryLoaded;
    public string? ClientExtensions;
    public bool Initialized;
    public string? EglVersion;
    public string? EglVendor;
    public string? DriverName;
    public string? GlVendor;
    public string? GlRenderer;
    public string? GlVersion;
    public string? Error;
    public readonly List<(string Name, double Ms)> Phases = [];
    public readonly List<string> Devices = [];
}

/// <summary>
/// Opens a throwaway EGL display, reads what it says about the driver, optionally creates a surfaceless GLES context to
/// read GL_RENDERER and GL_VENDOR, then tears everything down. Nothing is shown and no window system is needed.
/// </summary>
internal static unsafe class EglProbe
{
    public static EglResult Run(string platform, bool context, Phase phase)
    {
        var r = new EglResult { Platform = platform };
        try
        {
            if (!NativeLibrary.TryLoad("libEGL.so.1", out _))
            {
                r.Error = "libEGL.so.1 not found";
                return r;
            }
            r.LibraryLoaded = true;
            phase.Mark(r.Phases, "load");

            r.ClientExtensions = EglNative.Str(EglNative.eglQueryString(0, EglNative.EGL_EXTENSIONS));
            nint display = GetDisplay(platform, r);
            phase.Mark(r.Phases, "get-display");
            if (display == 0)
            {
                r.Error ??= $"no display (0x{EglNative.eglGetError():X4})";
                return r;
            }

            int major, minor;
            if (EglNative.eglInitialize(display, &major, &minor) == 0)
            {
                r.Error = $"eglInitialize failed (0x{EglNative.eglGetError():X4})";
                return r;
            }
            r.Initialized = true;
            phase.Mark(r.Phases, "initialize");

            r.EglVersion = EglNative.Str(EglNative.eglQueryString(display, EglNative.EGL_VERSION));
            r.EglVendor = EglNative.Str(EglNative.eglQueryString(display, EglNative.EGL_VENDOR));
            var displayExtensions = EglNative.Str(EglNative.eglQueryString(display, EglNative.EGL_EXTENSIONS)) ?? "";
            if (HasExtension(displayExtensions, "EGL_MESA_query_driver"))
            {
                var getDriverName = (delegate* unmanaged<nint, nint>)EglNative.eglGetProcAddress("eglGetDisplayDriverName");
                if (getDriverName != null)
                {
                    r.DriverName = EglNative.Str(getDriverName(display));
                }
            }
            phase.Mark(r.Phases, "query-driver");

            if (context)
            {
                ReadGlStrings(display, displayExtensions, r);
                phase.Mark(r.Phases, "context-and-gl-strings");
            }

            EglNative.eglTerminate(display);
            EglNative.eglReleaseThread();
            phase.Mark(r.Phases, "teardown");
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            r.Error = ex.GetType().Name + ": " + ex.Message;
        }
        return r;
    }

    /// <summary>Lists EGL devices (EGL_EXT_device_enumeration) without opening a display.</summary>
    public static EglResult Devices(Phase phase)
    {
        var r = new EglResult { Platform = "devices" };
        if (!NativeLibrary.TryLoad("libEGL.so.1", out _))
        {
            r.Error = "libEGL.so.1 not found";
            return r;
        }
        r.LibraryLoaded = true;
        phase.Mark(r.Phases, "load");
        r.ClientExtensions = EglNative.Str(EglNative.eglQueryString(0, EglNative.EGL_EXTENSIONS));
        var query = (delegate* unmanaged<int, nint*, int*, uint>)EglNative.eglGetProcAddress("eglQueryDevicesEXT");
        var queryString = (delegate* unmanaged<nint, int, nint>)EglNative.eglGetProcAddress("eglQueryDeviceStringEXT");
        if (query == null || queryString == null)
        {
            r.Error = "no EGL_EXT_device_enumeration";
            return r;
        }
        var devices = stackalloc nint[16];
        int count;
        if (query(16, devices, &count) == 0)
        {
            r.Error = $"eglQueryDevicesEXT failed (0x{EglNative.eglGetError():X4})";
            return r;
        }
        for (var i = 0; i < count; i++)
        {
            var ext = EglNative.Str(queryString(devices[i], EglNative.EGL_EXTENSIONS)) ?? "";
            var file = HasExtension(ext, "EGL_EXT_device_drm") ? EglNative.Str(queryString(devices[i], EglNative.EGL_DRM_DEVICE_FILE_EXT)) : null;
            var render = HasExtension(ext, "EGL_EXT_device_drm_render_node") ? EglNative.Str(queryString(devices[i], EglNative.EGL_DRM_RENDER_NODE_FILE_EXT)) : null;
            var software = HasExtension(ext, "EGL_MESA_device_software");
            r.Devices.Add($"#{i} software={software} drm={file ?? "-"} render={render ?? "-"} ext=[{ext}]");
        }
        phase.Mark(r.Phases, "enumerate");
        return r;
    }

    private static nint GetDisplay(string platform, EglResult r)
    {
        if (platform == "default")
        {
            return EglNative.eglGetDisplay(0);
        }

        uint platformId = platform switch
        {
            "surfaceless" => EglNative.EGL_PLATFORM_SURFACELESS_MESA,
            "device" => EglNative.EGL_PLATFORM_DEVICE_EXT,
            _ => throw new ArgumentException("unknown platform " + platform),
        };
        var ext = r.ClientExtensions ?? "";
        var needed = platform == "surfaceless" ? "EGL_MESA_platform_surfaceless" : "EGL_EXT_platform_device";
        if (!HasExtension(ext, needed))
        {
            r.Error = needed + " not offered";
            return 0;
        }

        nint native = 0;
        if (platform == "device")
        {
            // The first device that is not Mesa's software device, else the first one.
            var query = (delegate* unmanaged<int, nint*, int*, uint>)EglNative.eglGetProcAddress("eglQueryDevicesEXT");
            var queryString = (delegate* unmanaged<nint, int, nint>)EglNative.eglGetProcAddress("eglQueryDeviceStringEXT");
            var devices = stackalloc nint[16];
            int count = 0;
            if (query != null && query(16, devices, &count) != 0 && count > 0)
            {
                native = devices[0];
                for (var i = 0; i < count; i++)
                {
                    var dext = EglNative.Str(queryString(devices[i], EglNative.EGL_EXTENSIONS)) ?? "";
                    if (!HasExtension(dext, "EGL_MESA_device_software"))
                    {
                        native = devices[i];
                        break;
                    }
                }
            }
            if (native == 0)
            {
                r.Error = "no EGL device";
                return 0;
            }
        }

        // EGL 1.5 entry point first, the EXT one as the fallback (it takes EGLint attributes).
        var get15 = (delegate* unmanaged<uint, nint, nint*, nint>)EglNative.eglGetProcAddress("eglGetPlatformDisplay");
        if (get15 != null)
        {
            return get15(platformId, native, null);
        }
        var getExt = (delegate* unmanaged<uint, nint, int*, nint>)EglNative.eglGetProcAddress("eglGetPlatformDisplayEXT");
        return getExt == null ? 0 : getExt(platformId, native, null);
    }

    private static void ReadGlStrings(nint display, string displayExtensions, EglResult r)
    {
        if (EglNative.eglBindAPI(EglNative.EGL_OPENGL_ES_API) == 0)
        {
            r.Error = $"eglBindAPI failed (0x{EglNative.eglGetError():X4})";
            return;
        }

        nint config = 0;
        if (!HasExtension(displayExtensions, "EGL_KHR_no_config_context"))
        {
            var want = stackalloc int[] { EglNative.EGL_SURFACE_TYPE, EglNative.EGL_PBUFFER_BIT, EglNative.EGL_RENDERABLE_TYPE, EglNative.EGL_OPENGL_ES2_BIT, EglNative.EGL_NONE };
            int n;
            if (EglNative.eglChooseConfig(display, want, &config, 1, &n) == 0 || n == 0)
            {
                r.Error = $"no config (0x{EglNative.eglGetError():X4})";
                return;
            }
        }

        var attribs = stackalloc int[] { EglNative.EGL_CONTEXT_MAJOR_VERSION, 2, EglNative.EGL_NONE };
        var ctx = EglNative.eglCreateContext(display, config, 0, attribs);
        if (ctx == 0)
        {
            r.Error = $"eglCreateContext failed (0x{EglNative.eglGetError():X4})";
            return;
        }
        try
        {
            if (!HasExtension(displayExtensions, "EGL_KHR_surfaceless_context") || EglNative.eglMakeCurrent(display, 0, 0, ctx) == 0)
            {
                r.Error = $"surfaceless eglMakeCurrent failed (0x{EglNative.eglGetError():X4})";
                return;
            }
            var getString = (delegate* unmanaged<uint, nint>)EglNative.eglGetProcAddress("glGetString");
            if (getString != null)
            {
                r.GlVendor = EglNative.Str(getString(EglNative.GL_VENDOR));
                r.GlRenderer = EglNative.Str(getString(EglNative.GL_RENDERER));
                r.GlVersion = EglNative.Str(getString(EglNative.GL_VERSION));
            }
            EglNative.eglMakeCurrent(display, 0, 0, 0);
        }
        finally
        {
            EglNative.eglDestroyContext(display, ctx);
        }
    }

    public static bool HasExtension(string list, string name)
    {
        foreach (var part in list.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (part == name)
            {
                return true;
            }
        }
        return false;
    }
}
