using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;

namespace Spar;

/// <summary>Puts a command, or every command of a contract, in a serial group: one at a time, app-wide, in arrival order.</summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class | AttributeTargets.Interface, Inherited = false)]
public sealed class SparSerialAttribute(string group) : Attribute
{
    public string Group { get; } = group;
}

/// <summary>A command the page loads by its address (<c>url(request)</c>), run at each load; nothing is kept.</summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class SparResourceAttribute : Attribute;

/// <summary>Binary data the page reads on the app's own origin: a handle in a result, or a resource's answer.</summary>
public sealed class SparBlob
{
    private readonly byte[]? _bytes;
    private readonly string? _path;

    private SparBlob(byte[]? bytes, string? path, long length, string contentType, string? name)
    {
        _bytes = bytes;
        _path = path;
        Length = length;
        ContentType = contentType;
        Name = name;
    }

    public string ContentType { get; }

    public string? Name { get; }

    public long Length { get; }

    public static SparBlob FromBytes(byte[] bytes, string contentType, string? name = null) =>
        new(bytes, null, bytes.LongLength, contentType, name);

    public static SparBlob FromFile(string path, string contentType, string? name = null) =>
        new(null, path, new FileInfo(path).Length, contentType, name ?? Path.GetFileName(path));

    public Stream OpenRead() => _bytes is not null ? new MemoryStream(_bytes, writable: false) : File.OpenRead(_path!);
}

/// <summary>Makes blobs from streams, copied to a temporary file in the app's cache folder.</summary>
public sealed class SparBlobs
{
    public async Task<SparBlob> FromStreamAsync(Stream stream, string contentType, string? name = null, CancellationToken cancellationToken = default)
    {
        using var copy = new MemoryStream();
        await stream.CopyToAsync(copy, cancellationToken);
        return SparBlob.FromBytes(copy.ToArray(), contentType, name);
    }
}

/// <summary>Where the current settings came from.</summary>
public enum SparSettingsSource
{
    File,
    Defaults,
    LocalCopy,
    NewerVersion,
}

/// <summary>One settings record: read at start, written at once on each update, watched by the app's services.</summary>
public interface ISparSettings<T>
    where T : class
{
    T Current { get; }

    SparSettingsSource Source { get; }

    Task<T> UpdateAsync(Func<T, T> change, CancellationToken cancellationToken);

    IAsyncEnumerable<T> WatchAsync(CancellationToken cancellationToken);
}

/// <summary>Where a settings record lives and how an older file is read.</summary>
public sealed class SettingsOptions
{
    internal SettingsOptions()
    {
    }

    public SettingsOptions Name(string name) => this;

    public SettingsOptions File(string path) => this;

    public SettingsOptions AllowNetworkFile() => this;

    public SettingsOptions Version(int version) => this;

    public SettingsOptions Migrate(int from, Func<JsonObject, JsonObject> migrate) => this;
}

/// <summary>Which keys of the installation file the code keeps as it set them.</summary>
public sealed class InstallationOptions
{
    internal InstallationOptions()
    {
    }

    public InstallationOptions Locked(params string[] keys) => this;
}

/// <summary>Stand-in: the settings in memory, as the defaults.</summary>
internal sealed class MemorySettings<T>(T defaults) : ISparSettings<T>
    where T : class
{
    public T Current { get; private set; } = defaults;

    public SparSettingsSource Source => SparSettingsSource.Defaults;

    public Task<T> UpdateAsync(Func<T, T> change, CancellationToken cancellationToken)
    {
        Current = change(Current);
        return Task.FromResult(Current);
    }

    public async IAsyncEnumerable<T> WatchAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.Yield();
        yield return Current;
    }
}

/// <summary>The settings and installation-file calls of <c>Program.cs</c>.</summary>
public static class DataBuilder
{
    extension(SparBuilder builder)
    {
        public SparBuilder Settings<T>(T defaults, Action<SettingsOptions>? configure = null)
            where T : class
        {
            configure?.Invoke(new SettingsOptions());
            builder.Services.AddSingleton<ISparSettings<T>>(new MemorySettings<T>(defaults));
            return builder;
        }

        public SparBuilder InstallationFile(Action<InstallationOptions>? configure = null)
        {
            configure?.Invoke(new InstallationOptions());
            return builder;
        }
    }
}
