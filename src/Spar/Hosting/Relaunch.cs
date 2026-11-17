namespace Spar;

/// <summary>
/// A later launch of the app, handed to the running instance (<c>docs/app-model.md</c> section 3.14). Any process of
/// the same user can start the app with any arguments, so the handler checks them.
/// </summary>
/// <param name="Arguments">The launch's command-line arguments, without the executable. Untrusted input.</param>
/// <param name="WorkingDirectory">The launching process's working directory.</param>
public sealed record Relaunch(IReadOnlyList<string> Arguments, string WorkingDirectory)
{
    /// <summary>A path from the arguments, resolved against the relaunch's working directory, not the running app's.</summary>
    public string ResolvePath(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        return Path.GetFullPath(path, WorkingDirectory);
    }
}
