#if PART_DIALOGS || PART_CSWIN32_DIALOGS

namespace NativeServices.App.Dialogs;

/// <summary>Automated file dialog scenarios. Every shown dialog is dismissed by the script or the watchdog.</summary>
internal static class DialogChecks
{
    // A fixed client GUID keeps the dialog's remembered state separate from other apps; ClearClientData removes it.
    private static readonly Guid s_clientGuid = new("5b0c3f4e-2d1a-4c55-9a7e-6f3d2b1c0a99");

    public static void Run(string folder)
    {
        File.WriteAllText(Path.Combine(folder, "a.sparrecipe"), "{\"recipe\":\"a\"}");
        File.WriteAllText(Path.Combine(folder, "b.sparrecipe"), "{\"recipe\":\"b\"}");
        File.WriteAllText(Path.Combine(folder, "existing.csv"), "id\n1\n");

#if PART_DIALOGS
        RunHandDeclared(folder);
        RunMta();
#endif
#if PART_CSWIN32_DIALOGS
        RunCsWin32(folder);
#endif
    }

#if PART_DIALOGS
    private static FileFilter[] RecipeFilters =>
        [new("Spar recipes (*.sparrecipe)", "*.sparrecipe"), new("All files (*.*)", "*.*")];

    private static void RunHandDeclared(string folder)
    {
        var expectedA = Path.Combine(folder, "a.sparrecipe");

        var single = Show(new FileDialogRequest
        {
            Kind = FileDialogKind.Open,
            Title = "Spike: open recipe (default folder)",
            Filters = RecipeFilters,
            DefaultFolder = folder,
            ClientGuid = s_clientGuid,
        }, new DialogScript { Action = DialogAction.Accept, FileName = "a.sparrecipe" });
        Report.Check("open: accept via SetFileName + IDOK, DefaultFolder honoured",
            single.HResult == 0 && single.Paths.Count == 1 && Same(single.Paths[0], expectedA), single.ToString());
        Report.Check("open: title and owner applied",
            single.Observation.Title == "Spike: open recipe (default folder)" && single.Observation.OwnerMatches,
            $"title=\"{single.Observation.Title}\" ownerMatches={single.Observation.OwnerMatches}");

        var multi = Show(new FileDialogRequest
        {
            Kind = FileDialogKind.Open,
            Title = "Spike: open recipes (multi)",
            Filters = RecipeFilters,
            Folder = folder,
            Multiple = true,
            ClientGuid = s_clientGuid,
        }, new DialogScript { Action = DialogAction.Accept, FileName = "\"a.sparrecipe\" \"b.sparrecipe\"" });
        Report.Check("open: multi-select returns both paths",
            multi.HResult == 0 && multi.Paths.Count == 2, multi.ToString());

        var cancel = Show(OpenRequest(folder, "Spike: cancel"), new DialogScript { Action = DialogAction.Cancel });
        Report.Check("open: IDCANCEL returns HRESULT_FROM_WIN32(ERROR_CANCELLED)",
            cancel.Cancelled && cancel.Paths.Count == 0, cancel.ToString());

        var closeAbort = Show(OpenRequest(folder, "Spike: close E_ABORT"),
            new DialogScript { Action = DialogAction.CloseWithHResult, CloseHResult = ShellConstants.EAbort });
        Report.Check("open: IFileDialog::Close(E_ABORT) from OnFolderChange makes Show return E_ABORT",
            closeAbort.HResult == ShellConstants.EAbort, closeAbort.ToString());

        var closeCancelled = Show(OpenRequest(folder, "Spike: close cancelled"),
            new DialogScript { Action = DialogAction.CloseWithHResult });
        Report.Check("open: IFileDialog::Close(ERROR_CANCELLED) reads as a cancel",
            closeCancelled.Cancelled, closeCancelled.ToString());

        var missing = Show(OpenRequest(folder, "Spike: missing file"),
            new DialogScript { Action = DialogAction.Accept, FileName = "missing.sparrecipe", Deadline = TimeSpan.FromSeconds(2) });
        Report.Check("open: FOS_FILEMUSTEXIST blocks a missing name (native error box; watchdog dismisses)",
            missing.Cancelled && missing.Observation.WatchdogFired, missing.ToString());

        var save = Show(new FileDialogRequest
        {
            Kind = FileDialogKind.Save,
            Title = "Spike: export",
            Filters = [new("CSV (*.csv)", "*.csv"), new("JSON (*.json)", "*.json")],
            Folder = folder,
            DefaultExtension = "csv",
            FileName = "export",
            ClientGuid = s_clientGuid,
        }, new DialogScript { Action = DialogAction.Accept });
        Report.Check("save: default extension appended to a new name",
            save.HResult == 0 && save.Paths.Count == 1 && Same(save.Paths[0], Path.Combine(folder, "export.csv")) &&
            !File.Exists(save.Paths[0]), save.ToString());

        var overwrite = Show(SaveRequest(folder, "Spike: overwrite accepted"),
            new DialogScript { Action = DialogAction.Accept, FileName = "existing.csv", AcceptOverwrite = true });
        Report.Check("save: OnOverwrite answering FDEOR_ACCEPT skips the native prompt",
            overwrite.HResult == 0 && overwrite.Observation.Events.Contains("Overwrite") && !overwrite.Observation.WatchdogFired,
            overwrite.ToString());

        var prompt = Show(SaveRequest(folder, "Spike: overwrite prompt"),
            new DialogScript { Action = DialogAction.Accept, FileName = "existing.csv", Deadline = TimeSpan.FromSeconds(2) });
        Report.Check("save: default OnOverwrite shows the native confirmation (watchdog dismisses both)",
            prompt.Cancelled && prompt.Observation.WatchdogFired, prompt.ToString());

        // Latency: CoCreateInstance + release, and an automated open round trip.
        DialogWindows.RunOnSta((owner, threadId) =>
        {
            Report.Time("dialog CoCreateInstance(FileOpenDialog)+FinalRelease", 20, () =>
            {
                var dialog = ShellCom.Create<IFileOpenDialog>(ShellConstants.ClsidFileOpenDialog,
                    ShellConstants.IidFileOpenDialog);
                ShellCom.Release(dialog);
            });

            var shows = new double[10];
            var found = new double[10];
            for (var i = 0; i < shows.Length; i++)
            {
                var result = FileDialogs.Show(owner, threadId, OpenRequest(folder, "Spike: timing"),
                    new DialogScript { Action = DialogAction.Accept, FileName = "a.sparrecipe" });
                shows[i] = result.ShowMs;
                found[i] = result.Observation.FoundAfterMs;
            }

            Report.Summary("dialog Show -> first OnFolderChange (window exists)", found);
            Report.Summary("dialog Show -> automated IDOK -> Show returns", shows);
            Report.Info($"hide operations by the WH_CBT hook so far (create + re-hide on activate): {DialogWindows.HiddenAtCreation}");
            return 0;
        });
    }

    /// <summary>The dialog object is apartment-threaded; see what happens when the caller is in the MTA.</summary>
    private static void RunMta()
    {
        string outcome = "";
        var thread = new Thread(() =>
        {
            var hr = ShellNative.CoCreateInstance(ShellConstants.ClsidFileOpenDialog, 0, ShellConstants.ClsctxInprocServer,
                ShellConstants.IidFileOpenDialog, out var instance);
            outcome = $"apartment={Thread.CurrentThread.GetApartmentState()} CoCreateInstance hr=0x{hr:X8}";
            if (hr != 0)
            {
                return;
            }

            var dialog = ShellCom.Wrap<IFileOpenDialog>(instance);
            try
            {
                const string title = "Spike: shown from MTA";
                dialog.SetTitle(title);
                var observation = new DialogObservation();
                int showHr;
                using (new DialogWatchdog(title, 0, 0, DialogAction.Cancel, TimeSpan.FromSeconds(3), observation,
                    System.Diagnostics.Stopwatch.GetTimestamp()))
                {
                    showHr = dialog.Show(0);
                }

                outcome += $" Show hr=0x{showHr:X8} callerThread={Windows.Win32.PInvoke.GetCurrentThreadId()} " +
                    $"dialogThread={observation.WindowThread} {observation}";
            }
            catch (Exception e)
            {
                outcome += $" call failed: {e.GetType().Name} 0x{e.HResult:X8}";
            }
            finally
            {
                ShellCom.Release(dialog);
            }
        });
        thread.SetApartmentState(ApartmentState.MTA);
        thread.Start();
        thread.Join();
        Report.Check("MTA caller: observed behaviour (informational)", true, outcome);
    }

    private static FileDialogRequest OpenRequest(string folder, string title) => new()
    {
        Kind = FileDialogKind.Open,
        Title = title,
        Filters = RecipeFilters,
        Folder = folder,
        ClientGuid = s_clientGuid,
    };

    private static FileDialogRequest SaveRequest(string folder, string title) => new()
    {
        Kind = FileDialogKind.Save,
        Title = title,
        Filters = [new("CSV (*.csv)", "*.csv")],
        Folder = folder,
        DefaultExtension = "csv",
        ClientGuid = s_clientGuid,
    };

    private static FileDialogResult Show(FileDialogRequest request, DialogScript script) =>
        DialogWindows.RunOnSta((owner, threadId) => FileDialogs.Show(owner, threadId, request, script));
#endif

#if PART_CSWIN32_DIALOGS
    private static void RunCsWin32(string folder)
    {
        var accepted = DialogWindows.RunOnSta((owner, threadId) => CsWin32Dialogs.ShowOpen(owner, threadId,
            "Spike: CsWin32 open", folder, "a.sparrecipe", DialogAction.Accept));
        Report.Check("cswin32 open: prefilled name + watchdog IDOK",
            accepted.HResult == 0 && accepted.Paths.Length == 1 && Same(accepted.Paths[0], Path.Combine(folder, "a.sparrecipe")),
            $"hr=0x{accepted.HResult:X8} paths=[{string.Join(" | ", accepted.Paths)}] show={accepted.ShowMs:F1}ms {accepted.Observation}");

        var cancelled = DialogWindows.RunOnSta((owner, threadId) => CsWin32Dialogs.ShowOpen(owner, threadId,
            "Spike: CsWin32 cancel", folder, "a.sparrecipe", DialogAction.Cancel));
        Report.Check("cswin32 open: cancel surfaces as COMException(ERROR_CANCELLED)",
            cancelled.HResult == unchecked((int)0x800704C7),
            $"hr=0x{cancelled.HResult:X8} show={cancelled.ShowMs:F1}ms {cancelled.Observation}");
    }
#endif

    private static bool Same(string a, string b) => string.Equals(Path.GetFullPath(a), Path.GetFullPath(b),
        StringComparison.OrdinalIgnoreCase);
}
#endif
