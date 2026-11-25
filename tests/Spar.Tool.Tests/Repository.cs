namespace Spar.Tool.Tests;

/// <summary>The repository the tests run from, and app folders of their own under its disposable <c>artifacts/</c>.</summary>
internal static class Repository
{
    public static string Root { get; } = Find();

    /// <summary>A new, empty folder under <c>artifacts/tool-tests/</c>, deleted by <see cref="TestFolder.Dispose"/>.</summary>
    public static TestFolder NewFolder(string name)
    {
        var path = Path.Combine(Root, "artifacts", "tool-tests", $"{name}-{Guid.NewGuid():N}"[..(name.Length + 9)]);
        Directory.CreateDirectory(path);
        return new TestFolder(path);
    }

    private static string Find()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Spar.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("These tests run from a build inside the Spar repository.");
    }
}

/// <summary>A test's own folder, deleted with everything in it at the end of the test.</summary>
internal sealed class TestFolder(string path, string? root = null) : IDisposable
{
    public string Path { get; } = path;

    /// <summary>The folder deleted at the end: <see cref="Path"/>, or the folder it is in (a solution's folder).</summary>
    public string Root { get; } = root ?? path;

    public string Write(string relative, string text)
    {
        var file = System.IO.Path.Combine(Path, relative);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(file)!);
        File.WriteAllText(file, text);
        return file;
    }

    public string Read(string relative) => File.ReadAllText(System.IO.Path.Combine(Path, relative));

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
            // A file a failed test left open; the folder is under the disposable artifacts/.
        }
    }
}
