using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace SqlTool.CopyJobs;

/// <summary>Saved recipes handed over by a relaunch (Program.cs), run one after another by the copy jobs.</summary>
public sealed class RecipeQueue
{
    private readonly ConcurrentQueue<string> _files = new();

    /// <summary>Queues a recipe file. A relaunch's arguments come from any process of this user, so they are checked.</summary>
    public bool TryAdd(string path)
    {
        if (!path.EndsWith(".sqlrecipe", StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
        {
            return false;
        }

        _files.Enqueue(path);
        return true;
    }

    public bool TryTake([NotNullWhen(true)] out string? path) => _files.TryDequeue(out path);
}
