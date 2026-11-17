using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Spar.WindowTests;

/// <summary>Top-level window enumeration (to spot native dialogs without touching them) and the UDF lock file.</summary>
internal static unsafe partial class Native
{
    private const uint WmClose = 0x0010;

    [ThreadStatic]
    private static List<TopWindow>? t_found;

    public sealed record TopWindow(nint Handle, int ProcessId, string ClassName, string Title, nint Owner, int OwnerProcessId);

    /// <summary>Visible top-level windows that belong to one of <paramref name="processIds"/>.</summary>
    public static List<TopWindow> VisibleWindowsOf(IReadOnlySet<int> processIds)
    {
        t_found = [];
        EnumWindows(&Collect, 0);
        var all = t_found;
        t_found = null;
        return all.Where(w => processIds.Contains(w.ProcessId) || processIds.Contains(w.OwnerProcessId)).ToList();
    }

    public static bool PostClose(nint window) => PostMessageW(window, WmClose, 0, 0);

    /// <summary>
    /// "absent" (no lock file), "held" (another process has it open: Chromium creates it with share-read only and
    /// delete-on-close), or "free" (it exists and could be opened exclusively).
    /// </summary>
    public static string LockState(string udfPath)
    {
        var path = Path.Combine(udfPath, "EBWebView", "lockfile");
        if (!File.Exists(path))
        {
            return "absent";
        }

        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return "free";
        }
        catch (FileNotFoundException)
        {
            return "absent";
        }
        catch (IOException)
        {
            return "held";
        }
        catch (UnauthorizedAccessException)
        {
            // A delete-pending file answers with access denied.
            return "pending";
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int Collect(nint window, nint parameter)
    {
        if (!IsWindowVisible(window))
        {
            return 1;
        }

        GetWindowThreadProcessId(window, out var processId);
        var className = stackalloc char[256];
        var classLength = GetClassNameW(window, className, 256);
        var title = stackalloc char[256];
        var titleLength = GetWindowTextW(window, title, 256);
        var owner = GetWindow(window, 4);
        uint ownerProcessId = 0;
        if (owner != 0)
        {
            GetWindowThreadProcessId(owner, out ownerProcessId);
        }

        t_found!.Add(new TopWindow(window, (int)processId, new string(className, 0, classLength), new string(title, 0, titleLength), owner, (int)ownerProcessId));
        return 1;
    }

    [LibraryImport("user32")]
    private static partial int EnumWindows(delegate* unmanaged[Stdcall]<nint, nint, int> callback, nint parameter);

    [LibraryImport("user32")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWindowVisible(nint window);

    [LibraryImport("user32")]
    private static partial uint GetWindowThreadProcessId(nint window, out uint processId);

    [LibraryImport("user32")]
    private static partial int GetClassNameW(nint window, char* name, int count);

    [LibraryImport("user32")]
    private static partial int GetWindowTextW(nint window, char* text, int count);

    [LibraryImport("user32")]
    private static partial nint GetWindow(nint window, uint command);

    [LibraryImport("user32")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PostMessageW(nint window, uint message, nint wParam, nint lParam);
}
