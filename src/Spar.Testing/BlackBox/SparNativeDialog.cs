namespace Spar.Testing;

/// <summary>
/// A native message the launched app shows (a task dialog or a message box: a start-up failure, a failed relaunch, a
/// close question), as UI Automation read it when it appeared: its title, its text and its buttons. Answer it with
/// <see cref="PressAsync"/>, which invokes the button through UI Automation, never through input.
/// </summary>
public sealed class SparNativeDialog
{
    private readonly SparBlackBoxApp _app;
    private readonly DialogContent _content;

    /// <summary>The dialog's title: the app's name, for Spar's own messages.</summary>
    public string Title => _content.Title;

    /// <summary>The dialog's text: its parts (a task dialog's heading, then its content) on lines of their own.</summary>
    public string Text => string.Join("\n", _content.Texts);

    /// <summary>The dialog's text elements, in order.</summary>
    public IReadOnlyList<string> Texts => _content.Texts;

    /// <summary>The names of the dialog's own buttons, in order; its title bar's are left out.</summary>
    public IReadOnlyList<string> Buttons => _content.Buttons;

    internal SparNativeDialog(SparBlackBoxApp app, DialogContent content)
    {
        _app = app;
        _content = content;
    }

    /// <summary>Presses the button named <paramref name="button"/> (its whole name) through UI Automation's Invoke pattern.</summary>
    public async Task PressAsync(string button)
    {
        ArgumentException.ThrowIfNullOrEmpty(button);
        if (!Buttons.Contains(button, StringComparer.Ordinal))
        {
            var buttons = string.Join(", ", Buttons.Select(b => $"'{b}'"));
            throw new ArgumentException($"The dialog '{Title}' has no button '{button}'; it has {buttons}.", nameof(button));
        }

        if (!await _app.Automation.PressAsync(_content, button))
        {
            throw new InvalidOperationException($"The dialog '{Title}' closed before '{button}' could be pressed.");
        }
    }

    public override string ToString() => $"{Title}: {Text.Replace('\n', ' ')} [{string.Join(" | ", Buttons)}]";
}
