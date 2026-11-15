using Spar.Generator;

namespace Spar.Sdk.Tasks.Client;

/// <summary>A record or an error type as the page sees it: its TypeScript name and the module that declares it.</summary>
internal sealed record ClientType(string Key, string CSharp, string Name, string Module, RecordModel? Record, ErrorModel? Error);

/// <summary>
/// Where each type goes (plan section 2, "Generated TypeScript"): a type used by one contract lives in that contract's
/// module, and one used by several lives once in <c>shared</c> and is re-exported. The names were checked by the
/// contract analysis (SPAR003, SPAR004, SPAR040), whose analyzer reports any problem at its declaration.
/// </summary>
internal sealed class ClientLayout
{
    public const string SharedModule = "shared";

    private readonly Dictionary<string, ClientType> _types = new(StringComparer.Ordinal);

    private ClientLayout(IReadOnlyList<ClientContract> contracts)
    {
        Contracts = contracts;
    }

    public IReadOnlyList<ClientContract> Contracts { get; }

    /// <summary>The types the shared module declares.</summary>
    public IEnumerable<ClientType> Shared => _types.Values.Where(t => t.Module == SharedModule).OrderBy(t => t.Name, StringComparer.Ordinal);

    public static ClientLayout Plan(IReadOnlyList<ClientContract> contracts)
    {
        var layout = new ClientLayout(contracts);
        layout.PlaceTypes();
        return layout;
    }

    public ClientType Type(string key) => _types[key];

    /// <summary>The types a contract's module declares itself.</summary>
    public IEnumerable<ClientType> Own(ClientContract contract) =>
        UsedBy(contract).Where(t => t.Module == contract.Model.WireName).OrderBy(t => t.Name, StringComparer.Ordinal);

    /// <summary>The shared types a contract's module imports and re-exports.</summary>
    public IEnumerable<ClientType> Imported(ClientContract contract) =>
        UsedBy(contract).Where(t => t.Module == SharedModule).OrderBy(t => t.Name, StringComparer.Ordinal);

    private IEnumerable<ClientType> UsedBy(ClientContract contract) =>
        contract.Model.Records.Select(r => _types[r.Key]).Concat(contract.Model.Errors.Select(e => _types[e.Key]));

    private void PlaceTypes()
    {
        var users = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var contract in Contracts)
        {
            var keys = contract.Model.Records.Select(r => r.Key).Concat(contract.Model.Errors.Select(e => e.Key));
            foreach (var key in keys)
            {
                if (!users.TryGetValue(key, out var set))
                {
                    users[key] = set = new HashSet<string>(StringComparer.Ordinal);
                }

                set.Add(contract.Model.WireName);
            }
        }

        foreach (var contract in Contracts)
        {
            foreach (var record in contract.Model.Records)
            {
                _types.TryAdd(record.Key, new ClientType(record.Key, record.CSharp, record.Name, ModuleOf(record.Key), record, null));
            }

            foreach (var error in contract.Model.Errors)
            {
                _types.TryAdd(error.Key, new ClientType(error.Key, error.CSharp, error.Name, ModuleOf(error.Key), null, error));
            }
        }

        string ModuleOf(string key) => users[key].Count > 1 ? SharedModule : users[key].Single();
    }
}
