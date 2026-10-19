using System.Text.Json;

namespace Sotsera.Spar.Development;

public sealed partial class DevelopmentApplication
{
    private readonly object _inspectionGate = new();
    private Task? _inspectionTask;

    private void DispatchInspection(DevelopmentCommand request)
    {
        lock (_inspectionGate)
        {
            if (_ending)
            {
                return;
            }

            if (_inspectionTask is { IsCompleted: false })
            {
                Send(new SessionMessage("inspection-result", Document: request.Document, Detail: "{\"code\":\"inspection_busy\"}",
                    Generation: _generation, Correlation: request.Id));
                return;
            }

            _inspectionTask = InspectAsync(request);
        }
    }

    private async Task InspectAsync(DevelopmentCommand request)
    {
        var code = "inspection_unavailable";
        string? detail = null;
#if DEBUG
        try
        {
            if (_window is not null && request.Document is not null && request.Detail is not null)
            {
                var result = await _window.InspectAsync(request.Document, request.Detail, request.Selector, request.Text)
                    .ConfigureAwait(false);
                code = result.Code;
                detail = result.Json;
                if (result.Screenshot is { } bytes)
                {
                    var directory = UserDataFolder + ".inspection";
                    Directory.CreateDirectory(directory);
                    var path = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".png");
                    await File.WriteAllBytesAsync(path, bytes).ConfigureAwait(false);
                    detail = JsonSerializer.Serialize(path, SessionJsonContext.Default.String);
                }
            }
        }
        catch (Exception)
        {
            code = "inspection_failed";
        }
#else
        await Task.CompletedTask.ConfigureAwait(false);
#endif
        // Detail is already JSON, either the bounded DOM/log result or a generated artifact path.
        var payload = "{\"code\":" + JsonSerializer.Serialize(code, SessionJsonContext.Default.String)
            + ",\"result\":" + (detail ?? "null") + "}";
        var message = new SessionMessage("inspection-result", Document: request.Document, Detail: payload,
            Generation: _generation, Correlation: request.Id);
        // Nested JSON escaping counts toward the transport bound too. Oversized data must not end the session.
        if (JsonSerializer.SerializeToUtf8Bytes(message, SessionJsonContext.Default.SessionMessage).Length > 12 * 1024)
        {
            message = message with { Detail = "{\"code\":\"result_too_large\",\"result\":null}" };
        }
        Send(message);
    }
}
