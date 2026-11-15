using Spar;

namespace SqlTool.Recipes;

public sealed record RecipeInfo(string Name, int Tables);

/// <summary>Why a recipe could not be imported.</summary>
public closed record ImportError
{
    public sealed record Dismissed : ImportError;

    public sealed record NotAllowed(string Path) : ImportError;

    public sealed record NoFileDialog(string Reason) : ImportError;

    public sealed record TryAgain : ImportError;

    public sealed record Unreadable(string File) : ImportError;
}

[SparContract]
public sealed class RecipesService(IFileDialogs dialogs)
{
    /// <summary>Lets the user pick a recipe file and imports it.</summary>
    [SparCommand]
    public async Task<Result<RecipeInfo, ImportError>> ImportAsync(CancellationToken cancellationToken)
    {
        var picked = await dialogs.OpenAsync(new() { Title = "Import recipe", Filters = [new("Recipes", "*.sparrecipe")] }, cancellationToken);
        return picked switch
        {
            FileGrant file => Read(file),
            FileDialogError.Dismissed => new ImportError.Dismissed(),
            FileDialogError.AccessDenied e => new ImportError.NotAllowed(e.Path),
            FileDialogError.Unavailable e => new ImportError.NoFileDialog(e.Reason),
            FileDialogError.Busy => new ImportError.TryAgain(),
        };
    }

    private static Result<RecipeInfo, ImportError> Read(FileGrant file) => file.OpenRead() switch
    {
        Stream stream => new RecipeInfo(file.Name, Tables: CountTables(stream)),
        FileAccessError => new ImportError.Unreadable(file.Name),
    };

    private static int CountTables(Stream stream)
    {
        using (stream)
        {
            return 0;
        }
    }
}
