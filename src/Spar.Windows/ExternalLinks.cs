using Spar.Hosting;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Spar.Windows;

/// <summary>
/// External addresses the user follows from the page, opened outside the app (plan section 3b; decision "Opening links
/// and files outside the app"): only <c>http</c>, <c>https</c>, <c>mailto</c> and <c>tel</c>, never with credentials in
/// the address, never longer than <see cref="MaxLength"/>, and only after a user's action, which the caller checks.
/// <c>ShellExecuteW</c> can take hundreds of milliseconds and loads shell extensions, so it runs on an STA thread of its
/// own, never on the window's UI thread.
/// </summary>
internal static class ExternalLinks
{
    /// <summary>The longest address handed to the system: longer ones are refused, not cut.</summary>
    public const int MaxLength = 2_048;

    /// <summary>Why <paramref name="address"/> may not be opened outside the app; null when it may.</summary>
    public static string? Refusal(string address)
    {
        if (address.Length == 0 || address.Length > MaxLength)
        {
            return $"the address is empty or longer than {MaxLength} characters";
        }

        foreach (var character in address)
        {
            if (character <= ' ' || character == '"' || character == '\x7f')
            {
                return "the address has spaces, quotes or control characters";
            }
        }

        var colon = address.IndexOf(':', StringComparison.Ordinal);
        var scheme = colon <= 0 ? "" : address[..colon].ToLowerInvariant();
        switch (scheme)
        {
            case "http" or "https" when PageServer.IsAppOrigin(address):
                return "the app's own pages do not open in new windows";
            case "http" or "https":
                return HttpRefusal(address, colon);
            case "mailto" or "tel":
                return colon + 1 < address.Length ? null : "the address is empty";
            default:
                return $"only http, https, mailto and tel open outside the app, not '{(scheme.Length == 0 ? "?" : scheme)}:'";
        }
    }

    /// <summary>Opens an allowed address with the system's handler, on a thread of its own; failures are logged.</summary>
    public static void Open(string address, HostLog log)
    {
        var thread = new Thread(() => Execute(address, log)) { IsBackground = true, Name = "Spar external link" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    /// <summary><c>scheme://host…</c> with a host and no <c>user:password@</c> before it.</summary>
    private static string? HttpRefusal(string address, int colon)
    {
        if (string.CompareOrdinal(address, colon, "://", 0, 3) != 0)
        {
            return "the address has no host";
        }

        var authorityStart = colon + 3;
        var authorityEnd = address.IndexOfAny(['/', '?', '#'], authorityStart);
        var authority = authorityEnd < 0 ? address[authorityStart..] : address[authorityStart..authorityEnd];
        if (authority.Length == 0)
        {
            return "the address has no host";
        }

        return authority.Contains('@', StringComparison.Ordinal) ? "the address carries credentials" : null;
    }

    private static unsafe void Execute(string address, HostLog log)
    {
        try
        {
            HINSTANCE result;
            fixed (char* verb = "open")
            fixed (char* file = address)
            {
                result = PInvoke.ShellExecute(HWND.Null, verb, file, null, null, SHOW_WINDOW_CMD.SW_SHOWNORMAL);
            }

            // ShellExecute returns a value above 32 on success, an error code otherwise.
            if ((nint)result.Value <= 32)
            {
                log.Warn($"The system could not open the address (ShellExecute returned {(nint)result.Value}).");
            }
        }
        catch (Exception exception)
        {
            log.Error("Opening an address outside the app failed", exception);
        }
    }
}
