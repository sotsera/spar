using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;

namespace Spar;

public delegate ValueTask<SparReply> SparCommand(IServiceProvider services, string payload, CancellationToken cancellationToken);

public enum SparReplyStatus
{
    Ok,
    Error,
    Failure,
}

/// <summary>A command's answer: a value, a declared error, or a failure (kind and code), each as JSON.</summary>
public readonly record struct SparReply(SparReplyStatus Status, string Payload)
{
    public static SparReply Ok(string json) => new(SparReplyStatus.Ok, json);

    public static SparReply Error(string json) => new(SparReplyStatus.Error, json);

    public static SparReply Failure(string kind, string code) =>
        new(SparReplyStatus.Failure, $$"""{"kind":"{{kind}}","code":"{{code}}"}""");
}

/// <summary>Stand-in for the generated dispatch: the app's commands by contract and name, and its packaged page.</summary>
public sealed class SparContractCatalog(Assembly appAssembly)
{
    private readonly Dictionary<string, SparCommand> _commands = new(StringComparer.Ordinal);

    public Assembly AppAssembly { get; } = appAssembly;

    public SparContractCatalog Add(string contract, string command, SparCommand handler)
    {
        _commands.Add($"{contract}.{command}", handler);
        return this;
    }

    internal bool TryGet(string contract, string command, out SparCommand handler) =>
        _commands.TryGetValue($"{contract}.{command}", out handler!);
}

/// <summary>
/// One message format for every transport: a JSON header, a TAB, then the payload's JSON.
/// Request header <c>{"id":1,"contract":"notes","command":"add"}</c>; reply header <c>{"id":1,"status":"ok"}</c>.
/// Each call runs in its own DI scope.
/// </summary>
public sealed class SparBridge(IServiceProvider services, SparContractCatalog catalog, ISparLog log)
{
    public async Task<string> HandleAsync(string message, CancellationToken cancellationToken)
    {
        var tab = message.IndexOf('\t', StringComparison.Ordinal);
        if (tab < 0)
        {
            return Format(0, SparReply.Failure("protocol", "malformed"));
        }

        long id;
        string contract;
        string command;
        try
        {
            using var header = JsonDocument.Parse(message.AsMemory(0, tab));
            id = header.RootElement.GetProperty("id").GetInt64();
            contract = header.RootElement.GetProperty("contract").GetString() ?? "";
            command = header.RootElement.GetProperty("command").GetString() ?? "";
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return Format(0, SparReply.Failure("protocol", "malformed"));
        }

        if (!catalog.TryGet(contract, command, out var handler))
        {
            return Format(id, SparReply.Failure("protocol", "unknown-command"));
        }

        try
        {
            await using var scope = services.CreateAsyncScope();
            var reply = await handler(scope.ServiceProvider, message[(tab + 1)..], cancellationToken);
            return Format(id, reply);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Format(id, SparReply.Failure("cancelled", "cancelled"));
        }
        catch (Exception exception)
        {
            log.Write("bridge", $"{contract}.{command} threw", exception);
            return Format(id, SparReply.Failure("backend", exception is SparExternalDependencyException ? "external-dependency" : "unhandled"));
        }
    }

    private static string Format(long id, SparReply reply)
    {
        var status = reply.Status switch
        {
            SparReplyStatus.Ok => "ok",
            SparReplyStatus.Error => "error",
            _ => "failure",
        };
        return new StringBuilder().Append($$"""{"id":{{id}},"status":"{{status}}"}""").Append('\t').Append(reply.Payload).ToString();
    }
}

/// <summary>The packaged page: embedded resources named <c>spar/app/&lt;path&gt;</c>, read once.</summary>
public sealed class SparAssets
{
    private const string Prefix = "spar/app/";
    private readonly Dictionary<string, byte[]> _files = new(StringComparer.Ordinal);

    public SparAssets(Assembly appAssembly)
    {
        foreach (var name in appAssembly.GetManifestResourceNames().Where(n => n.StartsWith(Prefix, StringComparison.Ordinal)))
        {
            using var stream = appAssembly.GetManifestResourceStream(name)!;
            using var copy = new MemoryStream();
            stream.CopyTo(copy);
            _files["/" + name[Prefix.Length..]] = copy.ToArray();
        }
    }

    public const string ContentSecurityPolicy =
        "default-src 'none'; script-src 'self'; style-src 'self'; img-src 'self'; connect-src 'self'; base-uri 'none'; form-action 'none'; frame-ancestors 'none'";

    public bool TryGet(string path, out byte[] content) => _files.TryGetValue(path == "/" ? "/index.html" : path, out content!);

    public static string ContentType(string path) => Path.GetExtension(path) switch
    {
        ".html" => "text/html; charset=utf-8",
        ".js" => "text/javascript; charset=utf-8",
        ".css" => "text/css; charset=utf-8",
        _ => "application/octet-stream",
    };
}

/// <summary>A running app: its provider, bridge, folders and log. Hosts and tests reach the backend through it.</summary>
public sealed class SparRuntime(ServiceProvider services, SparBridge bridge, SparFolders folders, ISparLog log, WindowOptions window, SparAssets assets)
    : IAsyncDisposable
{
    public IServiceProvider Services => services;

    public SparBridge Bridge { get; } = bridge;

    public SparFolders Folders { get; } = folders;

    public ISparLog Log { get; } = log;

    public WindowOptions Window { get; } = window;

    public SparAssets Assets { get; } = assets;

    public ValueTask DisposeAsync() => services.DisposeAsync();

    internal static SparRuntime Build(SparBuilder builder, IServiceCollection services, SparFolders folders, ISparLog log)
    {
        var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        var bridge = new SparBridge(provider, builder.Catalog!, log);
        return new SparRuntime(provider, bridge, folders, log, builder.WindowOptions, new SparAssets(builder.Catalog!.AppAssembly));
    }
}
