using System.Buffers;
using System.Reflection.Metadata;
using System.Text.Json;
using Spar.Hosting;

[assembly: MetadataUpdateHandler(typeof(Spar.HotReload.BackendUpdates))]

namespace Spar.HotReload;

/// <summary>
/// The Hot Reload signal (plan section 4, "Hot Reload signal"): after each C# edit that Hot Reload applies, the runtime
/// calls <see cref="UpdateApplication"/> (the <see cref="MetadataUpdateHandlerAttribute"/> contract), and every host
/// that listens sends the <c>backend-updated</c> event to its open pages, with the update's revision, its time and the
/// names of the updated types. The SDK's <c>onBackendUpdate</c> and the development overlay's toast receive it.
/// <para>
/// Development builds only: <see cref="IsSupported"/> needs Spar's development switch, off and trimmed in Production,
/// and a runtime that applies updates (<see cref="MetadataUpdater.IsSupported"/>, true when <c>dotnet watch</c> or an IDE
/// starts the app with <c>DOTNET_MODIFIABLE_ASSEMBLIES=debug</c>). Hot Reload patches the whole process, so the revision
/// and the listeners are process-wide: several apps in one test process each receive every update.
/// </para>
/// <para>
/// The event can arrive after the patched code already ran once (a call that started meanwhile). Edits Hot Reload
/// rejects never reach the app: <c>dotnet watch</c> reports them in its own terminal, and restarts for rude edits.
/// </para>
/// </summary>
internal static class BackendUpdates
{
    /// <summary>The event's name on the bridge.</summary>
    public const string EventName = "backend-updated";

    private static readonly Lock Gate = new();
    private static Action<BackendUpdate>[] _listeners = [];
    private static long _revision;

    /// <summary>Whether this process can receive Hot Reload updates and is a Development build.</summary>
    public static bool IsSupported => SparDevelopment.IsSupported && MetadataUpdater.IsSupported;

    /// <summary>
    /// Calls <paramref name="listener"/> after each applied update, on the runtime's Hot Reload thread: it must only
    /// hand the update on (post it to its UI thread, queue it on a channel). Disposing the result stops the calls.
    /// </summary>
    public static IDisposable Listen(Action<BackendUpdate> listener)
    {
        lock (Gate)
        {
            _listeners = [.. _listeners, listener];
        }

        return new Listening(listener);
    }

    /// <summary>The runtime's call after an update is applied (<see cref="MetadataUpdateHandlerAttribute"/>).</summary>
    internal static void UpdateApplication(Type[]? updatedTypes)
    {
        if (IsSupported)
        {
            Publish(updatedTypes, DateTimeOffset.UtcNow);
        }
    }

    /// <summary>One applied update to every listener: the next revision, at <paramref name="time"/>.</summary>
    internal static BackendUpdate Publish(Type[]? updatedTypes, DateTimeOffset time)
    {
        Action<BackendUpdate>[] listeners;
        long revision;
        lock (Gate)
        {
            listeners = _listeners;
            revision = ++_revision;
        }

        var update = Describe(revision, time, updatedTypes);
        foreach (var listener in listeners)
        {
            listener(update);
        }

        return update;
    }

    /// <summary>
    /// The update as the page receives it: the revision, the time, and the names of the types the edit changed. A
    /// compiler-generated type (an async method's state machine, a lambda's closure) is named by the type it belongs
    /// to, so a method edit in <c>PagesService</c> reads "PagesService", once.
    /// </summary>
    internal static BackendUpdate Describe(long revision, DateTimeOffset time, Type[]? updatedTypes)
    {
        var names = new List<string>();
        foreach (var type in updatedTypes ?? [])
        {
            if (Named(type) is not { } named)
            {
                continue;
            }

            var name = named.FullName ?? named.Name;
            if (!names.Contains(name))
            {
                names.Add(name);
            }
        }

        return new BackendUpdate(revision, time, names, Payload(revision, time, names));
    }

    /// <summary><c>{"revision":3,"time":"2026-11-22T09:30:00.123+00:00","types":["Notebook.Pages.PagesService"]}</c>.</summary>
    internal static byte[] Payload(long revision, DateTimeOffset time, IReadOnlyList<string> types)
    {
        var buffer = new ArrayBufferWriter<byte>(128);
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteNumber("revision", revision);
            writer.WriteString("time", time);
            writer.WriteStartArray("types");
            foreach (var type in types)
            {
                writer.WriteStringValue(type);
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return buffer.WrittenSpan.ToArray();
    }

    /// <summary>
    /// The type a person wrote that <paramref name="type"/> belongs to: itself, or, when it or a type around it is one
    /// the compiler made, the type around the outermost such one; null when that is a top-level compiler type (the
    /// compiler's <c>&lt;PrivateImplementationDetails&gt;</c> and what it nests).
    /// </summary>
    private static Type? Named(Type type)
    {
        Type? named = type;
        for (var current = type; current is not null; current = current.DeclaringType)
        {
            if (IsCompilerGenerated(current))
            {
                named = current.DeclaringType;
            }
        }

        return named;
    }

    /// <summary>A name the compiler made up: <c>&lt;CreateAsync&gt;d__2</c>, <c>&lt;&gt;c</c>, <c>&lt;&gt;c__DisplayClass3_0</c>.</summary>
    private static bool IsCompilerGenerated(Type type) => type.Name.Contains('<', StringComparison.Ordinal);

    private static void Stop(Action<BackendUpdate> listener)
    {
        lock (Gate)
        {
            _listeners = [.. _listeners.Where(l => !ReferenceEquals(l, listener))];
        }
    }

    private sealed class Listening(Action<BackendUpdate> listener) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                Stop(listener);
            }
        }
    }
}

/// <summary>One applied Hot Reload update: its revision in this process, its time, the updated types and the event's payload.</summary>
internal sealed record BackendUpdate(long Revision, DateTimeOffset Time, IReadOnlyList<string> Types, byte[] Payload)
{
    /// <summary>The types for a log line: "Notebook.Pages.PagesService, Notebook.Pages.MarkdownPageStore", or "no named type".</summary>
    public string TypeList => Types.Count == 0 ? "no named type" : string.Join(", ", Types);
}
