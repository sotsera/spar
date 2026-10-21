#if DEBUG
using System.Text.Json;
using Sotsera.Spar.Bridge;
using Sotsera.Spar.Windows.Interop;

namespace Sotsera.Spar.Windows;

public sealed unsafe partial class SparWindow
{
    private string? _readinessDocument;
    private string? _readinessMessage;

    private void ScheduleDevelopmentReadiness(string document, string reply)
    {
        if (!_options.EnableDevelopmentReadiness || _options.LoopbackContent is null
            || _closeRequested || !_trustedNavigation || _document is not { IsActive: true } current
            || current.Id != document || current.NavigationId != _navigationId || _readinessDocument == document)
        {
            return;
        }

        using var parsed = JsonDocument.Parse(reply);
        if (parsed.RootElement.GetProperty("kind").GetString() != "result"
            || parsed.RootElement.GetProperty("status").GetString() != "ok")
        {
            return;
        }

        // One bounded request per native document. A frame signal cannot grant bridge permissions or run backend work.
        _readinessDocument = document;
        var token = Guid.NewGuid().ToString("N");
        _readinessMessage = "\"spar-development-ready:" + token + "\"";
        var script = "requestAnimationFrame(() => requestAnimationFrame(() => "
            + "window.chrome.webview.postMessage('spar-development-ready:" + token + "')))";
        _pendingNativeCallbacks++;
        try
        {
            var callback = new ScriptCompleted((error, _) =>
            {
                try
                {
                    Check(error, "Schedule development frame readiness");
                }
                catch (Exception exception)
                {
                    _failure ??= exception;
                    RequestClose();
                }
                finally
                {
                    _pendingNativeCallbacks--;
                    ScheduleFinish();
                }
            });
            ComReferences.WithCallback(callback, typeof(IScriptCompleted).GUID, handler =>
            {
                fixed (char* value = script)
                {
                    Check(_webView!.ExecuteScript((nint)value, handler), "Observe development frame readiness");
                }
            });
        }
        catch
        {
            _pendingNativeCallbacks--;
            throw;
        }
    }

    private bool AcceptDevelopmentReadiness(SparBridgeDocument document, string source, string message)
    {
        if (_readinessMessage is null || message != _readinessMessage || document.Id != _readinessDocument
            || !document.IsActive || document.NavigationId != _navigationId || source != document.Source)
        {
            return false;
        }

        Check(_webView!.GetSource(out var sourcePointer), "Recheck development readiness source");
        if (ReadNativeText(sourcePointer, 2048) != document.Source)
        {
            return false;
        }

        _readinessMessage = null;
        BridgeEvent(new SparBridgeEvent("development-ready", document.Id, 0, null, null));
        return true;
    }
}
#endif
