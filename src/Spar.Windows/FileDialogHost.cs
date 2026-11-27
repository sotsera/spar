using System.Runtime.InteropServices;
using Spar.Files;
using Spar.Hosting;
using Spar.Windows.Interop;

namespace Spar.Windows;

/// <summary>
/// The window's file dialogs (plan section 3, "Native services"; <c>docs/app-model.md</c> 3.6): the common item dialogs
/// (<c>IFileOpenDialog</c>, <c>IFileSaveDialog</c>), created on the window's STA and shown owned by it. <c>Show</c> runs
/// the dialog's own modal loop on the STA, which keeps dispatching the thread's messages and posted work, so the
/// window and the bridge stay live meanwhile. Only file-system paths come back (<c>FOS_FORCEFILESYSTEM</c>). A
/// cancelled call, or the window closing, closes the open dialog as if the user had (<c>IFileDialog::Close</c>).
/// </summary>
internal sealed class FileDialogHost(UiThread ui, Func<nint> owner, HostLog log) : INativeFileDialogs
{
    private IFileDialog? _open;

    public async Task<Result<IReadOnlyList<string>, FileDialogError>> ShowAsync(FileDialogRequest request, CancellationToken cancellationToken)
    {
        var done = new TaskCompletionSource<Result<IReadOnlyList<string>, FileDialogError>>(TaskCreationOptions.RunContinuationsAsynchronously);
        ui.Post(() => done.TrySetResult(Show(request)), "file dialog");
        await using var cancel = cancellationToken.Register(() => ui.Post(CloseOpen, "file dialog cancelled"));
        return await done.Task;
    }

    /// <summary>Closes the open dialog, if any, as a dismissal (UI thread); the window is closing, or its call was cancelled.</summary>
    public void CloseOpen() => _open?.Close(Shell.Cancelled);

    private Result<IReadOnlyList<string>, FileDialogError> Show(FileDialogRequest request)
    {
        var save = request.Kind == FileDialogKind.Save;
        var created = ShellNative.CoCreateInstance(
            save ? Shell.FileSaveDialog : Shell.FileOpenDialog,
            0,
            Shell.ClsctxInprocServer,
            Shell.IidFileDialog,
            out var pointer);
        if (created < 0)
        {
            return Unavailable("could not be created", created);
        }

        var dialog = Com.Take<IFileDialog>(pointer);
        try
        {
            var prepared = Prepare(dialog, request);
            if (prepared < 0)
            {
                return Unavailable("could not be prepared", prepared);
            }

            _open = dialog;
            var shown = dialog.Show(owner());
            _open = null;
            if (shown == Shell.Cancelled)
            {
                return new FileDialogError.Dismissed();
            }

            if (shown < 0)
            {
                return Unavailable("failed", shown);
            }

            return request.Kind == FileDialogKind.OpenMany ? Results((IFileOpenDialog)dialog) : Result(dialog);
        }
        finally
        {
            _open = null;
            Com.Release(dialog);
        }
    }

    private static int Prepare(IFileDialog dialog, FileDialogRequest request)
    {
        var hr = dialog.GetOptions(out var options);
        if (hr < 0)
        {
            return hr;
        }

        options |= Shell.ForceFileSystem | Shell.NoChangeDir | Shell.PathMustExist;
        options |= request.Kind switch
        {
            FileDialogKind.Open => Shell.FileMustExist,
            FileDialogKind.OpenMany => Shell.FileMustExist | Shell.AllowMultiSelect,
            FileDialogKind.Save => Shell.OverwritePrompt,
            _ => Shell.PickFolders,
        };
        hr = dialog.SetOptions(options);
        if (hr >= 0 && request.Title is { } title)
        {
            hr = dialog.SetTitle(title);
        }

        if (hr >= 0 && request.Kind != FileDialogKind.Folder && request.Filters.Count > 0)
        {
            hr = SetFilters(dialog, request.Filters);
        }

        if (hr >= 0 && request.Kind == FileDialogKind.Save)
        {
            hr = PrepareSave(dialog, request);
        }

        return hr;
    }

    /// <summary>The filters, with the patterns as Windows writes them (<c>*.md;*.markdown</c>); the strings live through the call.</summary>
    private static unsafe int SetFilters(IFileDialog dialog, IReadOnlyList<FileFilter> filters)
    {
        var strings = new List<nint>();
        var specs = new FilterSpec[filters.Count];
        try
        {
            for (var i = 0; i < filters.Count; i++)
            {
                var name = Marshal.StringToCoTaskMemUni(filters[i].Name);
                var spec = Marshal.StringToCoTaskMemUni(filters[i].Pattern);
                strings.Add(name);
                strings.Add(spec);
                specs[i] = new FilterSpec { Name = name, Spec = spec };
            }

            fixed (FilterSpec* first = specs)
            {
                var hr = dialog.SetFileTypes((uint)specs.Length, first);
                return hr < 0 ? hr : dialog.SetFileTypeIndex(1);
            }
        }
        finally
        {
            strings.ForEach(Marshal.FreeCoTaskMem);
        }
    }

    /// <summary>The suggested name, and the first filter's extension added to a name typed without one.</summary>
    private static int PrepareSave(IFileDialog dialog, FileDialogRequest request)
    {
        var hr = 0;
        if (request.SuggestedName is { } name)
        {
            hr = dialog.SetFileName(name);
        }

        var extension = request.Filters.Count > 0 ? FirstExtension(request.Filters[0].Pattern) : null;
        return hr >= 0 && extension is not null ? dialog.SetDefaultExtension(extension) : hr;
    }

    private static string? FirstExtension(string pattern)
    {
        var first = pattern.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
        return first is not null && first.StartsWith("*.", StringComparison.Ordinal) && first.IndexOf('*', 2) < 0 ? first[2..] : null;
    }

    private Result<IReadOnlyList<string>, FileDialogError> Result(IFileDialog dialog)
    {
        var hr = dialog.GetResult(out var item);
        if (hr < 0)
        {
            return Unavailable("gave no result", hr);
        }

        return PathOf(item) is { } path ? new[] { path } : Unavailable("gave an item without a path", hr);
    }

    private Result<IReadOnlyList<string>, FileDialogError> Results(IFileOpenDialog dialog)
    {
        var hr = dialog.GetResults(out var pointer);
        if (hr < 0)
        {
            return Unavailable("gave no results", hr);
        }

        var items = Com.Take<IShellItemArray>(pointer);
        try
        {
            hr = items.GetCount(out var count);
            if (hr < 0)
            {
                return Unavailable("gave no results", hr);
            }

            var paths = new List<string>((int)count);
            for (var i = 0u; i < count; i++)
            {
                if (items.GetItemAt(i, out var item) < 0 || PathOf(item) is not { } path)
                {
                    return Unavailable("gave an item without a path", hr);
                }

                paths.Add(path);
            }

            return paths;
        }
        finally
        {
            Com.Release(items);
        }
    }

    /// <summary>The item's file-system path; the item's reference is released.</summary>
    private static unsafe string? PathOf(nint pointer)
    {
        var item = Com.Take<IShellItem>(pointer);
        try
        {
            if (item.GetDisplayName(Shell.FileSystemPath, out var name) < 0)
            {
                return null;
            }

            try
            {
                return new string((char*)name);
            }
            finally
            {
                Marshal.FreeCoTaskMem(name);
            }
        }
        finally
        {
            Com.Release(item);
        }
    }

    private FileDialogError Unavailable(string what, int hr)
    {
        var reason = $"The file dialog {what} (0x{hr:X8}).";
        log.Warn(reason);
        return new FileDialogError.Unavailable(reason);
    }
}
