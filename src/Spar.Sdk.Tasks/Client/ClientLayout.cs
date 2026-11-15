using Spar.Generator;

namespace Spar.Sdk.Tasks.Client;

/// <summary>A record or an error type as the page sees it: its TypeScript name and the module that declares it.</summary>
internal sealed record ClientType(string Key, string CSharp, string Name, string Module, RecordModel? Record, ErrorModel? Error);

/// <summary>A problem that stops the client step, reported at the C# declaration it is about.</summary>
internal sealed record ClientProblem(string Code, string Message, string SourceFile);

/// <summary>
/// Where each type goes (plan section 2, "Generated TypeScript"): a type used by one contract lives in that contract's
/// module, and one used by several lives once in <c>shared</c> and is re-exported. Within a module every name is used
/// once (SPAR040), and contract names are neither reserved subpaths nor JavaScript reserved words (SPAR003).
/// </summary>
internal sealed class ClientLayout
{
    public const string SharedModule = "shared";

    /// <summary>Subpaths the package uses itself (plan section 2, "Names").</summary>
    private static readonly HashSet<string> ReservedSubpaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "sdk", "vite", "windows", "telemetry", "shared", "spar", "testing",
    };

    private readonly Dictionary<string, ClientType> _types = new(StringComparer.Ordinal);

    private ClientLayout(IReadOnlyList<ClientContract> contracts)
    {
        Contracts = contracts;
    }

    public IReadOnlyList<ClientContract> Contracts { get; }

    public List<ClientProblem> Problems { get; } = [];

    /// <summary>The types the shared module declares.</summary>
    public IEnumerable<ClientType> Shared => _types.Values.Where(t => t.Module == SharedModule).OrderBy(t => t.Name, StringComparer.Ordinal);

    public static ClientLayout Plan(IReadOnlyList<ClientContract> contracts)
    {
        var layout = new ClientLayout(contracts);
        layout.PlaceTypes();
        layout.CheckNames();
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
                _types.TryAdd(record.Key, new ClientType(record.Key, record.CSharp, SimpleName(record.CSharp), ModuleOf(record.Key), record, null));
            }

            foreach (var error in contract.Model.Errors)
            {
                _types.TryAdd(error.Key, new ClientType(error.Key, error.CSharp, SimpleName(error.CSharp), ModuleOf(error.Key), null, error));
            }
        }

        string ModuleOf(string key) => users[key].Count > 1 ? SharedModule : users[key].Single();
    }

    private void CheckNames()
    {
        foreach (var contract in Contracts)
        {
            var name = contract.Model.WireName;
            if (ReservedSubpaths.Contains(name) || TypeScriptNames.IsReserved(name))
            {
                Problems.Add(new ClientProblem("SPAR003", $"Contract name '{name}' is reserved; give the contract another name with [SparContract(\"…\")]", contract.SourceFile));
            }

            var visible = UsedBy(contract).Concat(Shared).DistinctBy(t => t.Key).ToList();
            foreach (var clash in visible.GroupBy(t => t.Name, StringComparer.Ordinal).Where(g => g.Count() > 1))
            {
                var spellings = string.Join(" and ", clash.Select(t => t.CSharp.Replace("global::", "", StringComparison.Ordinal)));
                Problems.Add(new ClientProblem("SPAR040", $"Contract '{name}' uses two types named '{clash.Key}' in TypeScript: {spellings}; rename one", contract.SourceFile));
            }

            foreach (var type in visible.Where(t => TypeScriptNames.SdkTypes.Contains(t.Name)))
            {
                Problems.Add(new ClientProblem("SPAR040", $"Type '{type.Name}' of contract '{name}' has the name of a Spar SDK type; rename it", contract.SourceFile));
            }
        }
    }

    /// <summary>
    /// <c>global::App.Orders.Order</c>, <c>global::App.PlanError.TableMissing</c> and a type in the global namespace,
    /// <c>global::Order</c>, become their last names.
    /// </summary>
    private static string SimpleName(string csharp)
    {
        const string Global = "global::";
        var name = csharp.StartsWith(Global, StringComparison.Ordinal) ? csharp[Global.Length..] : csharp;
        return name[(name.LastIndexOf('.') + 1)..];
    }
}
