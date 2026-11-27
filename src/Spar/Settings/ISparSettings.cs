using System.ComponentModel;
using System.Text.Json;
using Spar.Runtime;

namespace Spar
{
    /// <summary>
    /// One settings record of the app, declared in <c>Program.cs</c> with <c>spar.Settings(defaults)</c> and injected
    /// where it is needed. The page reaches it only through the app's own contracts.
    /// </summary>
    public interface ISparSettings<T>
        where T : class
    {
        /// <summary>The settings now: the file's, laid over the defaults.</summary>
        T Current { get; }

        /// <summary>Where <see cref="Current"/> came from.</summary>
        SparSettingsSource Source { get; }

        /// <summary>
        /// Applies <paramref name="change"/> to the settings and writes them at once, one update at a time. On a network
        /// file the change runs on the file as it is just before writing, so a change another machine wrote meanwhile
        /// stays. Throws <see cref="SparSettingsUnavailableException"/> when the file cannot be written now.
        /// </summary>
        Task<T> UpdateAsync(Func<T, T> change, CancellationToken cancellationToken = default);

        /// <summary>The settings now, then each update, until <paramref name="cancellationToken"/> ends the watch.</summary>
        IAsyncEnumerable<T> WatchAsync(CancellationToken cancellationToken = default);
    }

    /// <summary>Where a settings record's current value came from.</summary>
    public enum SparSettingsSource
    {
        /// <summary>Its file.</summary>
        File,

        /// <summary>The defaults: there is no file yet, or it was unreadable and was set aside.</summary>
        Defaults,

        /// <summary>The share was unreachable: the last copy Spar kept locally, read-only until the share is back.</summary>
        LocalCopy,

        /// <summary>The file was written by a newer version of the app: the defaults, and the file left as it is, read-only.</summary>
        NewerVersion,
    }

    /// <summary>A settings file cannot be written now: the share is unreachable, or a newer version of the app wrote it.</summary>
    public sealed class SparSettingsUnavailableException(string message, Exception? inner = null) : IOException(message, inner);
}

namespace Spar.Runtime
{
    /// <summary>A settings record's generated reader and writer, which the build hands the runtime.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public abstract class SparSettingsSerializer
    {
        internal SparSettingsSerializer()
        {
        }
    }

    /// <summary>The generated reader and writer of the settings record <typeparamref name="T"/>.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public sealed class SparSettingsSerializer<T>(SparReader<T> read, Action<Utf8JsonWriter, T> write) : SparSettingsSerializer
    {
        public SparReader<T> Read { get; } = read;

        public Action<Utf8JsonWriter, T> Write { get; } = write;
    }
}
