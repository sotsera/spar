using Spar;

namespace SqlTool.Recipes;

public sealed record ExportRequest(IReadOnlyList<string> Tables);

/// <summary>Why an export did not finish.</summary>
public closed record ExportError
{
    public sealed record Dismissed : ExportError;

    public sealed record NoFolder(string Reason) : ExportError;

    public sealed record WriteFailed(string File) : ExportError;
}

/// <summary>Exports tables as CSV files. Wire name: csvExport.</summary>
[SparContract]
public sealed class CsvExportService(IFileDialogs dialogs)
{
    /// <summary>Writes one CSV per table into a folder the user picks.</summary>
    [SparCommand]
    public async Task<Result<int, ExportError>> ExportAsync(ExportRequest request, CancellationToken cancellationToken)
    {
        var picked = await dialogs.OpenFolderAsync(new() { Title = "Choose export folder", Access = FolderAccess.ReadWrite }, cancellationToken);
        return picked switch
        {
            FolderGrant folder => await WriteAsync(folder, request.Tables, cancellationToken),
            FileDialogError.Dismissed => new ExportError.Dismissed(),
            FileDialogError.AccessDenied e => new ExportError.NoFolder(e.Path),
            FileDialogError.Unavailable e => new ExportError.NoFolder(e.Reason),
            FileDialogError.Busy => new ExportError.NoFolder("busy"),
        };
    }

    private static async Task<Result<int, ExportError>> WriteAsync(FolderGrant folder, IReadOnlyList<string> tables, CancellationToken cancellationToken)
    {
        foreach (var table in tables)
        {
            // Relative to the chosen folder; "..", absolute paths and links leaving it are refused (OutsideFolder).
            if (await folder.WriteTextAsync($"{table}.csv", "", cancellationToken) is FileAccessError)
            {
                return new ExportError.WriteFailed($"{table}.csv");
            }
        }

        return tables.Count;
    }
}
