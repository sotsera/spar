#if PART_GRANTS && PART_DIALOGS
using NativeServices.App.Dialogs;

namespace NativeServices.App.Grants;

/// <summary>What the page receives from a pick: an opaque handle and a display name, never a path.</summary>
internal sealed record PickedFile(string Handle, string Name);

/// <summary>
/// Sketch of the page-facing service (files.pickOpen / pickSave / readText / writeText / release). The dialog runs in
/// the backend on the window's STA thread; the page only ever names handles.
/// </summary>
internal sealed class FilesService(GrantTable grants, Func<FileDialogRequest, FileDialogResult> showDialog)
{
    private static readonly TimeSpan s_lifetime = TimeSpan.FromHours(8);

    /// <summary>Returns null when the user cancels; the page treats that as an ordinary outcome, not an error.</summary>
    public PickedFile[]? PickOpen(string title, IReadOnlyList<FileFilter> filters, bool multiple)
    {
        var result = showDialog(new FileDialogRequest
        {
            Kind = FileDialogKind.Open,
            Title = title,
            Filters = filters,
            Multiple = multiple,
        });
        if (result.Cancelled)
        {
            return null;
        }

        var picked = new PickedFile[result.Paths.Count];
        for (var i = 0; i < picked.Length; i++)
        {
            var handle = grants.Issue(result.Paths[i], GrantAccess.Read, s_lifetime);
            picked[i] = new PickedFile(handle, Path.GetFileName(result.Paths[i]));
        }

        return picked;
    }

    /// <summary>A save grant allows writing (and reading back what was written); nothing else in that folder.</summary>
    public PickedFile? PickSave(string title, string suggestedName, IReadOnlyList<FileFilter> filters,
        string defaultExtension)
    {
        var result = showDialog(new FileDialogRequest
        {
            Kind = FileDialogKind.Save,
            Title = title,
            Filters = filters,
            FileName = suggestedName,
            DefaultExtension = defaultExtension,
        });
        if (result.Cancelled)
        {
            return null;
        }

        var path = result.Paths[0];
        return new PickedFile(grants.Issue(path, GrantAccess.Write | GrantAccess.Read, s_lifetime), Path.GetFileName(path));
    }

    public string ReadText(string handle) => grants.ReadText(handle);

    public void WriteText(string handle, string text) => grants.WriteText(handle, text);

    public bool Release(string handle) => grants.Revoke(handle);
}
#endif
