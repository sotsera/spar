#if DEBUG
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Sotsera.Spar.Windows.Interop;

namespace Sotsera.Spar.Windows;

public sealed record SparInspectionResult(string Code, string? Json = null, byte[]? Screenshot = null);

internal sealed record InspectionScriptRequest(string Source, string Operation, string? Selector, string? Text);
internal sealed record InspectionLog(string Kind, string? ObservedDocument, string Detail);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(InspectionScriptRequest))]
[JsonSerializable(typeof(InspectionLog[]))]
[JsonSerializable(typeof(string))]
internal sealed partial class InspectionJsonContext : JsonSerializerContext;

public sealed unsafe partial class SparWindow
{
    private readonly Queue<InspectionLog> _inspectionLogs = new();
    private bool _inspectionBusy;
    private int _inspectionRequests;
    private int _inspectionLogsDropped;

    /// <summary>Executes one fixed operation against the specified native document. Debug builds only.</summary>
    /// <remarks>Completion means dispatch/capture ended, not that application work initiated by a click ended.</remarks>
    public Task<SparInspectionResult> InspectAsync(string document, string operation, string? selector = null, string? text = null)
    {
        var completion = new TaskCompletionSource<SparInspectionResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_posted)
        {
            if (_closed || _closeRequested || !_options.EnableDevelopmentInspection)
            {
                completion.SetResult(new("inspection_unavailable"));
                return completion.Task;
            }

            if (Interlocked.CompareExchange(ref _inspectionRequests, 1, 0) != 0)
            {
                completion.SetResult(new("inspection_busy"));
                return completion.Task;
            }

            _ = completion.Task.ContinueWith(_ => Interlocked.Exchange(ref _inspectionRequests, 0),
                CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            try
            {
                RunBridgeUi(() => BeginInspection(document, operation, selector, text, completion));
            }
            catch (Exception)
            {
                completion.TrySetResult(new("inspection_unavailable"));
            }
        }

        return completion.Task;
    }

    private void BeginInspection(string document, string operation, string? selector, string? text,
        TaskCompletionSource<SparInspectionResult> completion)
    {
        if (!InspectionDocumentMatches(document))
        {
            completion.SetResult(new("document_retired"));
            return;
        }

        if (_inspectionBusy)
        {
            completion.SetResult(new("inspection_busy"));
            return;
        }

        if (operation is not ("snapshot" or "screenshot" or "logs" or "click" or "type")
            || selector?.Length > 512 || text?.Length > 1024
            || (operation is "click" or "type" && string.IsNullOrWhiteSpace(selector))
            || (operation == "type" && text is null))
        {
            completion.SetResult(new("invalid_inspection"));
            return;
        }

        if (operation == "logs")
        {
            var logs = JsonSerializer.Serialize(_inspectionLogs.ToArray(), InspectionJsonContext.Default.InspectionLogArray);
            completion.SetResult(new("ok", "{\"dropped\":" + _inspectionLogsDropped + ",\"entries\":" + logs + "}"));
            return;
        }

        _inspectionBusy = true;
        _pendingNativeCallbacks++;
        try
        {
            void Completed(int error, nint pointer)
            {
                try
                {
                    if (!InspectionDocumentMatches(document))
                    {
                        completion.TrySetResult(new("document_retired"));
                        return;
                    }
                    Check(error, "Complete development inspection");
                    // Callback strings are borrowed, unlike strings returned by WebView property getters.
                    var limit = operation == "screenshot" ? 8 * 1024 * 1024 : 12 * 1024;
                    var json = ReadBorrowedInspectionText(pointer, limit);
                    if (json is null)
                    {
                        completion.TrySetResult(new("result_too_large"));
                        return;
                    }
                    using var parsed = JsonDocument.Parse(json);
                    if (operation == "screenshot")
                    {
                        var bytes = Convert.FromBase64String(parsed.RootElement.GetProperty("data").GetString()!);
                        completion.TrySetResult(bytes.Length <= 4 * 1024 * 1024
                            ? new("ok", Screenshot: bytes) : new("result_too_large"));
                    }
                    else
                    {
                        completion.TrySetResult(new(parsed.RootElement.GetProperty("code").GetString()!, json));
                    }
                }
                catch (Exception exception)
                {
                    completion.TrySetResult(new("inspection_failed", JsonSerializer.Serialize(exception.Message,
                        InspectionJsonContext.Default.String)));
                }
                finally
                {
                    _inspectionBusy = false;
                    _pendingNativeCallbacks--;
                    ScheduleFinish();
                }
            }

            if (operation == "screenshot")
            {
                CallInspectionProtocol("Page.captureScreenshot", "{\"format\":\"png\",\"captureBeyondViewport\":false}", Completed);
            }
            else
            {
                using var stream = typeof(SparWindow).Assembly.GetManifestResourceStream("Spar.Inspection.js")!;
                using var reader = new StreamReader(stream);
                var request = new InspectionScriptRequest(_document!.Source, operation, selector, text);
                var script = "(" + reader.ReadToEnd() + ")(" + JsonSerializer.Serialize(request,
                    InspectionJsonContext.Default.InspectionScriptRequest) + ")";
                var callback = new ScriptCompleted(Completed);
                ComReferences.WithCallback(callback, typeof(IScriptCompleted).GUID, handler =>
                {
                    fixed (char* value = script)
                    {
                        Check(_webView!.ExecuteScript((nint)value, handler), "Execute fixed inspection operation");
                    }
                });
            }
        }
        catch (Exception)
        {
            _inspectionBusy = false;
            _pendingNativeCallbacks--;
            completion.TrySetResult(new("inspection_failed"));
            ScheduleFinish();
        }
    }

    private bool InspectionDocumentMatches(string document)
    {
        return !_closeRequested && _trustedNavigation && _document is { IsActive: true }
            && _document.Id == document && _document.NavigationId == _navigationId;
    }

    partial void ConfigureInspection()
    {
        if (!_options.EnableDevelopmentInspection)
        {
            return;
        }
        foreach (var name in new[] { "Runtime.consoleAPICalled", "Runtime.exceptionThrown" })
        {
            nint pointer;
            fixed (char* value = name)
            {
                Check(_webView!.GetDevToolsProtocolEventReceiver((nint)value, out pointer), "Get development log receiver");
            }
            var receiver = ComReferences.Take<IProtocolReceiver>(pointer);
            var callback = new ProtocolReceived(argsPointer => RecordInspectionLog(name, argsPointer));
            try
            {
                ComReferences.WithCallback(callback, typeof(IProtocolReceived).GUID, handler =>
                {
                    Check(receiver.AddReceived(handler, out var token), "Subscribe development logs");
                    _documentSubscriptions.Add(() =>
                    {
                        try
                        {
                            Check(receiver.RemoveReceived(token), "Unsubscribe development logs");
                        }
                        finally
                        {
                            ComReferences.Release(receiver);
                        }
                    });
                });
            }
            catch
            {
                ComReferences.Release(receiver);
                throw;
            }
        }

        _pendingNativeCallbacks++;
        try
        {
            CallInspectionProtocol("Runtime.enable", "{}", (error, _) =>
            {
                if (error < 0)
                {
                    _inspectionLogs.Enqueue(new("inspector", null, "Runtime logging unavailable."));
                }
                _pendingNativeCallbacks--;
                ScheduleFinish();
            });
        }
        catch
        {
            _pendingNativeCallbacks--;
            throw;
        }
    }

    private void RecordInspectionLog(string kind, nint pointer)
    {
        var args = ComReferences.Borrow<IProtocolEventArgs>(pointer);
        try
        {
            Check(args.GetJson(out var jsonPointer), "Read development log");
            var json = ReadNativeText(jsonPointer, 32768);
            if (_inspectionLogs.Count >= 12)
            {
                _inspectionLogs.Dequeue();
                _inspectionLogsDropped++;
            }
            var detail = json is null ? "[oversized event]" : json.Length > 400 ? json[..400] + " [truncated]" : json;
            _inspectionLogs.Enqueue(new(kind, _document?.Id, detail));
        }
        catch (Exception)
        {
            _inspectionLogsDropped++;
        }
        finally
        {
            ComReferences.Release(args);
        }
    }

    private void CallInspectionProtocol(string method, string parameters, Action<int, nint> completed)
    {
        var callback = new ProtocolCompleted(completed);
        ComReferences.WithCallback(callback, typeof(IProtocolCompleted).GUID, handler =>
        {
            fixed (char* name = method)
            fixed (char* json = parameters)
            {
                Check(_webView!.CallDevToolsProtocolMethod((nint)name, (nint)json, handler), "Call bounded development protocol operation");
            }
        });
    }

    private static string? ReadBorrowedInspectionText(nint pointer, int maximum)
    {
        if (pointer == 0)
        {
            return null;
        }
        var text = (char*)pointer;
        var length = 0;
        while (length <= maximum && text[length] != '\0')
        {
            length++;
        }

        return length > maximum ? null : new string(text, 0, length);
    }
}
#endif
