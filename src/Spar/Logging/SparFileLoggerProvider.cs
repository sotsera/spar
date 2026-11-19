using Microsoft.Extensions.Logging;

namespace Spar.Logging;

/// <summary>
/// Spar's provider for the app's log file (decision "Logging's public shape"), in every app's services unless the app
/// calls <c>spar.Logging(l => l.WithoutFile())</c>: each logger writes its category's entries to the one
/// <see cref="LogFile"/> of the run, at the levels the logger factory's filter rules let through. A host that opened
/// the file before the services existed shares it and closes it itself at process exit; a test host's provider opens
/// its own file and closes it with the services. Its alias, <c>SparFile</c>, is what a filter rule for this provider
/// names.
/// </summary>
[ProviderAlias("SparFile")]
internal sealed class SparFileLoggerProvider(LogFile file, bool ownsFile) : ILoggerProvider, IAsyncDisposable
{
    public LogFile File => file;

    public ILogger CreateLogger(string categoryName) => new FileLogger(file, categoryName);

    public void Dispose()
    {
        if (ownsFile)
        {
            file.Close();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (ownsFile)
        {
            await file.CloseAsync().ConfigureAwait(false);
        }
    }
}

/// <summary>
/// One category's logger for the log file: the message is formatted on the caller's thread, which only queues it;
/// time, level and exception are written by the file's task. Scopes are not written. The factory filters by level.
/// </summary>
internal sealed class FileLogger(LogFile file, string category) : ILogger
{
    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (logLevel == LogLevel.None)
        {
            return;
        }

        var message = formatter(state, exception);
        file.Write(new LogEntry(DateTime.UtcNow, logLevel, category, message, exception));
    }
}
