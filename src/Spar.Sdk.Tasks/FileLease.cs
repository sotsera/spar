using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace Spar.Sdk.Tasks;

/// <summary>
/// A lease that one build holds at a time: a file opened with no sharing, deleted when closed. The variants of one
/// app build at the same time and share some folders (<c>node_modules</c>, the generated client), and two writers at
/// once would break them.
/// </summary>
internal static class FileLease
{
    public static readonly TimeSpan Timeout = TimeSpan.FromMinutes(10);

    /// <summary>Waits for the lease; null when another build held it for <see cref="Timeout"/>.</summary>
    public static FileStream? Acquire(AbsolutePath path, TaskLoggingHelper log, string waiting)
    {
        Directory.CreateDirectory(new AbsolutePath(Path.GetDirectoryName(path.Value)!));
        var deadline = DateTime.UtcNow + Timeout;
        var waited = false;
        while (true)
        {
            try
            {
                return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose);
            }
            catch (IOException) when (DateTime.UtcNow < deadline)
            {
                if (!waited)
                {
                    log.LogMessage(MessageImportance.High, waiting);
                    waited = true;
                }

                Thread.Sleep(250);
            }
            catch (IOException)
            {
                return null;
            }
        }
    }
}
