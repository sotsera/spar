using System.Collections.Generic;
using System.Linq;

namespace Spar.Generator;

/// <summary>The contracts an application exports, after the checks that need all of them at once.</summary>
public sealed record ContractSet(EquatableArray<ContractModel> Contracts, EquatableArray<DiagnosticModel> Diagnostics)
{
    /// <summary>
    /// Checks wire-name uniqueness across contracts (local and referenced) and TypeScript name uniqueness inside each
    /// contract. Returns the contracts that can be exported, ordered by wire name, and every diagnostic.
    /// </summary>
    public static ContractSet Analyze(IEnumerable<ContractModel> all)
    {
        var contracts = all.OrderBy(c => c.WireName, System.StringComparer.Ordinal).ThenBy(c => c.CSharp, System.StringComparer.Ordinal).ToList();
        var diagnostics = contracts.SelectMany(c => c.Diagnostics).ToList();
        var excluded = new HashSet<ContractModel>();

        // Contract names become package folders, so compare them ignoring case.
        foreach (var group in contracts.Where(c => c.IsValid).GroupBy(c => c.WireName, System.StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
        {
            var names = string.Join(" and ", group.Select(Describe));
            var local = group.Where(c => c.Assembly is null).ToList();
            var advice = local.Count > 0
                ? "Give one of them an explicit name with [SparContract(\"…\")]."
                : "Give one of them an explicit name with [SparContract(\"…\")] in its assembly.";
            foreach (var contract in local)
            {
                diagnostics.Add(new DiagnosticModel(SparDiagnostics.DuplicateContract,
                    $"Contracts {names} {(group.Count() == 2 ? "both" : "all")} resolve to the wire name '{contract.WireName}'. {advice}", contract.Location));
            }

            if (local.Count == 0)
            {
                diagnostics.Add(new DiagnosticModel(SparDiagnostics.DuplicateContract,
                    $"Contracts {names} {(group.Count() == 2 ? "both" : "all")} resolve to the wire name '{group.Key}'. {advice}", null));
            }

            excluded.UnionWith(group);
        }

        // One TypeScript module per contract: its types need distinct names there.
        foreach (var contract in contracts.Where(c => c.IsValid && !excluded.Contains(c)))
        {
            var types = contract.Dtos.Select(d => (d.Key, d.TsName, d.Location))
                .Concat(contract.Errors.Select(e => (e.Key, e.TsName, e.Location)));
            foreach (var group in types.GroupBy(t => t.TsName, System.StringComparer.Ordinal).Where(g => g.Count() > 1))
            {
                var keys = string.Join(" and ", group.Select(t => "'" + t.Key.Replace("global::", string.Empty) + "'"));
                var message = $"Contract '{contract.WireName}' uses two different types named '{group.Key}': {keys}. "
                    + "Give one of them a TypeScript name with [SparName(\"…\")].";
                if (contract.Assembly is not null)
                {
                    message = $"In referenced assembly '{contract.Assembly}': {message}";
                }

                // On each clashing type declared in this compilation; otherwise on the contract.
                var locations = group.Select(t => t.Location).Where(l => l is not null).ToList();
                if (locations.Count == 0)
                {
                    locations.Add(contract.Location);
                }

                foreach (var location in locations)
                {
                    var diagnostic = new DiagnosticModel(SparDiagnostics.TypeScriptNameClash, message, location);
                    if (!diagnostics.Contains(diagnostic))
                    {
                        diagnostics.Add(diagnostic);
                    }
                }

                excluded.Add(contract);
            }
        }

        var exported = contracts.Where(c => c.IsValid && !excluded.Contains(c)).ToEquatableArray();
        return new ContractSet(exported, diagnostics.ToEquatableArray());
    }

    private static string Describe(ContractModel contract) =>
        "'" + contract.CSharp.Replace("global::", string.Empty) + "'" + (contract.Assembly is null ? string.Empty : $" (in '{contract.Assembly}')");
}
