using Microsoft.Extensions.Logging;

namespace Spar.Logging;

/// <summary>
/// The <see cref="ILoggerFactory"/> of an app that adds no logging of its own: every <see cref="ILoggerProvider"/> in
/// the app's services, Spar's file among them, gets each category's lines, each provider filtering by itself; scopes
/// are not passed on. An app that calls <c>AddLogging</c> gets Microsoft.Extensions.Logging's factory instead, which
/// takes Spar's provider from the services too. Spar has its own because that factory, with its options and filter
/// rules, cost a NativeAOT app 96,256 B (<c>docs/evidence/m1b-logging.md</c>); this one is kept small, since each
/// logger type's <c>Log</c> is compiled again for every kind of message state in the image.
/// </summary>
internal sealed class SparLoggerFactory : ILoggerFactory
{
    private readonly Lock _gate = new();
    private readonly List<ILoggerProvider> _providers;
    private readonly List<ILoggerProvider> _added = [];
    private readonly Dictionary<string, FanOutLogger> _loggers = new(StringComparer.Ordinal);
    private bool _disposed;

    public SparLoggerFactory(IEnumerable<ILoggerProvider> providers)
    {
        _providers = [.. providers];
    }

    public ILogger CreateLogger(string categoryName)
    {
        lock (_gate)
        {
            if (!_loggers.TryGetValue(categoryName, out var logger))
            {
                var loggers = new ILogger[_providers.Count];
                for (var index = 0; index < loggers.Length; index++)
                {
                    loggers[index] = _providers[index].CreateLogger(categoryName);
                }

                logger = new FanOutLogger(loggers);
                _loggers.Add(categoryName, logger);
            }

            return logger;
        }
    }

    /// <summary>Adds a provider for every logger, those created already included; the factory disposes it.</summary>
    public void AddProvider(ILoggerProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _providers.Add(provider);
            _added.Add(provider);
            foreach (var (category, logger) in _loggers)
            {
                logger.Add(provider.CreateLogger(category));
            }
        }
    }

    /// <summary>Disposes the providers added through <see cref="AddProvider"/>; the services dispose their own.</summary>
    public void Dispose()
    {
        ILoggerProvider[] added;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            added = [.. _added];
        }

        foreach (var provider in added)
        {
            provider.Dispose();
        }
    }

    /// <summary>One category's loggers, one per provider, each asked whether it takes the level.</summary>
    private sealed class FanOutLogger(ILogger[] loggers) : ILogger
    {
        private ILogger[] _loggers = loggers;

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel)
        {
            foreach (var logger in _loggers)
            {
                if (logger.IsEnabled(logLevel))
                {
                    return true;
                }
            }

            return false;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            foreach (var logger in _loggers)
            {
                logger.Log(logLevel, eventId, state, exception, formatter);
            }
        }

        /// <summary>Called under the factory's lock; readers see the old or the new array, never a torn one.</summary>
        public void Add(ILogger logger) => _loggers = [.. _loggers, logger];
    }
}
