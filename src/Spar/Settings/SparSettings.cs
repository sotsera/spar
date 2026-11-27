using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Spar.Runtime;

namespace Spar.Settings;

/// <summary>
/// One settings record (decision "M3b's decide-first questions", item 4): read once at start, updated one at a time,
/// each update written at once. On a network file (<see cref="SettingsOptions.AllowNetworkFile"/>), a copy is kept in
/// the local data folder for when the share cannot be reached, and each update reads the share's file again first.
/// </summary>
internal sealed class SparSettings<T> : ISparSettings<T>, IDisposable
    where T : class
{
    private readonly SettingsOptions _options;
    private readonly SparSettingsSerializer<T> _serializer;
    private readonly byte[] _defaults;
    private readonly T _defaultValue;
    private readonly string _path;
    private readonly string? _localCopy;
    private readonly ILogger _logger;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _updates = new(1, 1);
    private readonly Lock _gate = new();
    private readonly List<Channel<T>> _watchers = [];
    private T _current;
    private byte[]? _lastHash;

    private SparSettings(T defaults, SettingsOptions options, SparSettingsSerializer<T> serializer, string path, string? localCopy, ILogger logger, TimeProvider time)
    {
        _options = options;
        _serializer = serializer;
        _defaultValue = defaults;
        _defaults = SparJson.Write(defaults, serializer.Write).ToArray();
        _path = path;
        _localCopy = localCopy;
        _logger = logger;
        _time = time;
        _current = defaults;
    }

    public T Current
    {
        get
        {
            lock (_gate)
            {
                return _current;
            }
        }
    }

    public SparSettingsSource Source { get; private set; } = SparSettingsSource.Defaults;

    /// <summary>The file this record lives in, for the start-up log and the tests.</summary>
    internal string FilePath => _path;

    /// <summary>
    /// The record as <c>Program.cs</c> declared it, read from its file: the data folder's <c>&lt;name&gt;.json</c>, or the
    /// app's chosen path (with <c>.Development</c> in a Development build, under the test's root in a test).
    /// </summary>
    public static SparSettings<T> Load(T defaults, SettingsOptions options, IServiceProvider services, bool test, bool development)
    {
        var serializer = services.GetRequiredService<SparSettingsSerializers>().For<T>()
            ?? throw new InvalidOperationException(
                $"The build wrote no serializer for the settings record {typeof(T).Name}: spar.Settings<{typeof(T).Name}>(…) must be called in the app's own project.");
        return Load(
            defaults,
            options,
            serializer,
            services.GetRequiredService<SparFolders>(),
            services.GetRequiredService<ILoggerFactory>().CreateLogger("Spar.Settings"),
            services.GetService<TimeProvider>() ?? TimeProvider.System,
            test,
            development);
    }

    /// <summary>
    /// The record read from its file; <paramref name="network"/>, for Spar's own tests only, takes the file for one on a
    /// share whatever its path says, since a test cannot make a share.
    /// </summary>
    internal static SparSettings<T> Load(
        T defaults, SettingsOptions options, SparSettingsSerializer<T> serializer, SparFolders folders, ILogger logger, TimeProvider time,
        bool test, bool development, bool? network = null)
    {
        var path = PathOf(options, folders, test, development);
        var onShare = network ?? SparFolders.IsNetworkPath(path);
        if (onShare && !options.NetworkAllowed)
        {
            throw new InvalidOperationException(
                $"The settings file {path} is on a network share. Allow it with s.AllowNetworkFile(), whose documentation says what changes, or choose a local path.");
        }

        var localCopy = onShare ? Path.Combine(folders.Data, "settings-copies", options.SettingsName + ".json") : null;
        var settings = new SparSettings<T>(defaults, options, serializer, path, localCopy, logger, time);
        settings.ReadAtStart();
        return settings;
    }

    public async Task<T> UpdateAsync(Func<T, T> change, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(change);
        await _updates.WaitAsync(cancellationToken);
        try
        {
            if (_localCopy is not null)
            {
                // Another machine may have written the share's file since: the change applies to it as it is now.
                RereadShare();
            }

            if (Source is SparSettingsSource.LocalCopy or SparSettingsSource.NewerVersion)
            {
                throw new SparSettingsUnavailableException(Source == SparSettingsSource.NewerVersion
                    ? $"The settings file {_path} was written by a newer version of the app; this one does not change it."
                    : $"The settings file {_path} cannot be reached now; try again when it can.");
            }

            var next = change(Current) ?? throw new InvalidOperationException("A settings change returned null.");
            var settings = SparJson.Write(next, _serializer.Write);
            var bytes = SettingsFile.Wrap(_options.CurrentVersion, settings);
            try
            {
                SettingsFile.Write(_path, bytes);
            }
            catch (Exception exception) when (_localCopy is not null && exception is IOException or UnauthorizedAccessException)
            {
                throw new SparSettingsUnavailableException($"The settings file {_path} could not be written: {exception.Message}", exception);
            }

            Keep(next, bytes, SparSettingsSource.File);
            return next;
        }
        finally
        {
            _updates.Release();
        }
    }

    public async IAsyncEnumerable<T> WatchAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var watcher = Channel.CreateBounded<T>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });
        lock (_gate)
        {
            _watchers.Add(watcher);
            watcher.Writer.TryWrite(_current);
        }

        try
        {
            await foreach (var value in watcher.Reader.ReadAllAsync(cancellationToken))
            {
                yield return value;
            }
        }
        finally
        {
            lock (_gate)
            {
                _watchers.Remove(watcher);
            }
        }
    }

    public void Dispose() => _updates.Dispose();

    private static string PathOf(SettingsOptions options, SparFolders folders, bool test, bool development)
    {
        if (options.FilePath is not { } chosen)
        {
            return Path.Combine(folders.Data, options.SettingsName + ".json");
        }

        if (test)
        {
            // A test never reads or writes a file outside its own root, a chosen one included.
            return Path.Combine(folders.Data, "settings-files", Path.GetFileName(chosen));
        }

        return development
            ? Path.Combine(Path.GetDirectoryName(chosen)!, $"{Path.GetFileNameWithoutExtension(chosen)}.Development{Path.GetExtension(chosen)}")
            : chosen;
    }

    private void ReadAtStart()
    {
        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(_path);
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            // No file yet: the defaults, and the first update writes it.
            return;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            if (_localCopy is not null)
            {
                SettingsFile.LogUnreachable(_logger, _path, exception);
                UseLocalCopy();
                return;
            }

            // A local file that cannot be read is never taken for a broken one: it stays, and the app runs on the defaults.
            SettingsFile.LogUnreadable(_logger, _path, exception);
            return;
        }

        Apply(bytes);
    }

    /// <summary>Reads the share's file again before an update; unreachable, the update fails.</summary>
    private void RereadShare()
    {
        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(_path);
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new SparSettingsUnavailableException($"The settings file {_path} cannot be reached now: {exception.Message}", exception);
        }

        if (_lastHash is null || !SHA256.HashData(bytes).AsSpan().SequenceEqual(_lastHash) || Source != SparSettingsSource.File)
        {
            Apply(bytes);
        }
    }

    /// <summary>A file's bytes: read into the record, set aside when broken, left alone when a newer version wrote it.</summary>
    private void Apply(byte[] bytes)
    {
        var dropped = new List<string>();
        var parsed = SettingsFile.Parse(bytes, _options, _defaults, dropped);
        if (parsed.Kind == SettingsFile.Kind.Newer)
        {
            SettingsFile.LogNewer(_logger, _path, parsed.Version);
            Keep(_defaultValue, null, SparSettingsSource.NewerVersion);
            return;
        }

        T value;
        try
        {
            value = parsed.Kind == SettingsFile.Kind.Read
                ? SparJson.Read(parsed.Settings!, _serializer.Read)
                : throw new System.Text.Json.JsonException(parsed.Reason);
        }
        catch (System.Text.Json.JsonException exception)
        {
            SettingsFile.SetAside(_path, exception.Message, _time, _logger);
            Keep(_defaultValue, null, SparSettingsSource.Defaults);
            return;
        }

        SettingsFile.LogDropped(_logger, _path, dropped);
        Keep(value, bytes, SparSettingsSource.File);
    }

    private void UseLocalCopy()
    {
        try
        {
            var dropped = new List<string>();
            var parsed = SettingsFile.Parse(File.ReadAllBytes(_localCopy!), _options, _defaults, dropped);
            if (parsed.Kind == SettingsFile.Kind.Read)
            {
                lock (_gate)
                {
                    _current = SparJson.Read(parsed.Settings!, _serializer.Read);
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            // No usable copy: the defaults, still read-only until the share is back.
        }

        Source = SparSettingsSource.LocalCopy;
    }

    /// <summary>The new current value: remembered, copied locally for a network file, and told to every watcher.</summary>
    private void Keep(T value, byte[]? bytes, SparSettingsSource source)
    {
        _lastHash = bytes is null ? null : SHA256.HashData(bytes);
        Source = source;
        if (bytes is not null && _localCopy is not null)
        {
            try
            {
                SettingsFile.Write(_localCopy, bytes);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // The copy is a fallback; the share's file is the record.
            }
        }

        lock (_gate)
        {
            _current = value;
            foreach (var watcher in _watchers)
            {
                watcher.Writer.TryWrite(value);
            }
        }
    }
}

/// <summary>The settings records' serializers the build wrote, for <see cref="SparSettings{T}"/>.</summary>
internal sealed class SparSettingsSerializers(IReadOnlyList<SparSettingsSerializer> serializers)
{
    public SparSettingsSerializer<T>? For<T>() => serializers.OfType<SparSettingsSerializer<T>>().FirstOrDefault();
}
