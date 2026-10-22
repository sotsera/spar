using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sotsera.Spar.Development;

internal sealed record SessionMessage(string Kind, string? Token = null, string? Session = null,
    int? ProcessId = null, string? Url = null, string? Contract = null, string? Document = null,
    string? Detail = null, string? Credential = null, string? Profile = null, int Generation = 1, long? Window = null,
    long? UpdateRevision = null, string? Correlation = null, bool InspectionEnabled = false,
    DevelopmentCommand? Inspection = null);

public sealed record DevelopmentCommand(string Command, string? Id = null, string? Detail = null, bool Force = false,
    int Generation = 0, string? Session = null, string? Document = null, string? Contract = null,
    string? Selector = null, string? Text = null, int TimeoutMilliseconds = 5000);

/// <summary>Private owner handoff. Environment contains capabilities and must not be persisted in event logs.</summary>
public sealed record DevelopmentDebugLaunch(string Kind, string Session, int Generation, string Project,
    string Application, Dictionary<string, string> Environment, string[] Arguments);

public sealed record DevelopmentEvent(string Kind, string Session, int Generation, DateTimeOffset ObservedAt,
    int CoordinatorProcessId, string? Role = null, int? ProcessId = null, DateTime? ProcessStartedAtUtc = null,
    string? Code = null, string? Detail = null, long? Window = null, int? Port = null, string? Contract = null,
    string? Document = null, string? Correlation = null, long? UpdateRevision = null,
    string? BuildSourceHash = null, long? SourceRevision = null, DevelopmentProcessDiagnostic? Diagnostic = null);

/// <summary>Retained process output locations. A missing exit code means exit was not observed at failure time.</summary>
public sealed record DevelopmentProcessDiagnostic(string Role, int? ExitCode, string? StdoutLog, string? StderrLog,
    string NextAction);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, MaxDepth = 16,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(DevelopmentEvent))]
[JsonSerializable(typeof(DevelopmentCommand))]
[JsonSerializable(typeof(DevelopmentSettings))]
[JsonSerializable(typeof(DevelopmentDebugLaunch))]
public sealed partial class DevelopmentJsonContext : JsonSerializerContext;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, MaxDepth = 16,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(SessionMessage))]
[JsonSerializable(typeof(string))]
internal sealed partial class SessionJsonContext : JsonSerializerContext;

internal sealed class SessionConnection(PipeStream pipe) : IAsyncDisposable
{
    private readonly SemaphoreSlim _writeGate = new(1);
    private readonly BoundedLineReader _reader = new(pipe);

    internal PipeStream Pipe => pipe;

    internal async Task<SessionMessage?> ReadAsync(CancellationToken cancellationToken = default)
    {
        var line = await _reader.ReadAsync(cancellationToken).ConfigureAwait(false);
        return line is null ? null : JsonSerializer.Deserialize(line, SessionJsonContext.Default.SessionMessage)
            ?? throw new InvalidDataException("Null control frame.");
    }

    internal async Task WriteAsync(SessionMessage message, CancellationToken cancellationToken = default)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(message, SessionJsonContext.Default.SessionMessage);
        if (bytes.Length > BoundedLineReader.MaximumBytes)
        {
            throw new InvalidDataException("Control frame exceeds 16 KiB.");
        }
        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await pipe.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
            await pipe.WriteAsync("\n"u8.ToArray(), cancellationToken).ConfigureAwait(false);
            await pipe.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally { _writeGate.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        await pipe.DisposeAsync().ConfigureAwait(false);
        _writeGate.Dispose();
    }
}

/// <summary>A bounded UTF-8 line reader shared by private control pipes and the owner's standard input.</summary>
public sealed class BoundedLineReader(Stream stream)
{
    public const int MaximumBytes = 16 * 1024;
    private static readonly UTF8Encoding _utf8 = new(false, true);
    private readonly byte[] _buffer = new byte[4096];
    private int _offset;
    private int _count;

    public async Task<string?> ReadAsync(CancellationToken cancellationToken = default)
    {
        using var line = new MemoryStream();
        while (true)
        {
            if (_offset == _count)
            {
                _offset = 0;
                _count = await stream.ReadAsync(_buffer, cancellationToken).ConfigureAwait(false);
                if (_count == 0)
                {
                    if (line.Length != 0)
                    {
                        throw new InvalidDataException("Incomplete control frame.");
                    }
                    return null;
                }
            }

            var boundary = Array.IndexOf(_buffer, (byte)'\n', _offset, _count - _offset);
            var length = boundary < 0 ? _count - _offset : boundary - _offset;
            if (line.Length + length > MaximumBytes)
            {
                throw new InvalidDataException("Control frame exceeds 16 KiB.");
            }
            line.Write(_buffer, _offset, length);
            _offset += length;
            if (boundary >= 0)
            {
                _offset++;
                return _utf8.GetString(line.GetBuffer(), 0, (int)line.Length).TrimEnd('\r');
            }
        }
    }
}
