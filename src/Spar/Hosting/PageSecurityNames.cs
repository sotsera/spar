namespace Spar.Hosting;

/// <summary>
/// The C# names of the page security enums, written out, and a loop over a permission array: an enum's
/// <c>ToString</c> and LINQ over an enum collection would cost a NativeAOT image generic and reflection code for a
/// handful of log lines.
/// </summary>
internal static class PageSecurityNames
{
    public static string Of(Permission permission) => permission switch
    {
        Permission.Camera => "Camera",
        Permission.Microphone => "Microphone",
        Permission.Geolocation => "Geolocation",
        Permission.ClipboardRead => "ClipboardRead",
        Permission.Sensors => "Sensors",
        Permission.LocalFonts => "LocalFonts",
        Permission.Midi => "Midi",
        Permission.WindowManagement => "WindowManagement",
        Permission.Autoplay => "Autoplay",
        _ => "?",
    };

    public static string Of(CspReports reports) => reports switch
    {
        CspReports.Always => "Always",
        CspReports.DevelopmentOnly => "DevelopmentOnly",
        CspReports.Off => "Off",
        _ => "?",
    };

    public static bool Contains(Permission[] permissions, Permission permission)
    {
        foreach (var declared in permissions)
        {
            if (declared == permission)
            {
                return true;
            }
        }

        return false;
    }
}
