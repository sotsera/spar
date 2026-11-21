using System.Runtime.InteropServices;

namespace Spar.Linux.Interop;

/// <summary>
/// EGL 1.4 (libglvnd's <c>libEGL.so.1</c>) and libdrm, for the software-GPU check before GTK starts (checked against
/// Mesa's <c>EGL/egl.h</c>, <c>eglext.h</c> and libdrm's <c>xf86drm.h</c> on Ubuntu 24.04): <c>EGLint</c> is 32-bit,
/// <c>EGLBoolean</c> and <c>EGLenum</c> unsigned 32-bit, displays, configs and contexts pointers. Entry points newer
/// than 1.4 and <c>glGetString</c> come from <see cref="eglGetProcAddress"/>.
/// </summary>
internal static unsafe partial class Egl
{
    public const int None = 0x3038;
    public const int Extensions = 0x3055;
    public const int SurfaceType = 0x3033;
    public const int PbufferBit = 0x0001;
    public const int RenderableType = 0x3040;
    public const int OpenGlEs2Bit = 0x0004;
    public const int ContextClientVersion = 0x3098;
    public const uint OpenGlEsApi = 0x30A0;

    /// <summary><c>EGL_PLATFORM_SURFACELESS_MESA</c>: a display with no window system, for <c>eglGetPlatformDisplayEXT</c>.</summary>
    public const uint PlatformSurfaceless = 0x31DD;

    /// <summary><c>GL_RENDERER</c>.</summary>
    public const uint GlRenderer = 0x1F01;

    public const string Library = "libEGL.so.1";
    public const string DrmLibrary = "libdrm.so.2";

    /// <summary>A function of EGL or of the current client API (<c>EGL_KHR_get_all_proc_addresses</c>), or 0.</summary>
    [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)]
    public static partial nint eglGetProcAddress(string name);

    /// <summary>Borrowed; the client extensions for <paramref name="display"/> 0 (<c>EGL_NO_DISPLAY</c>).</summary>
    [LibraryImport(Library)]
    public static partial nint eglQueryString(nint display, int name);

    [LibraryImport(Library)]
    public static partial uint eglInitialize(nint display, int* major, int* minor);

    /// <summary>Releases the display's resources once no thread has a context current on it.</summary>
    [LibraryImport(Library)]
    public static partial uint eglTerminate(nint display);

    [LibraryImport(Library)]
    public static partial uint eglBindAPI(uint api);

    [LibraryImport(Library)]
    public static partial uint eglChooseConfig(nint display, int* attributes, nint* configs, int size, int* count);

    [LibraryImport(Library)]
    public static partial nint eglCreateContext(nint display, nint config, nint shareContext, int* attributes);

    [LibraryImport(Library)]
    public static partial uint eglDestroyContext(nint display, nint context);

    /// <summary>With no surfaces (<c>EGL_KHR_surfaceless_context</c>) to read strings only; all zero releases it.</summary>
    [LibraryImport(Library)]
    public static partial uint eglMakeCurrent(nint display, nint draw, nint read, nint context);

    /// <summary>Frees the calling thread's EGL state, so the check leaves nothing behind on the main thread.</summary>
    [LibraryImport(Library)]
    public static partial uint eglReleaseThread();

    [LibraryImport(Library)]
    public static partial int eglGetError();

    /// <summary>
    /// A <c>drmVersion</c> of the device behind <paramref name="descriptor"/>; owned, <see cref="drmFreeVersion"/>. Its
    /// <c>name</c> (the kernel driver) is the pointer at offset 16 on LP64.
    /// </summary>
    [LibraryImport(DrmLibrary)]
    public static partial nint drmGetVersion(int descriptor);

    [LibraryImport(DrmLibrary)]
    public static partial void drmFreeVersion(nint version);
}
