using System.Collections.Frozen;
using System.ComponentModel;
using Microsoft.Extensions.DependencyInjection;

namespace Spar.Runtime;

/// <summary>One generated command: what dispatch runs for <c>contract.command</c>.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public delegate ValueTask<SparOutcome> SparCommandHandler(SparCallContext call);

/// <summary>A command as the generator registers it.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed record SparCommandEntry(string Contract, string Command, int TimeoutSeconds, SparCommandHandler Handler);

/// <summary>
/// The app's contracts, built by generated code and passed to <see cref="SparApplication"/> by the generated
/// interceptor of <c>SparApplication.Create</c>. Immutable once built.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class SparCatalog
{
    private readonly FrozenDictionary<string, SparCommandEntry> _commands;
    private readonly Action<IServiceCollection> _addServices;

    public SparCatalog(string fingerprint, IEnumerable<SparCommandEntry> commands, Action<IServiceCollection> addServices)
    {
        Fingerprint = fingerprint;
        _commands = commands.ToFrozenDictionary(entry => Key(entry.Contract, entry.Command), StringComparer.Ordinal);
        _addServices = addServices;
    }

    /// <summary>16 hex characters of the SHA-256 of the contracts' wire shape (plan section 2c, "The messages").</summary>
    public string Fingerprint { get; }

    public IEnumerable<SparCommandEntry> Commands => _commands.Values;

    public bool TryGet(string contract, string command, out SparCommandEntry entry) =>
        _commands.TryGetValue(Key(contract, command), out entry!);

    /// <summary>Registers the class contracts (one instance per call scope).</summary>
    public void AddServices(IServiceCollection services) => _addServices(services);

    private static string Key(string contract, string command) => $"{contract}.{command}";
}
