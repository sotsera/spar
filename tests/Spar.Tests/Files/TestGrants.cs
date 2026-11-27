using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Spar.Files;
using Spar.Runtime;

namespace Spar.Tests.Files;

/// <summary>
/// Hand-written commands taking grants, standing in for what the generator writes: <c>grants.size</c> takes a
/// <c>FileGrant</c> request and returns its size read in C#.
/// </summary>
internal static class TestGrants
{
    public static IEnumerable<SparCommandEntry> Entries =>
    [
        new("grants", "size", 0, SizeAsync),
    ];

    private static ValueTask<SparOutcome> SizeAsync(SparCallContext call)
    {
        FileGrant grant;
        using (SparGrants.Begin(call))
        {
            try
            {
                grant = SparJson.Read(call.Request, SparGrants.ReadFile);
            }
            catch (JsonException exception)
            {
                return ValueTask.FromResult(SparOutcome.FromFailure(SparFailure.InvalidRequest(exception.Message)));
            }
        }

        var size = new FileInfo(grant.Path).Length;
        return ValueTask.FromResult(SparOutcome.FromValue(Encoding.UTF8.GetBytes(size.ToString(System.Globalization.CultureInfo.InvariantCulture))));
    }
}

/// <summary>Dialogs that answer in the order the test queued, and record what was asked.</summary>
internal sealed class QueuedDialogs : INativeFileDialogs
{
    private readonly ConcurrentQueue<Result<IReadOnlyList<string>, FileDialogError>> _answers = new();

    public ConcurrentQueue<FileDialogRequest> Asked { get; } = new();

    /// <summary>When set, the next dialog waits for it, as one the user keeps open.</summary>
    public TaskCompletionSource? Hold { get; set; }

    public QueuedDialogs Choose(params string[] paths)
    {
        _answers.Enqueue(paths);
        return this;
    }

    public QueuedDialogs Answer(FileDialogError error)
    {
        _answers.Enqueue(error);
        return this;
    }

    public async Task<Result<IReadOnlyList<string>, FileDialogError>> ShowAsync(FileDialogRequest request, CancellationToken cancellationToken)
    {
        Asked.Enqueue(request);
        if (Hold is { } hold)
        {
            await hold.Task.WaitAsync(cancellationToken);
        }

        return _answers.TryDequeue(out var answer) ? answer : new FileDialogError.Dismissed();
    }
}

/// <summary>A folder of its own under the repository's disposable <c>artifacts/</c>, removed afterwards.</summary>
internal sealed class TestFolder : IDisposable
{
    public TestFolder()
    {
        Path = System.IO.Path.Combine(TestFolders.Artifacts(), "file-grants", Guid.NewGuid().ToString("n")[..12]);
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string File(string name, string text = "")
    {
        var path = System.IO.Path.Combine(Path, name);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        System.IO.File.WriteAllText(path, text);
        return path;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
            // A file a failed test left open goes with the next clean of the temporary folder.
        }
    }
}
