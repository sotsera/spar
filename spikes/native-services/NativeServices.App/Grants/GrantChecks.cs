#if PART_GRANTS
#if PART_DIALOGS
using NativeServices.App.Dialogs;
#endif

namespace NativeServices.App.Grants;

internal static class GrantChecks
{
    public static void Run(string folder)
    {
        var path = Path.Combine(folder, "granted.sparrecipe");
        File.WriteAllText(path, "{\"recipe\":\"granted\"}");

        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 11, 13, 12, 0, 0, TimeSpan.Zero));
        var table = new GrantTable(clock);
        var handle = table.Issue(path, GrantAccess.Read, TimeSpan.FromMinutes(10));
        Report.Check("grant: handle is opaque (no path), 128-bit random",
            handle.StartsWith("fg_", StringComparison.Ordinal) && handle.Length == 25 &&
            !handle.Contains("granted", StringComparison.OrdinalIgnoreCase), handle);
        Report.Check("grant: read through a read grant", table.ReadText(handle) == "{\"recipe\":\"granted\"}", "ok");
        Report.Check("grant: write through a read grant is refused",
            Fails(() => table.WriteText(handle, "x")) == GrantError.Forbidden, "Forbidden");

        var unique = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < 10_000; i++)
        {
            unique.Add(table.Issue(path, GrantAccess.Read, TimeSpan.FromMinutes(1)));
        }

        Report.Check("grant: 10,000 handles are distinct", unique.Count == 10_000, $"{unique.Count}");
        foreach (var issued in unique)
        {
            table.Revoke(issued);
        }

        clock.Advance(TimeSpan.FromMinutes(11));
        Report.Check("grant: expired handle refused and removed",
            Fails(() => table.ReadText(handle)) == GrantError.Expired && table.Count == 0, $"count={table.Count}");

        var revoked = table.Issue(path, GrantAccess.Read, TimeSpan.FromMinutes(10));
        table.Revoke(revoked);
        Report.Check("grant: revoked handle is unknown", Fails(() => table.ReadText(revoked)) == GrantError.Unknown, "Unknown");

        var beforeRestart = table.Issue(path, GrantAccess.Read, TimeSpan.FromMinutes(10));
        var afterRestart = new GrantTable(clock);
        Report.Check("grant: a new process (new table) does not know old handles",
            Fails(() => afterRestart.ReadText(beforeRestart)) == GrantError.Unknown, "Unknown");

        var timingTable = new GrantTable(TimeProvider.System);
        var timingHandle = timingTable.Issue(path, GrantAccess.Read, TimeSpan.FromHours(1));
        Report.Time("grant Issue (random handle + dictionary add)", 1000,
            () => timingTable.Issue(path, GrantAccess.Read, TimeSpan.FromHours(1)));
        Report.Time("grant Require (lookup + expiry + access check)", 1000,
            () => timingTable.Require(timingHandle, GrantAccess.Read));

#if PART_DIALOGS
        RunEndToEnd(folder);
#endif
    }

#if PART_DIALOGS
    private static void RunEndToEnd(string folder)
    {
        var grants = new GrantTable(TimeProvider.System);
        DialogScript? next = null;
        var service = new FilesService(grants, request => DialogWindows.RunOnSta((owner, threadId) =>
            FileDialogs.Show(owner, threadId, new FileDialogRequest
            {
                Kind = request.Kind,
                Title = request.Title,
                Filters = request.Filters,
                Multiple = request.Multiple,
                FileName = request.FileName,
                DefaultExtension = request.DefaultExtension,
                Folder = folder,
            }, next)));

        next = new DialogScript { Action = DialogAction.Accept, FileName = "granted.sparrecipe" };
        var opened = service.PickOpen("Spike: pick recipe", [new("Spar recipes", "*.sparrecipe")], multiple: false);
        Report.Check("files.pickOpen -> handle + name only",
            opened is [{ Name: "granted.sparrecipe" }], opened is null ? "cancelled" : $"{opened[0]}");
        if (opened is [var file])
        {
            Report.Check("files.readText(handle)", service.ReadText(file.Handle) == "{\"recipe\":\"granted\"}", "ok");
        }

        next = new DialogScript { Action = DialogAction.Accept, FileName = "results" };
        var saved = service.PickSave("Spike: export results", "results", [new("CSV", "*.csv")], "csv");
        if (saved is not null)
        {
            service.WriteText(saved.Handle, "id,name\n1,fake\n");
            Report.Check("files.pickSave + writeText(handle)",
                File.ReadAllText(Path.Combine(folder, "results.csv")) == "id,name\n1,fake\n" && saved.Name == "results.csv",
                $"{saved}");
            service.Release(saved.Handle);
            Report.Check("files.release(handle) then write is refused",
                Fails(() => service.WriteText(saved.Handle, "x")) == GrantError.Unknown, "Unknown");
        }
        else
        {
            Report.Check("files.pickSave + writeText(handle)", false, "cancelled");
        }

        next = new DialogScript { Action = DialogAction.Cancel };
        var cancelled = service.PickOpen("Spike: pick cancelled", [new("Spar recipes", "*.sparrecipe")], multiple: false);
        Report.Check("files.pickOpen cancelled -> null, no grant",
            cancelled is null && grants.Count == 1, $"grants={grants.Count}");
        grants.RevokeAll();
    }
#endif

    private static GrantError Fails(Action action)
    {
        try
        {
            action();
            return GrantError.None;
        }
        catch (GrantException e)
        {
            return e.Error;
        }
    }
}
#endif
