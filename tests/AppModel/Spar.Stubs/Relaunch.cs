namespace Spar;

/// <summary>A later launch of the app, handed to the running instance (single instance).</summary>
/// <param name="Arguments">The launch's command-line arguments, without the executable. Untrusted input.</param>
/// <param name="WorkingDirectory">The launching process's working directory.</param>
public sealed record Relaunch(IReadOnlyList<string> Arguments, string WorkingDirectory)
{
    /// <summary>A path from the arguments, resolved against the relaunch's working directory.</summary>
    public string ResolvePath(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        return Path.GetFullPath(path, WorkingDirectory);
    }
}
