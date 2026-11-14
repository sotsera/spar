#if PART_DIALOGS
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace NativeServices.App.Dialogs;

internal sealed record FileFilter(string Name, string Pattern);

internal enum FileDialogKind
{
    Open,
    Save,
}

/// <summary>A dialog request. A Spar backend would build this from a page call plus app-registered defaults.</summary>
internal sealed class FileDialogRequest
{
    public required FileDialogKind Kind { get; init; }
    public required string Title { get; init; }
    public IReadOnlyList<FileFilter> Filters { get; init; } = [];
    public string? DefaultFolder { get; init; } // used when the dialog has no remembered folder for ClientGuid
    public string? Folder { get; init; }        // always used (SetFolder)
    public string? FileName { get; init; }
    public string? DefaultExtension { get; init; }
    public bool Multiple { get; init; }
    public Guid ClientGuid { get; init; }
}

/// <summary>Test-only automation of a shown dialog. Production code passes null.</summary>
internal sealed class DialogScript
{
    public DialogAction Action { get; init; }
    public string? FileName { get; init; }
    public int CloseHResult { get; init; } = ShellConstants.HResultCancelled;
    public bool AcceptOverwrite { get; init; }
    public TimeSpan Deadline { get; init; } = TimeSpan.FromSeconds(5);
}

internal sealed class FileDialogResult
{
    public int HResult { get; init; }
    public bool Cancelled => HResult == ShellConstants.HResultCancelled;
    public IReadOnlyList<string> Paths { get; init; } = [];
    public DialogObservation Observation { get; init; } = new();
    public double ShowMs { get; init; }

    public override string ToString() =>
        $"hr=0x{HResult:X8} cancelled={Cancelled} paths=[{string.Join(" | ", Paths)}] show={ShowMs:F1}ms {Observation}";
}

/// <summary>Common Item Dialog through hand-declared [GeneratedComInterface] COM. Must run on an STA thread.</summary>
internal static unsafe class FileDialogs
{
    public static FileDialogResult Show(nint owner, uint threadId, FileDialogRequest request, DialogScript? script)
    {
        if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
        {
            throw new InvalidOperationException("The Common Item Dialog must be shown from an STA thread.");
        }

        var dialog = Create(request.Kind);
        try
        {
            Configure(dialog, request);

            var observation = new DialogObservation();
            var start = Stopwatch.GetTimestamp();
            var events = new DialogEvents(dialog, owner, script, observation, start);
            var cookie = dialog.Advise(events);
            int hr;
            using (new DialogWatchdog(request.Title, owner, threadId, DialogAction.None, script?.Deadline ?? TimeSpan.FromSeconds(5),
                observation, start))
            {
                hr = dialog.Show(owner);
            }

            var showMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            dialog.Unadvise(cookie);

            var paths = hr == ShellConstants.SOk ? ReadResults(dialog, request.Kind) : [];
            if (request.ClientGuid != Guid.Empty)
            {
                _ = dialog.ClearClientData(); // drop the remembered folder and size for this spike's client GUID
            }

            return new FileDialogResult { HResult = hr, Paths = paths, Observation = observation, ShowMs = showMs };
        }
        finally
        {
            ShellCom.Release(dialog);
        }
    }

    private static IFileDialog Create(FileDialogKind kind)
    {
        if (kind == FileDialogKind.Open)
        {
            return ShellCom.Create<IFileOpenDialog>(ShellConstants.ClsidFileOpenDialog, ShellConstants.IidFileOpenDialog);
        }

#if SPIKE_SAVE_AS_DERIVED
        // Repro: under NativeAOT, wrapping as IFileSaveDialog while none of its own methods is called fails at the cast
        // (IIUnknownDerivedDetails lookup: "Operation is not valid due to the current state of the object"), with no
        // build warning. A reachable call to one of its methods (SPIKE_SAVE_METHOD_ROOT) makes the cast work.
        var save = ShellCom.Create<IFileSaveDialog>(ShellConstants.ClsidFileSaveDialog, ShellConstants.IidFileSaveDialog);
#if SPIKE_SAVE_METHOD_ROOT
        if (Environment.GetEnvironmentVariable("SPIKE_NEVER_SET") == "x")
        {
            save.SetSaveAsItem(null!);
        }
#endif
        return save;
#else
        // Only IFileDialog methods are needed for saving, so ask for that interface.
        return ShellCom.Create<IFileDialog>(ShellConstants.ClsidFileSaveDialog, ShellConstants.IidFileDialog);
#endif
    }

    private static void Configure(IFileDialog dialog, FileDialogRequest request)
    {
        if (request.ClientGuid != Guid.Empty)
        {
            dialog.SetClientGuid(request.ClientGuid);
            _ = dialog.ClearClientData();
        }

        var options = dialog.GetOptions() | ShellConstants.FosForceFileSystem | ShellConstants.FosDontAddToRecent |
            ShellConstants.FosNoChangeDir;
        options |= request.Kind == FileDialogKind.Open
            ? ShellConstants.FosFileMustExist | ShellConstants.FosPathMustExist
            : ShellConstants.FosOverwritePrompt | ShellConstants.FosPathMustExist;
        if (request.Multiple)
        {
            options |= ShellConstants.FosAllowMultiSelect;
        }

        dialog.SetOptions(options);
        dialog.SetTitle(request.Title);
        SetFilters(dialog, request.Filters);
        if (request.DefaultExtension is not null)
        {
            dialog.SetDefaultExtension(request.DefaultExtension);
        }

        if (request.DefaultFolder is not null)
        {
            var folder = ShellCom.ItemFromPath(request.DefaultFolder);
            try
            {
                dialog.SetDefaultFolder(folder);
            }
            finally
            {
                ShellCom.Release(folder);
            }
        }

        if (request.Folder is not null)
        {
            var folder = ShellCom.ItemFromPath(request.Folder);
            try
            {
                dialog.SetFolder(folder);
            }
            finally
            {
                ShellCom.Release(folder);
            }
        }

        if (request.FileName is not null)
        {
            dialog.SetFileName(request.FileName);
        }
    }

    private static void SetFilters(IFileDialog dialog, IReadOnlyList<FileFilter> filters)
    {
        if (filters.Count == 0)
        {
            return;
        }

        // The dialog copies the strings during SetFileTypes, so they only need to live for the call.
        var specs = stackalloc ComdlgFilterSpec[filters.Count];
        var allocated = new nint[filters.Count * 2];
        try
        {
            for (var i = 0; i < filters.Count; i++)
            {
                allocated[i * 2] = Marshal.StringToCoTaskMemUni(filters[i].Name);
                allocated[i * 2 + 1] = Marshal.StringToCoTaskMemUni(filters[i].Pattern);
                specs[i] = new ComdlgFilterSpec { Name = allocated[i * 2], Spec = allocated[i * 2 + 1] };
            }

            dialog.SetFileTypes((uint)filters.Count, specs);
            dialog.SetFileTypeIndex(1);
        }
        finally
        {
            foreach (var pointer in allocated)
            {
                Marshal.FreeCoTaskMem(pointer);
            }
        }
    }

    private static string[] ReadResults(IFileDialog dialog, FileDialogKind kind)
    {
        if (kind == FileDialogKind.Save)
        {
            Marshal.ThrowExceptionForHR(dialog.GetResult(out var item));
            return [ShellCom.FileSystemPath(item)];
        }

        Marshal.ThrowExceptionForHR(((IFileOpenDialog)dialog).GetResults(out var arrayPointer));
        var array = ShellCom.Wrap<IShellItemArray>(arrayPointer);
        try
        {
            var count = array.GetCount();
            var paths = new string[count];
            for (var i = 0u; i < count; i++)
            {
                Marshal.ThrowExceptionForHR(array.GetItemAt(i, out var item));
                paths[i] = ShellCom.FileSystemPath(item);
            }

            return paths;
        }
        finally
        {
            ShellCom.Release(array);
        }
    }
}

/// <summary>
/// Event sink. The first OnFolderChange arrives once the dialog window exists; the script acts there. All callbacks
/// run on the dialog's STA thread inside Show's modal loop.
/// </summary>
[GeneratedComClass]
internal sealed partial class DialogEvents(IFileDialog dialog, nint owner, DialogScript? script,
    DialogObservation observation, long start) : IFileDialogEvents
{
    private bool _shown;

    public int OnFileOk(nint dialogPointer) => Record("FileOk");

    public int OnFolderChanging(nint dialogPointer, nint folder) => Record("FolderChanging");

    public int OnFolderChange(nint dialogPointer)
    {
        Record("FolderChange");
        if (_shown)
        {
            return ShellConstants.SOk;
        }

        _shown = true;
        try
        {
            var window = ((IOleWindow)dialog).GetWindow();
            DialogWindows.Observe(window, owner, observation, start);
            switch (script?.Action)
            {
                case DialogAction.Accept:
                    if (script.FileName is not null)
                    {
                        dialog.SetFileName(script.FileName);
                    }

                    DialogWindows.PostCommand(window, DialogWindows.IdOk);
                    break;
                case DialogAction.Cancel:
                    DialogWindows.PostCommand(window, DialogWindows.IdCancel);
                    break;
                case DialogAction.CloseWithHResult:
                    observation.AddEvent($"Close=0x{dialog.Close(script.CloseHResult):X8}");
                    break;
            }
        }
        catch (Exception e)
        {
            observation.AddEvent($"error:{e.GetType().Name}:0x{e.HResult:X8}");
        }

        return ShellConstants.SOk;
    }

    public int OnSelectionChange(nint dialogPointer) => Record("SelectionChange");

    public int OnShareViolation(nint dialogPointer, nint item, out int response)
    {
        response = 0;
        return Record("ShareViolation");
    }

    public int OnTypeChange(nint dialogPointer) => Record("TypeChange");

    public int OnOverwrite(nint dialogPointer, nint item, out int response)
    {
        Record("Overwrite");
        response = script?.AcceptOverwrite == true ? ShellConstants.FdeorAccept : 0;
        return ShellConstants.SOk;
    }

    private int Record(string name)
    {
        observation.AddEvent(name);
        return ShellConstants.SOk;
    }
}
#endif
