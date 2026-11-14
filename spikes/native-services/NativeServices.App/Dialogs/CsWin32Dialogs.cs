#if PART_CSWIN32_DIALOGS
using System.Diagnostics;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Shell = Windows.Win32.UI.Shell;
using Windows.Win32.UI.Shell.Common;

namespace NativeServices.App.Dialogs;

/// <summary>
/// The same Open dialog through CsWin32's allowMarshaling=false projection: COM interfaces are unmanaged structs with
/// function-pointer vtables, reference counting is manual, and failing HRESULTs throw (Show's cancel included).
/// No event sink: implementing IFileDialogEvents in this mode means hand-building a vtable, so the watchdog drives it.
/// </summary>
internal static unsafe class CsWin32Dialogs
{
    public static (int HResult, string[] Paths, DialogObservation Observation, double ShowMs) ShowOpen(nint owner,
        uint threadId, string title, string folder, string fileName, DialogAction action)
    {
        Shell.IFileOpenDialog* dialog = null;
        Shell.IShellItem* folderItem = null;
        Shell.IShellItemArray* results = null;
        try
        {
            Shell.FileOpenDialog.CreateInstance(out dialog).ThrowOnFailure();

            dialog->GetOptions(out var options);
            dialog->SetOptions(options | Shell.FILEOPENDIALOGOPTIONS.FOS_FORCEFILESYSTEM |
                Shell.FILEOPENDIALOGOPTIONS.FOS_DONTADDTORECENT | Shell.FILEOPENDIALOGOPTIONS.FOS_NOCHANGEDIR |
                Shell.FILEOPENDIALOGOPTIONS.FOS_FILEMUSTEXIST);
            fixed (char* titlePointer = title)
            fixed (char* namePointer = fileName)
            fixed (char* filterName = "Spar recipes (*.sparrecipe)")
            fixed (char* filterSpec = "*.sparrecipe")
            {
                dialog->SetTitle(titlePointer);
                dialog->SetFileName(namePointer);
                var spec = new COMDLG_FILTERSPEC { pszName = filterName, pszSpec = filterSpec };
                dialog->SetFileTypes(1, &spec);
            }

            PInvoke.SHCreateItemFromParsingName(folder, null, out folderItem).ThrowOnFailure();
            dialog->SetFolder(folderItem);

            var observation = new DialogObservation();
            var start = Stopwatch.GetTimestamp();
            var hr = 0;
            using (new DialogWatchdog(title, owner, threadId, action, TimeSpan.FromSeconds(5), observation, start))
            {
                try
                {
                    dialog->Show((HWND)owner);
                }
                catch (COMException e)
                {
                    hr = e.HResult; // cancel arrives as an exception in this projection
                }
            }

            var showMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            if (hr != 0)
            {
                return (hr, [], observation, showMs);
            }

            dialog->GetResults(&results);
            results->GetCount(out var count);
            var paths = new string[count];
            for (var i = 0u; i < count; i++)
            {
                Shell.IShellItem* item = null;
                results->GetItemAt(i, &item);
                try
                {
                    item->GetDisplayName(Shell.SIGDN.SIGDN_FILESYSPATH, out var name);
                    paths[i] = new string(name.Value);
                    PInvoke.CoTaskMemFree(name.Value);
                }
                finally
                {
                    item->Release();
                }
            }

            return (0, paths, observation, showMs);
        }
        finally
        {
            if (results is not null)
            {
                results->Release();
            }

            if (folderItem is not null)
            {
                folderItem->Release();
            }

            if (dialog is not null)
            {
                dialog->Release();
            }
        }
    }
}
#endif
