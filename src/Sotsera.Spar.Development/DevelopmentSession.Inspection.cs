using System.Text;
using System.Text.Json;

namespace Sotsera.Spar.Development;

public sealed partial class DevelopmentSession
{
    private readonly HashSet<string> _inspectionIds = new(StringComparer.Ordinal);
    private PendingInspection? _inspection;

    private async Task BeginInspectionAsync(DevelopmentCommand request)
    {
        var error = ValidateInspection(request);
        if (error is not null)
        {
            Emit("command-rejected", code: error, correlation: request.Id);
            return;
        }

        _inspectionIds.Add(request.Id!);
        var pending = new PendingInspection(request, DateTimeOffset.UtcNow.AddMilliseconds(request.TimeoutMilliseconds));
        _inspection = pending;
        Emit("command-accepted", "application", _application!.Process, detail: "inspect:" + request.Detail,
            contract: _contract, document: _readyDocument, correlation: request.Id);
        try
        {
            await _applicationPeer!.Connection.WriteAsync(new SessionMessage("inspect", Inspection: request)).ConfigureAwait(false);
        }
        catch (IOException)
        {
            // A failed write does not prove that the action was never delivered. Keep it pending until retirement.
            Emit("command-timeout", code: "delivery_unknown", detail: "Execution remains unconfirmed; do not replay.",
                document: request.Document, contract: request.Contract, correlation: request.Id);
            pending.TimedOut = true;
        }
    }

    private string? ValidateInspection(DevelopmentCommand request)
    {
        if (!enableInspection)
        {
            return "inspection_disabled";
        }

        if (string.IsNullOrWhiteSpace(request.Id) || request.Id.Length > 64
            || !request.Id.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_')
            || request.TimeoutMilliseconds is < 50 or > 30000
            || request.Selector?.Length > 512 || request.Text?.Length > 1024
            || request.Detail is not ("snapshot" or "screenshot" or "logs" or "click" or "type"))
        {
            return "invalid_inspection";
        }

        if (_inspectionIds.Contains(request.Id))
        {
            return "duplicate_id";
        }

        if (_inspectionIds.Count >= 1024)
        {
            return "session_command_limit";
        }

        if (request.Session != _session || request.Generation != _generation || request.Contract != _contract
            || request.Document is null || request.Document != _readyDocument || _applicationPeer is null
            || _application is null || _applicationControlLost)
        {
            return "stale_target";
        }

        return _inspection is null ? null : "inspection_busy";
    }

    private void CheckInspectionDeadline()
    {
        if (_inspection is { TimedOut: false } pending && DateTimeOffset.UtcNow >= pending.Deadline)
        {
            pending.TimedOut = true;
            Emit("command-timeout", "application", _application?.Process, code: "unfinished",
                detail: "The action may still execute. It remains tracked and will not be replayed.",
                contract: pending.Request.Contract, document: pending.Request.Document, correlation: pending.Request.Id);
        }
    }

    private void CompleteInspection(SessionMessage message, bool retiring = false)
    {
        if (_inspection is not { } pending || message.Correlation != pending.Request.Id
            || message.Document != pending.Request.Document || message.Generation != pending.Request.Generation)
        {
            return;
        }
        var current = !retiring && pending.Request.Document == _readyDocument;
        var code = "document_retired";
        if (current && message.Detail is { } detail)
        {
            using var parsed = JsonDocument.Parse(detail);
            code = parsed.RootElement.GetProperty("code").GetString() ?? "inspection_failed";
        }
        Emit("command-completed", "application", _application?.Process, code: code,
            detail: current ? message.Detail : "Result discarded because the target document retired.",
            document: pending.Request.Document, contract: pending.Request.Contract, correlation: pending.Request.Id);
        _inspection = null;
    }

    private void EndRetiredInspection()
    {
        if (_inspection is not { } pending)
        {
            return;
        }
        Emit("command-completed", "application", _application?.Process, code: "process_ended",
            detail: "The process exited; action outcome is unknown and no action was replayed.",
            document: pending.Request.Document, contract: pending.Request.Contract, correlation: pending.Request.Id);
        _inspection = null;
    }

    private void InspectDiagnostics(DevelopmentCommand request)
    {
        if (!enableInspection)
        {
            Emit("command-rejected", code: "inspection_disabled", correlation: request.Id);
            return;
        }
        Emit("command-accepted", detail: "diagnostics", correlation: request.Id);
        // Only coordinator-created logs in its own directory; the owner cannot submit a path.
        foreach (var path in Directory.EnumerateFiles(_directory, "*.log").Order(StringComparer.Ordinal).TakeLast(8))
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var length = stream.Length;
            var truncated = length > 2048;
            stream.Seek(Math.Max(0, length - 2048), SeekOrigin.Begin);
            var bytes = new byte[(int)Math.Min(length, 2048)];
            var count = stream.Read(bytes, 0, bytes.Length);
            var detail = Encoding.UTF8.GetString(bytes, 0, count);
            Emit("diagnostic-log", code: truncated ? "tail" : "complete", detail: Path.GetFileName(path) + "\n" + detail,
                correlation: request.Id, document: _currentDocument, contract: _contract);
        }
        Emit("command-completed", code: "diagnostics", correlation: request.Id, document: _currentDocument, contract: _contract);
    }

    private sealed class PendingInspection(DevelopmentCommand request, DateTimeOffset deadline)
    {
        internal DevelopmentCommand Request { get; } = request;
        internal DateTimeOffset Deadline { get; } = deadline;
        internal bool TimedOut { get; set; }
    }
}
