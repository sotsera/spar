namespace Spar;

/// <summary>
/// The powerful browser features the page may use, from <c>spar.Permissions(p => …)</c> in <c>Program.cs</c> (plan
/// section 3b, "Permissions, two layers"). Everything is denied with no prompt unless declared here. Declaring a feature
/// is the consent: the page is the app's own code, so the host grants it when the page asks, and the browser never
/// remembers the answer. The operating system's privacy settings (camera and microphone access, location) still apply
/// on top. The build reads these calls without running the app, so the call sits directly in the
/// <c>SparApplication.Create</c> configuration.
/// </summary>
public sealed class PermissionsBuilder
{
    /// <summary>The declared features as bits, by the enum's value: an enum list would cost the image generic code.</summary>
    private int _allowed;

    internal PermissionsBuilder()
    {
    }

    /// <summary>The declared features, each once, in the enum's order (the build writes them in the same order).</summary>
    internal Permission[] Allowed
    {
        get
        {
            var allowed = new Permission[System.Numerics.BitOperations.PopCount((uint)_allowed)];
            var at = 0;
            for (var value = 0; value <= (int)Permission.Autoplay; value++)
            {
                if ((_allowed & (1 << value)) != 0)
                {
                    allowed[at++] = (Permission)value;
                }
            }

            return allowed;
        }
    }

    /// <summary>
    /// Lets the page use <paramref name="permission"/>: the host's <c>Permissions-Policy</c> allows it for the app's own
    /// origin, and the host grants the page's request for it without a prompt. Declare only what the app uses.
    /// </summary>
    public PermissionsBuilder Allow(Permission permission)
    {
        if (permission is < Permission.Camera or > Permission.Autoplay)
        {
            throw new ArgumentOutOfRangeException(nameof(permission), (int)permission, "Use a Permission member.");
        }

        _allowed |= 1 << (int)permission;
        return this;
    }
}

/// <summary>
/// A powerful browser feature the page can ask for, denied unless declared with
/// <see cref="PermissionsBuilder.Allow"/>. These are the features both the <c>Permissions-Policy</c> header and the
/// WebView's permission requests know. The others the header names (USB, serial, HID, Bluetooth, payment, display
/// capture) stay denied, and so do the requests no header covers (notifications, downloads, file access, storage).
/// </summary>
public enum Permission
{
    /// <summary>The camera (<c>getUserMedia</c> video). The operating system's camera privacy setting still applies.</summary>
    Camera,

    /// <summary>The microphone (<c>getUserMedia</c> audio). The operating system's microphone privacy setting still applies.</summary>
    Microphone,

    /// <summary>The device's location (<c>navigator.geolocation</c>). The operating system's location setting still applies.</summary>
    Geolocation,

    /// <summary>Reading the clipboard from the page (<c>navigator.clipboard.read</c>, <c>readText</c>).</summary>
    ClipboardRead,

    /// <summary>The motion sensors: accelerometer, gyroscope and magnetometer.</summary>
    Sensors,

    /// <summary>The list of fonts installed on the computer (<c>queryLocalFonts</c>).</summary>
    LocalFonts,

    /// <summary>MIDI devices (<c>requestMIDIAccess</c>), including system-exclusive messages.</summary>
    Midi,

    /// <summary>The screens' layout and placing windows across them (<c>getScreenDetails</c>).</summary>
    WindowManagement,

    /// <summary>Playing audio and video with sound without a user gesture first. Muted media may always play.</summary>
    Autoplay,
}
