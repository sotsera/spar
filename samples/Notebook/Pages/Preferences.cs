using Spar;

namespace Notebook.Pages;

/// <summary>How the open page is shown: the editor, the editor beside the preview, or the preview.</summary>
public enum ViewMode
{
    Write,
    Split,
    Read,
}

/// <summary>What the notebook remembers between runs, in <c>settings.json</c> in the app's data folder.</summary>
public sealed record NotebookSettings(ViewMode ViewMode, string? LastPage);

/// <summary>The notebook's remembered choices: the view mode, and the page to open at the next start.</summary>
[SparContract]
public sealed class PreferencesService(ISparSettings<NotebookSettings> settings)
{
    [SparCommand]
    public Task<NotebookSettings> GetAsync() => Task.FromResult(settings.Current);

    [SparCommand]
    public async Task SetViewModeAsync(ViewMode mode, CancellationToken cancellationToken) =>
        await settings.UpdateAsync(current => current with { ViewMode = mode }, cancellationToken);

    /// <summary>Remembers the open page; null when none is open.</summary>
    [SparCommand]
    public async Task SetLastPageAsync(string? pageId, CancellationToken cancellationToken) =>
        await settings.UpdateAsync(current => current with { LastPage = pageId }, cancellationToken);
}
