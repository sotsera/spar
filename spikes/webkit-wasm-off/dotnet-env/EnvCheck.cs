#:property AllowUnsafeBlocks=true
// Does Environment.SetEnvironmentVariable reach native getenv() (which JavaScriptCore and GLib's spawn use)
// on Linux? Run inside the Linux container: tools/dotnet-env.sh
using System.Runtime.InteropServices;

Environment.SetEnvironmentVariable("JSC_useWasm", "false");
Console.WriteLine($"managed Get after SetEnvironmentVariable: {Environment.GetEnvironmentVariable("JSC_useWasm") ?? "(null)"}");
Console.WriteLine($"native getenv after SetEnvironmentVariable: {Libc.Get("JSC_useWasm")}");
Console.WriteLine($"child via libc system(): {Libc.Child("JSC_useWasm")}");

int rc = Libc.setenv("JSC_useWasm", "false", 1);
Console.WriteLine($"libc setenv returned {rc}");
Console.WriteLine($"native getenv after setenv: {Libc.Get("JSC_useWasm")}");
Console.WriteLine($"child via libc system(): {Libc.Child("JSC_useWasm")}");

static partial class Libc
{
    [LibraryImport("libc", StringMarshalling = StringMarshalling.Utf8)]
    public static partial int setenv(string name, string value, int overwrite);

    [LibraryImport("libc", StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint getenv(string name);

    [LibraryImport("libc", StringMarshalling = StringMarshalling.Utf8)]
    private static partial int system(string command);

    public static string Get(string name) => Marshal.PtrToStringUTF8(getenv(name)) ?? "(null)";

    public static string Child(string name)
    {
        int code = system($"test -n \"${name}\"");
        return code == 0 ? "set" : "not set";
    }
}
