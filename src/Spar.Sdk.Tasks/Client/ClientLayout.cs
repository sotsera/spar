using Spar.Generator;

namespace Spar.Sdk.Tasks.Client;

/// <summary>A record or an error type as the page sees it: its TypeScript name and the module that declares it.</summary>
internal sealed record ClientType(string Key, string CSharp, string Name, string Module, RecordModel? Record, ErrorModel? Error);

/// <summary>A problem that stops the client step, reported at the C# declaration it is about (line 0: the file only).</summary>
internal sealed record ClientProblem(string Code, string Message, string SourceFile, int Line = 0);

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
        var docs = Contracts.Count > 0 ? Contracts[0].Docs : null;

        // Each type once, reported at its own declaration.
        foreach (var type in _types.Values.OrderBy(t => t.Name, StringComparer.Ordinal))
        {
            var (file, line) = docs?.Location(type.CSharp) ?? ("", 0);
            if (TypeScriptNames.IsReserved(type.Name))
            {
                Problems.Add(new ClientProblem("SPAR003", $"Type '{CSharpName(type)}' is named '{type.Name}', a reserved word in TypeScript; rename it", file, line));
            }

            if (TypeScriptNames.SdkImports.Contains(type.Name))
            {
                Problems.Add(new ClientProblem("SPAR040", $"Type '{CSharpName(type)}' has the name of the Spar SDK type '{type.Name}', which the generated modules import; rename it", file, line));
            }
        }

        foreach (var contract in Contracts)
        {
            var name = contract.Model.WireName;
            if (ReservedSubpaths.Contains(name) || TypeScriptNames.IsReserved(name))
            {
                Problems.Add(new ClientProblem("SPAR003", $"Contract name '{name}' is reserved; give the contract another name with [SparContract(\"…\")]", contract.SourceFile));
            }
            else if (ModuleEmitter.TopLevelNames(this, contract).Contains(name))
            {
                Problems.Add(new ClientProblem("SPAR003", $"Contract name '{name}' is a name its generated module uses itself; give the contract another name with [SparContract(\"…\")]", contract.SourceFile));
            }

            ReportClashes($"Contract '{name}' uses", UsedBy(contract), contract.SourceFile);
        }

        ReportClashes("The contracts share", Shared, "");
    }

    private void ReportClashes(string subject, IEnumerable<ClientType> types, string file)
    {
        foreach (var clash in types.GroupBy(t => t.Name, StringComparer.Ordinal).Where(g => g.Count() > 1))
        {
            var spellings = string.Join(" and ", clash.Select(CSharpName));
            Problems.Add(new ClientProblem("SPAR040", $"{subject} two types named '{clash.Key}' in TypeScript: {spellings}; rename one", file));
        }
    }

    private static string CSharpName(ClientType type) => type.CSharp.Replace("global::", "", StringComparison.Ordinal);

    /// <summary>
    /// <c>global::App.Orders.Order</c>, <c>global::App.PlanError.TableMissing</c> and a type in the global namespace,
    /// <c>global::Order</c>, become their last names. Roslyn writes a keyword used as a name with <c>@</c>
    /// (<c>global::App.@string</c>); the name is the word itself.
    /// </summary>
    private static string SimpleName(string csharp)
    {
        const string Global = "global::";
        var name = csharp.StartsWith(Global, StringComparison.Ordinal) ? csharp[Global.Length..] : csharp;
        return name[(name.LastIndexOf('.') + 1)..].TrimStart('@');
    }
}
