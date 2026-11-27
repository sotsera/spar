using System.Collections.Concurrent;
using Spar.Files;

namespace Spar.Testing;

/// <summary>A file dialog the app showed under the test host (<see cref="SparTestFileDialogs.Shown"/>).</summary>
/// <param name="Kind"><c>open</c>, <c>openMany</c>, <c>save</c> or <c>folder</c>.</param>
public sealed record SparTestDialog(string Kind, string? Title, IReadOnlyList<FileFilter> Filters, string? SuggestedName);

/// <summary>
/// The file dialogs of a test app (<c>app.FileDialogs</c>): no dialog reaches the screen; each one the app shows, from
/// C# or from the page, takes the next answer the test queued, in order. Queue the answer before the call that opens
/// the dialog. A dialog with no answer queued ends <see cref="FileDialogError.Unavailable"/> and is reported in
/// <c>app.Problems</c>. The chosen paths become grants of the window that asked, as a real dialog's do.
/// </summary>
public sealed class SparTestFileDialogs : INativeFileDialogs
{
    private readonly ConcurrentQueue<Result<IReadOnlyList<string>, FileDialogError>> _answers = new();
    private readonly ConcurrentQueue<SparTestDialog> _shown = new();
    private readonly Action<string> _problem;

    internal SparTestFileDialogs(Action<string> problem)
    {
        _problem = problem;
    }

    /// <summary>Every dialog the app showed, in order.</summary>
    public IReadOnlyList<SparTestDialog> Shown => [.. _shown];

    /// <summary>The next dialog chooses these files or this folder (full paths; several only for an open-many dialog).</summary>
    public SparTestFileDialogs Choose(params string[] paths)
    {
        if (paths.Length == 0)
        {
            throw new ArgumentException("Choose at least one path; Dismiss() closes the dialog without a choice.", nameof(paths));
        }

        _answers.Enqueue(paths.Select(Path.GetFullPath).ToArray());
        return this;
    }

    /// <summary>The next dialog is closed by the user, without a choice.</summary>
    public SparTestFileDialogs Dismiss()
    {
        _answers.Enqueue(new FileDialogError.Dismissed());
        return this;
    }

    /// <summary>The next dialog fails, as when no file-chooser portal answers (<see cref="FileDialogError.Unavailable"/>).</summary>
    public SparTestFileDialogs Fail(FileDialogError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        _answers.Enqueue(error);
        return this;
    }

    Task<Result<IReadOnlyList<string>, FileDialogError>> INativeFileDialogs.ShowAsync(FileDialogRequest request, CancellationToken cancellationToken)
    {
        var kind = request.Kind switch
        {
            FileDialogKind.Open => "open",
            FileDialogKind.OpenMany => "openMany",
            FileDialogKind.Save => "save",
            _ => "folder",
        };
        _shown.Enqueue(new SparTestDialog(kind, request.Title, request.Filters, request.SuggestedName));
        if (_answers.TryDequeue(out var answer))
        {
            return Task.FromResult(answer);
        }

        var problem = $"The app showed a {kind} dialog (\"{request.Title}\") and the test queued no answer: call app.FileDialogs.Choose(…) or Dismiss() first.";
        _problem(problem);
        return Task.FromResult<Result<IReadOnlyList<string>, FileDialogError>>(new FileDialogError.Unavailable(problem));
    }
}
