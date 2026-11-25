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
/// An interface contract: the app registers its implementation itself, so the start checks that it did.
/// <paramref name="Name"/> is the interface's C# name, for the message.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed record SparInterfaceContract(string Contract, string Name, Type Service);

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

    /// <summary>The app's assembly, which carries its packed page and notices; set by the generated catalog.</summary>
    public System.Reflection.Assembly? AppAssembly { get; init; }

    /// <summary>
    /// The app id the build checked (<c>&lt;SparAppId&gt;</c>, plan section 3c), which names the app's folders and its
    /// identity to the OS; set by the generated catalog, null when the build had none.
    /// </summary>
    public string? AppId { get; init; }

    /// <summary>The interface contracts, whose implementations come from the app's own registrations.</summary>
    public IReadOnlyList<SparInterfaceContract> Interfaces { get; init; } = [];

    /// <summary>
    /// The page security the build read from <c>Program.cs</c> (<c>spar.ContentSecurity</c>, <c>spar.Permissions</c>);
    /// null when no generator set it, and the run's own declaration is used.
    /// </summary>
    public SparPageSecurity? PageSecurity { get; init; }

    /// <summary>
    /// The build's identity block (the variant, the git revision, whether the tree had uncommitted changes, the
    /// contracts' fingerprint), which the host writes in its first log line; set by the generated catalog, empty
    /// without one.
    /// </summary>
    public ReadOnlyMemory<byte> BuildIdentity { get; init; }

    /// <summary>16 hex characters of the SHA-256 of the contracts' wire shape (plan section 2c, "The messages").</summary>
    public string Fingerprint { get; }

    public IEnumerable<SparCommandEntry> Commands => _commands.Values;

    public bool TryGet(string contract, string command, out SparCommandEntry entry) =>
        _commands.TryGetValue(Key(contract, command), out entry!);

    /// <summary>Registers the class contracts (one instance per call scope).</summary>
    public void AddServices(IServiceCollection services) => _addServices(services);

    private static string Key(string contract, string command) => $"{contract}.{command}";
}
