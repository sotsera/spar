namespace Spar;

/// <summary>Why a message dialog gave no button.</summary>
public closed record DialogError
{
    /// <summary>The user closed the dialog without choosing a button.</summary>
    public sealed record Dismissed : DialogError;
}

/// <summary>A native message dialog: title, text, named buttons and the default one.</summary>
public sealed record MessageOptions(string Title, string Text, IReadOnlyList<string> Buttons, string? Default = null);

/// <summary>Native message dialogs owned by the window, never blocking its thread; injectable.</summary>
public interface IDialogs
{
    Task<Result<string, DialogError>> MessageAsync(MessageOptions options, CancellationToken cancellationToken);
}
