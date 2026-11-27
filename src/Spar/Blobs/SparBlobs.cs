namespace Spar;

/// <summary>
/// Makes blobs from streams (decision "M3b's decide-first questions", item 2): the stream is copied to a temporary
/// file in the app's cache folder (<c>blobs/</c>), so the handle holds no memory; the file goes with the last handle,
/// and whatever an earlier run left there goes when this service first starts. Injected like any service; blobs from
/// bytes or a file need no service (<see cref="SparBlob.FromBytes"/>, <see cref="SparBlob.FromFile"/>).
/// </summary>
public sealed class SparBlobs
{
    private readonly string _folder;

    internal SparBlobs(SparFolders folders)
    {
        _folder = Path.Combine(folders.Cache, "blobs");
        RemoveLeftovers(_folder);
    }

    /// <summary>
    /// Copies <paramref name="stream"/> from its position to its end into a temporary file, and gives the blob that
    /// reads it. The stream is not disposed. A cancelled copy leaves no file.
    /// </summary>
    public Task<SparBlob> FromStreamAsync(
        Stream stream, string contentType, string? name = null, CancellationToken cancellationToken = default) =>
        SparBlob.FromStreamAsync(stream, _folder, contentType, name, cancellationToken);

    private static void RemoveLeftovers(string folder)
    {
        if (!Directory.Exists(folder))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(folder, "*.blob"))
        {
            try
            {
                File.Delete(file);
            }
            catch (IOException)
            {
                // Another instance is serving it: it goes at a later start.
            }
            catch (UnauthorizedAccessException)
            {
                // As above.
            }
        }
    }
}
