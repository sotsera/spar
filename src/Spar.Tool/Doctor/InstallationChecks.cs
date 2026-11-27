namespace Spar.Tool.Doctor;

/// <summary>
/// The installation file beside the app whose identity doctor read (decision "M3b's decide-first questions", item 6):
/// <c>&lt;app&gt;.spar.json</c>, read with the app's own parser, so a file the app would refuse is an error here too, and
/// a file it would read is listed with every value it sets, environment variables expanded as on this machine.
/// </summary>
internal static class InstallationChecks
{
    public static IReadOnlyList<Finding> Check(string? appFile)
    {
        if (appFile is null)
        {
            return [];
        }

        var folder = Path.GetDirectoryName(Path.GetFullPath(appFile))!;
        var path = Path.Combine(folder, Path.GetFileNameWithoutExtension(appFile) + ".spar.json");
        InstallationFile? file;
        try
        {
            file = InstallationFile.Read(path, new InstallationOptions());
        }
        catch (InvalidOperationException exception)
        {
            return [Finding.Error(FindingIds.InstallationFile, Area.Identity, exception.Message,
                "Fix the key the message names, or remove the file: an app that reads it does not start while it is wrong.")];
        }

        if (file is null)
        {
            return [Finding.Info(FindingIds.InstallationFile, Area.Identity, $"No installation file beside the app ({path}).")];
        }

        var sets = file.Values.Count == 0 ? "nothing" : file.Describe();
        return [Finding.Info(FindingIds.InstallationFile, Area.Identity,
            $"Installation file {path} sets {sets}. The app reads it only when its Program.cs calls spar.InstallationFile(), and a key the code locked stops its start.")];
    }
}
