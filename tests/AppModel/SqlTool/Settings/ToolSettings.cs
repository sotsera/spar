using Spar;

namespace SqlTool.Settings;

/// <summary>What the tool remembers between runs. Connections live in their own store; passwords never here.</summary>
public sealed record ToolSettings(int BatchSize, int PreviewRows, string? LastRecipe);

/// <summary>The settings the page shows and changes. Wire name: preferences.</summary>
[SparContract("preferences")]
public sealed class PreferencesService(ISparSettings<ToolSettings> settings)
{
    [SparCommand]
    public Task<ToolSettings> GetAsync() => Task.FromResult(settings.Current);

    /// <summary>A batch size between 1,000 and 1,000,000 rows; another one is kept as it was.</summary>
    [SparCommand]
    public Task<ToolSettings> SetBatchSizeAsync(int rows, CancellationToken cancellationToken) =>
        settings.UpdateAsync(current => rows is >= 1_000 and <= 1_000_000 ? current with { BatchSize = rows } : current, cancellationToken);
}
