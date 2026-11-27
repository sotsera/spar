namespace Spar.AppWindows;

/// <summary>
/// The rules for a window kind's name and route. A kind's name becomes a string literal of the page's
/// <c>WindowKind</c> union and a key of <c>windows.json</c>; a route is a path of the app's own origin that the host
/// answers with the frontend's page.
/// </summary>
internal static class WindowRoutes
{
    /// <summary>The longest kind name and route, in characters.</summary>
    public const int MaxLength = 64;

    /// <summary>The name the main window has in hooks and logs; no kind may take it.</summary>
    public const string Main = "main";

    /// <summary>
    /// A kind's name: camelCase ASCII (<c>settings</c>, <c>queryEditor</c>), starting with a letter, at most
    /// <see cref="MaxLength"/> characters, and not <c>main</c>.
    /// </summary>
    public static string CheckKind(string kind)
    {
        ArgumentException.ThrowIfNullOrEmpty(kind);
        var valid = kind.Length <= MaxLength
            && char.IsAsciiLetterLower(kind[0])
            && kind.All(char.IsAsciiLetterOrDigit);
        if (!valid)
        {
            throw new ArgumentException($"Window kind '{kind}': a kind's name is camelCase ASCII letters and digits, starting with a lower-case letter, at most {MaxLength} characters.", nameof(kind));
        }

        if (kind == Main)
        {
            throw new ArgumentException("Window kind 'main' is the main window's name; give the kind another one.", nameof(kind));
        }

        return kind;
    }

    /// <summary>
    /// A route: an absolute path of letters, digits and <c>-_./</c>, with no empty, <c>.</c> or <c>..</c> segment, not
    /// under <c>/_spar/</c> (Spar's own addresses) and without a query or fragment; a trailing <c>/</c> is dropped.
    /// </summary>
    public static string Check(string route, string kind)
    {
        ArgumentException.ThrowIfNullOrEmpty(route);
        var trimmed = route.Length > 1 ? route.TrimEnd('/') : route;
        var segments = trimmed.Split('/');
        var valid = trimmed.Length <= MaxLength
            && trimmed.StartsWith('/')
            && trimmed.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' or '/')
            && (trimmed == "/" || segments.Skip(1).All(segment => segment.Length > 0 && segment is not "." and not ".."))
            && !trimmed.StartsWith("/_spar", StringComparison.OrdinalIgnoreCase);
        if (!valid)
        {
            throw new ArgumentException($"Window kind '{kind}': route '{route}' is not a path the app's origin can serve; give an absolute path such as '/settings', of letters, digits and -_./, at most {MaxLength} characters, outside /_spar.", nameof(route));
        }

        return trimmed;
    }

    /// <summary>Whether <paramref name="path"/> is <paramref name="route"/> or below it.</summary>
    public static bool IsUnder(string path, string route) =>
        route == "/"
            ? path == "/"
            : path.Equals(route, StringComparison.Ordinal)
              || (path.Length > route.Length && path.StartsWith(route, StringComparison.Ordinal) && path[route.Length] == '/');
}
