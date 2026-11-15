using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace Spar.Generator;

/// <summary>
/// Every contract of one compilation, read once: the valid ones, and every problem, once per location and message,
/// filed under the type that contains it. The analyzer reports them as you type; the client step reports the same
/// ones in the build and stops before anything uses the contracts.
/// </summary>
internal sealed class ContractProblems
{
    private readonly Dictionary<INamedTypeSymbol, List<Diagnostic>> _byType = new(SymbolEqualityComparer.Default);
    private readonly HashSet<(string Id, string Path, int Start, string Message)> _seen = [];
    private readonly List<Diagnostic> _all = [];

    private ContractProblems()
    {
    }

    /// <summary>Every problem, in the order found.</summary>
    public IReadOnlyList<Diagnostic> All => _all;

    /// <summary>The contracts with no problem of their own and none with another contract, with their declarations.</summary>
    public IReadOnlyList<(ContractModel Model, INamedTypeSymbol Type)> Valid { get; private set; } = [];

    public static ContractProblems Find(Compilation compilation)
    {
        var problems = new ContractProblems();
        var symbols = new Dictionary<string, INamedTypeSymbol>(StringComparer.Ordinal);
        var models = new List<ContractModel>();
        foreach (var type in Contracts(compilation.Assembly.GlobalNamespace))
        {
            var model = ContractAnalysis.Analyze(type, problems.Add, symbols);
            if (model is not null)
            {
                models.Add(model);
            }
        }

        var (valid, setProblems) = ContractSet.Check(models);
        foreach (var problem in setProblems)
        {
            if (symbols.TryGetValue(problem.Subject, out var subject))
            {
                var location = subject.Locations.FirstOrDefault(l => l.IsInSource) ?? Location.None;
                problems.Add(subject, Diagnostic.Create(problem.Descriptor, location, problem.Arguments));
            }
        }

        problems.Valid = [.. valid.Select(m => (m, symbols[m.CSharp]))];
        return problems;
    }

    /// <summary>The problems a type's own declaration contains.</summary>
    public IReadOnlyList<Diagnostic> In(INamedTypeSymbol type) =>
        _byType.TryGetValue(type, out var diagnostics) ? diagnostics : [];

    /// <summary>
    /// Files a diagnostic under the type its symbol belongs to (a parameter or method under its type), once per
    /// location and message. Problems with no source location (inside referenced assemblies) are the generator's.
    /// </summary>
    private void Add(ISymbol symbol, Diagnostic diagnostic)
    {
        var owner = symbol as INamedTypeSymbol ?? symbol.ContainingType;
        var location = diagnostic.Location;
        if (owner is null || !location.IsInSource)
        {
            return;
        }

        var key = (diagnostic.Id, location.SourceTree!.FilePath, location.SourceSpan.Start, diagnostic.GetMessage());
        if (!_seen.Add(key))
        {
            return;
        }

        if (!_byType.TryGetValue(owner, out var list))
        {
            _byType[owner] = list = [];
        }

        list.Add(diagnostic);
        _all.Add(diagnostic);
    }

    private static IEnumerable<INamedTypeSymbol> Contracts(INamespaceSymbol root)
    {
        var pending = new Stack<INamespaceOrTypeSymbol>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            foreach (var member in current is INamespaceSymbol ns ? ns.GetMembers() : ((INamedTypeSymbol)current).GetTypeMembers().Cast<INamespaceOrTypeSymbol>())
            {
                pending.Push(member);
            }

            if (current is INamedTypeSymbol type && IsContract(type))
            {
                yield return type;
            }
        }
    }

    private static bool IsContract(INamedTypeSymbol type) =>
        type.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == "Spar.SparContractAttribute");
}
