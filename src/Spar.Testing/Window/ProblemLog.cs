using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Spar.Testing;

/// <summary>
/// The Window surface's problems for <see cref="SparTestApp.Problems"/>: every warning and error Spar logs for the app
/// (categories <c>Spar.*</c>: the host, its bridge's refusals and drops, the page's CSP violations, and the page's
/// errors under <c>Spar.Page</c>), added to the app's own logging as one more provider, so a clean window run reads
/// as one with no problems, as on the Browser surface.
/// </summary>
internal sealed class ProblemLog : ILoggerProvider
{
    private readonly ConcurrentQueue<string> _problems = new();

    public IReadOnlyCollection<string> Problems => _problems;

    public ILogger CreateLogger(string categoryName) => new Writer(this, categoryName);

    public void Dispose()
    {
    }

    private sealed class Writer(ProblemLog log, string category) : ILogger
    {
        private readonly bool _spar = category.StartsWith("Spar.", StringComparison.Ordinal);

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => _spar && logLevel is >= LogLevel.Warning and < LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel))
            {
                log._problems.Enqueue($"{logLevel} {category}: {formatter(state, exception)}");
            }
        }
    }
}
