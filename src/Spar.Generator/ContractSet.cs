using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace Spar.Generator;

/// <summary>
/// A problem between contracts, about one contract or one type, named by its C# name (<see cref="ContractModel.CSharp"/>,
/// <see cref="RecordModel.CSharp"/>, <see cref="ErrorModel.CSharp"/>), so the analyzer can place it at that declaration.
/// </summary>
internal sealed record SetProblem(DiagnosticDescriptor Descriptor, string Subject, object[] Arguments);

/// <summary>
/// The checks that need every contract at once (plan section 2, "Names"): two contracts with one name (SPAR005), and
/// two types with one TypeScript name in the shared module (SPAR040). A contract involved in either is left out of the
/// generated code and client; the analyzer reports why.
/// </summary>
internal static class ContractSet
{
    public static (List<ContractModel> Valid, List<SetProblem> Problems) Check(IEnumerable<ContractModel> models)
    {
        var contracts = models.OrderBy(c => c.WireName, StringComparer.Ordinal).ThenBy(c => c.CSharp, StringComparer.Ordinal).ToList();
        var problems = new List<SetProblem>();
        var excluded = new HashSet<string>(StringComparer.Ordinal);

        foreach (var group in contracts.GroupBy(c => c.WireName, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
        {
            var members = group.ToList();
            foreach (var contract in members)
            {
                var other = members.First(c => c != contract);
                problems.Add(new SetProblem(Descriptors.DuplicateContract, contract.CSharp, [Display(contract.CSharp), Display(other.CSharp), contract.WireName]));
                excluded.Add(contract.CSharp);
            }
        }

        foreach (var clash in SharedClashes(contracts.Where(c => !excluded.Contains(c.CSharp)).ToList()))
        {
            var spellings = string.Join(" and ", clash.Select(t => Display(t.CSharp)).OrderBy(s => s, StringComparer.Ordinal));
            foreach (var type in clash)
            {
                problems.Add(new SetProblem(Descriptors.DuplicateTypeName, type.CSharp, ["The shared module declares", type.Name, spellings]));
            }

            var keys = new HashSet<string>(clash.Select(t => t.Key), StringComparer.Ordinal);
            excluded.UnionWith(contracts.Where(c => Used(c).Any(t => keys.Contains(t.Key))).Select(c => c.CSharp));
        }

        return (contracts.Where(c => !excluded.Contains(c.CSharp)).ToList(), problems);
    }

    /// <summary>The valid contracts alone, for the generator, which reports nothing.</summary>
    public static List<ContractModel> Valid(IEnumerable<ContractModel> models) => Check(models).Valid;

    /// <summary>Types used by two or more contracts go to the shared module once; there each name is used once.</summary>
    private static IEnumerable<List<(string Key, string CSharp, string Name)>> SharedClashes(List<ContractModel> contracts)
    {
        var shared = contracts.SelectMany(c => Used(c).Select(t => (Contract: c.CSharp, Type: t)))
            .GroupBy(u => u.Type.Key, StringComparer.Ordinal)
            .Where(g => g.Select(u => u.Contract).Distinct().Count() > 1)
            .Select(g => g.First().Type);
        return shared.GroupBy(t => t.Name, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.ToList());
    }

    private static IEnumerable<(string Key, string CSharp, string Name)> Used(ContractModel contract) =>
        contract.Records.Select(r => (r.Key, r.CSharp, r.Name)).Concat(contract.Errors.Select(e => (e.Key, e.CSharp, e.Name)));

    private static string Display(string csharp) => csharp.Replace("global::", "");
}
