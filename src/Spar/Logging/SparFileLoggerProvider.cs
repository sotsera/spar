using Microsoft.Extensions.Logging;

namespace Spar.Logging;

/// <summary>
/// Spar's provider for the app's log file (decision "Logging's public shape"), in every app's services: each logger
/// writes its category's entries at or above its level (<see cref="LogLevels"/>) to the one <see cref="LogFile"/> of the
/// run. A host that opened the file before the services existed shares it and closes it itself at process exit; a test
/// host's provider opens its own file and closes it with the services. Its alias is <c>SparFile</c>, for an app whose
/// own Microsoft.Extensions.Logging factory filters by provider.
/// </summary>
[ProviderAlias("SparFile")]
internal sealed class SparFileLoggerProvider(LogFile file, LogLevels levels, bool ownsFile) : ILoggerProvider, IAsyncDisposable
{
    public LogFile File => file;

    public ILogger CreateLogger(string categoryName) => new FileLogger(file, categoryName, levels.For(categoryName));

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
/// time, level and exception are written by the file's task. Scopes are not written.
/// </summary>
internal sealed class FileLogger(LogFile file, string category, LogLevel minimum) : ILogger
{
    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel >= minimum && logLevel != LogLevel.None;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
        {
            return;
        }

        var message = formatter(state, exception);
        file.Write(new LogEntry(DateTime.UtcNow, logLevel, category, message, exception));
    }
}
