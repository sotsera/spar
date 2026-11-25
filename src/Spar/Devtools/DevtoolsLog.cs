using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Spar.Devtools;

/// <summary>
/// The devtools' <c>logs</c>: one bounded, merged record of the backend's log (every <c>ILogger</c> category the app's
/// logging lets through), the page's console messages and errors, and Content Security Policy violations, each tagged by
/// source, newest last. It is a logger provider beside the app's own, so it sees what <c>app.log</c> sees, plus the
/// page's console, which the page automation hands it.
/// </summary>
internal sealed class DevtoolsLog(TimeProvider time) : ILoggerProvider
{
    public const int Capacity = 1000;

    /// <summary>The longest message kept; a longer one is cut, with a mark.</summary>
    private const int MaxMessage = 4000;

    private readonly Lock _gate = new();
    private readonly Queue<LogEntry> _entries = new(Capacity);

    /// <summary>Adds the provider to the app's logging, so every category reaches it.</summary>
    public static DevtoolsLog AddTo(IServiceCollection services, TimeProvider time)
    {
        var log = new DevtoolsLog(time);
        services.AddLogging(builder => builder.AddProvider(log));
        return log;
    }

    public ILogger CreateLogger(string categoryName) => new Writer(this, categoryName);

    /// <summary>A message of the page's console (<c>log</c>, <c>info</c>, <c>warn</c>, <c>error</c>, <c>debug</c>, …).</summary>
    public void PageConsole(string type, string text) => Add("page", type switch
    {
        "error" or "assert" => "error",
        "warning" or "warn" => "warn",
        "debug" or "trace" => "debug",
        _ => "info",
    }, "console." + type, text);

    /// <summary>An uncaught error or rejection of the page.</summary>
    public void PageError(string text) => Add("page", "error", "page error", text);

    public void Add(string source, string level, string category, string message)
    {
        var entry = new LogEntry(time.GetUtcNow(), source, level, category, message.Length > MaxMessage ? message[..MaxMessage] + " …" : message);
        lock (_gate)
        {
            if (_entries.Count == Capacity)
            {
                _entries.Dequeue();
            }

            _entries.Enqueue(entry);
        }
    }

    /// <summary>The entries at or after <paramref name="since"/>, oldest first, at most <paramref name="limit"/> (the newest).</summary>
    public IReadOnlyList<LogEntry> Since(DateTimeOffset since, int limit)
    {
        lock (_gate)
        {
            return [.. _entries.Where(entry => entry.Time >= since).TakeLast(limit)];
        }
    }

    public void Dispose()
    {
    }

    /// <summary>Content Security Policy events are the host's (category <c>Spar.Host</c>, the violation log's event names).</summary>
    private static string SourceOf(string category, EventId eventId) =>
        category == "Spar.Page" ? "page"
        : eventId.Name is { } name && name.StartsWith("Csp", StringComparison.Ordinal) ? "csp"
        : "backend";

    private static string LevelOf(LogLevel level) => level switch
    {
        LogLevel.Critical or LogLevel.Error => "error",
        LogLevel.Warning => "warn",
        LogLevel.Information => "info",
        _ => "debug",
    };

    private sealed class Writer(DevtoolsLog log, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var message = formatter(state, exception);
            if (exception is not null)
            {
                message += Environment.NewLine + exception;
            }

            log.Add(SourceOf(category, eventId), LevelOf(logLevel), category, message);
        }
    }
}

/// <summary>One line of the devtools' log.</summary>
internal sealed record LogEntry(DateTimeOffset Time, string Source, string Level, string Category, string Message)
{
    public void Write(Utf8JsonWriter writer)
    {
        writer.WriteStartObject();
        writer.WriteString("time", Time);
        writer.WriteString("source", Source);
        writer.WriteString("level", Level);
        writer.WriteString("category", Category);
        writer.WriteString("message", Message);
        writer.WriteEndObject();
    }
}
