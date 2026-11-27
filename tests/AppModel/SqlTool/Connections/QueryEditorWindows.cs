using System.Text.Json.Nodes;
using Spar;

namespace SqlTool.Connections;

/// <summary>Opens a connection's query editor from C#, as the tray's menu does, and names it after the connection.</summary>
public sealed class QueryEditorWindows(IWindows windows)
{
    public async Task<bool> OpenAsync(string connectionId, string connectionName, CancellationToken cancellationToken)
    {
        // The same params a page passes to windows.open: a JSON object the editor's page reads with windows.params().
        var parameters = new JsonObject { ["connectionId"] = connectionId }.ToJsonString();
        var opened = await windows.OpenAsync("queryEditor", parameters, cancellationToken);
        return opened switch
        {
            IAppWindow editor => Named(editor, connectionName),
            WindowOpenError.NotOpened => false,
        };
    }

    // Closing every editor before a restore: each through its own guards, which may keep one open.
    public async Task<bool> CloseAllAsync()
    {
        var editors = windows.All.Where(window => window.Kind == "queryEditor").ToList();
        var results = await Task.WhenAll(editors.Select(editor => editor.CloseAsync()));
        return results.All(closed => closed);
    }

    private static bool Named(IAppWindow editor, string connectionName)
    {
        editor.SetTitle($"Query — {connectionName}");
        return true;
    }
}
