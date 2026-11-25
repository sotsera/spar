namespace Spar.Hosting;

/// <summary>
/// The app's own theme choice (plan section 3d), for every host: the SDK's <c>setColorScheme</c> saves it as
/// <c>light</c>, <c>dark</c> or <c>system</c> in a file of the data root, and the host reads it before its window
/// exists, so the window has the chosen colour from its first frame.
/// </summary>
internal static class SavedColorScheme
{
    /// <summary>The file's name in the data root.</summary>
    public const string FileName = "color-scheme";

    /// <summary>
    /// True for dark, false for light, or null to follow the system: no file, <c>system</c>, anything else, or a file
    /// that cannot be read. A small local read, before any window.
    /// </summary>
    public static bool? Read(string dataRoot)
    {
        try
        {
            var path = Path.Combine(dataRoot, FileName);
            if (!File.Exists(path))
            {
                return null;
            }

            return Parse(File.ReadAllText(path).Trim());
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>The choice a name stands for: true dark, false light, null for <c>system</c> or anything else.</summary>
    public static bool? Parse(string name) => name switch
    {
        "dark" => true,
        "light" => false,
        _ => null,
    };

    /// <summary>The name a choice is saved under.</summary>
    public static string NameOf(bool? dark) => dark switch
    {
        true => "dark",
        false => "light",
        null => "system",
    };

    /// <summary>
    /// Saves the choice for the next start, replacing the file in one step, so a start that reads it meanwhile sees the
    /// old choice or the new one, never half a file. Off the UI thread: it writes to disk. Throws
    /// <see cref="IOException"/> or <see cref="UnauthorizedAccessException"/> when the data root cannot be written.
    /// </summary>
    public static void Write(string dataRoot, bool? dark)
    {
        Directory.CreateDirectory(dataRoot);
        var path = Path.Combine(dataRoot, FileName);
        var temporary = Path.Combine(dataRoot, $"{FileName}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporary, NameOf(dark) + "\n");
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }
}
