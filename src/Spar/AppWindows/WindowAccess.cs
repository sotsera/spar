using System.Collections.Frozen;
using Spar.Runtime;

namespace Spar.AppWindows;

/// <summary>
/// A window's allow-list resolved against the app's contracts (decision "Per-window command allow-lists"): the
/// contracts it may call whole, and single commands of others. Spar's own page commands, under the host contract
/// <c>spar</c>, are never limited.
/// </summary>
internal sealed class WindowAccess
{
    private readonly FrozenSet<string> _contracts;
    private readonly FrozenSet<string> _commands;

    private WindowAccess(FrozenSet<string> contracts, FrozenSet<string> commands)
    {
        _contracts = contracts;
        _commands = commands;
    }

    /// <summary>The window's name for messages: <c>main</c> or the kind.</summary>
    public required string Window { get; init; }

    public bool Allows(string contract, string command) =>
        _contracts.Contains(contract) || _commands.Contains(Key(contract, command));

    /// <summary>
    /// The list's contract types and commands as the catalog names them; a type that is not one of the app's contracts,
    /// or a command it does not have, fails the start with the window's name.
    /// </summary>
    public static WindowAccess Resolve(string window, CommandAllowList list, SparCatalog catalog)
    {
        var contracts = new HashSet<string>(StringComparer.Ordinal);
        foreach (var type in list.Contracts)
        {
            contracts.Add(NameOf(window, type, catalog));
        }

        var commands = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (type, command) in list.SingleCommands)
        {
            var contract = NameOf(window, type, catalog);
            if (catalog.Find(contract, command) is null)
            {
                throw new InvalidOperationException($"Window '{window}': its allow-list names {contract}.{command}, which the contract {type.Name} does not have; name a command as the page calls it.");
            }

            commands.Add(Key(contract, command));
        }

        return new WindowAccess(contracts.ToFrozenSet(StringComparer.Ordinal), commands.ToFrozenSet(StringComparer.Ordinal)) { Window = window };
    }

    private static string NameOf(string window, Type type, SparCatalog catalog) =>
        catalog.ContractNameOf(type)
        ?? throw new InvalidOperationException($"Window '{window}': its allow-list names {type.Name}, which is not one of the app's contracts (a [SparContract] class or interface).");

    private static string Key(string contract, string command) => $"{contract}.{command}";
}
