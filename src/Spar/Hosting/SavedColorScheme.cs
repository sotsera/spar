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

            return File.ReadAllText(path).Trim() switch
            {
                "dark" => true,
                "light" => false,
                _ => null,
            };
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
