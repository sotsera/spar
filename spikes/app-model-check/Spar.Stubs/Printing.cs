namespace Spar;

/// <summary>Why printing gave no output.</summary>
public closed record PrintError
{
    /// <summary>The user closed the print dialog.</summary>
    public sealed record Dismissed : PrintError;

    public sealed record NoPrinter : PrintError;

    /// <summary>No print service, for example none on this Linux desktop.</summary>
    public sealed record Unavailable(string Reason) : PrintError;

    public sealed record Failed(string Reason) : PrintError;

    /// <summary>The PDF could not be written to the grant.</summary>
    public sealed record WriteFailed(FileAccessError Error) : PrintError;
}

public enum Paper
{
    A4,
    Letter,
}

public sealed record PdfOptions
{
    public Paper Paper { get; init; } = Paper.A4;

    public bool Landscape { get; init; }

    public bool Background { get; init; } = true;
}

/// <summary>Prints the current page of a window; injectable. The page uses <c>print</c> from the SDK.</summary>
public interface IPrinting
{
    /// <summary>Writes the window's page as a PDF into a folder grant, without a dialog.</summary>
    Task<Result<FileGrant, PrintError>> ToPdfAsync(PdfOptions options, FolderGrant destination, string fileName, CancellationToken cancellationToken);
}
