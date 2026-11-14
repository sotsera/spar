using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Shell;

namespace CsWin32ComProbe;

/// <summary>
/// CsWin32 0.3.369 build-task mode with allowMarshaling=true and comInterop.useComSourceGenerators=true: the dialog is
/// created, configured and closed from its own OnFolderChange (before it is shown), all through generated COM.
/// </summary>
internal static unsafe class Program
{
    private static int Main()
    {
        // Safety net: never leave a dialog behind if Close does not work.
        var deadline = new Thread(() =>
        {
            Thread.Sleep(5000);
            Console.WriteLine("deadline hit; exiting");
            Environment.Exit(3);
        }) { IsBackground = true };
        deadline.Start();

        var exitCode = 1;
        var thread = new Thread(() => exitCode = Run());
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        return exitCode;
    }

    private static int Run()
    {
        var folder = Path.GetTempPath();
        PInvoke.SHCreateItemFromParsingName<IShellItem>(folder, null, out var item).ThrowOnFailure();
        item.GetDisplayName(SIGDN.SIGDN_FILESYSPATH, out PWSTR name);
        var path = new string(name.Value);
        PInvoke.CoTaskMemFree(name.Value);
        Console.WriteLine($"IShellItem display name: {path}");

        var dialog = FileOpenDialog.CreateInstance<IFileOpenDialog>();
        fixed (char* title = "Spike: CsWin32 COM generators")
        {
            dialog.SetTitle(title);
        }

        var events = new Events();
        dialog.Advise(events, out var cookie);
        var hr = 0;
        try
        {
            dialog.Show(HWND.Null);
        }
        catch (COMException e)
        {
            hr = e.HResult;
        }

        dialog.Unadvise(cookie);
        Console.WriteLine($"Show hr=0x{hr:X8} events={events.Count} closeSeen={events.Closed}");
        return hr == unchecked((int)0x800704C7) && events.Closed ? 0 : 1;
    }
}

[GeneratedComClass]
internal sealed partial class Events : IFileDialogEvents
{
    public int Count { get; private set; }
    public bool Closed { get; private set; }

    public void OnFileOk(IFileDialog pfd) => Count++;

    public void OnFolderChanging(IFileDialog pfd, IShellItem psiFolder) => Count++;

    public void OnFolderChange(IFileDialog pfd)
    {
        Count++;
        if (!Closed)
        {
            Closed = true;
            pfd.Close((HRESULT)unchecked((int)0x800704C7)); // closes before the window is shown
        }
    }

    public void OnSelectionChange(IFileDialog pfd) => Count++;

    public unsafe void OnShareViolation(IFileDialog pfd, IShellItem psi, FDE_SHAREVIOLATION_RESPONSE* pResponse) =>
        Count++;

    public void OnTypeChange(IFileDialog pfd) => Count++;

    public unsafe void OnOverwrite(IFileDialog pfd, IShellItem psi, FDE_OVERWRITE_RESPONSE* pResponse) => Count++;
}
