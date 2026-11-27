using Spar.Files;

namespace Spar.Bridge;

/// <summary>
/// The document's window and the page's file commands (decisions "M3c's decide-first questions", items 2 and 3, and "The
/// drop design, from the file-drop spike"): the grants are the window's, so they outlive the document, and a drop's
/// files come from the host, never from the page's JSON.
/// </summary>
internal sealed partial class BridgeSession
{
    private readonly FileCommands? _files;

    /// <summary>
    /// The window the document belongs to: its grants and dialogs, which the page's file commands and the app's
    /// <see cref="IFileDialogs"/> use. Null for a session without one, where the file commands are unknown commands.
    /// </summary>
    public HostWindow? Window
    {
        get => _blobs.Window;
        init
        {
            _blobs.Window = value;
            _files = value is null ? null : new FileCommands(_dispatcher, _blobs, value);
        }
    }

    /// <summary>
    /// A message with the paths of the files the host read from it, not from its JSON: WebView2's
    /// <c>postMessageWithAdditionalObjects</c> of a drop. Only a call of the page's drop command uses them.
    /// </summary>
    public void Receive(string message, IReadOnlyList<string> files) => ReceiveCore(message, files);
}
