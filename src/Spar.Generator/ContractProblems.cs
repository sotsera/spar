using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace Spar.Generator;

/// <summary>
/// Every contract one app serves, read once per compilation: its own, and the public ones of the libraries it lists in
/// <c>SparContracts</c> (decision "Contracts from referenced libraries: the app opts in"). It holds the valid ones and
/// every problem, once per location and message. A problem in the project is filed under the type that contains it, for
/// the analyzer to report as you type; one inside a referenced library has no place and says which library. The
/// client step reports them all in the build and stops before anything uses the contracts.
/// </summary>
internal sealed class ContractProblems
{
    private readonly Dictionary<INamedTypeSymbol, List<Diagnostic>> _byType = new(SymbolEqualityComparer.Default);
    private readonly HashSet<(string Id, string Path, int Start, string Message)> _seen = [];
    private readonly List<Diagnostic> _all = [];
    private readonly List<Diagnostic> _unlocated = [];
    private readonly Compilation _compilation;

    private ContractProblems(Compilation compilation)
    {
        _compilation = compilation;
    }

    /// <summary>Every problem, in the order found.</summary>
    public IReadOnlyList<Diagnostic> All => _all;

    /// <summary>The problems with no place in the project: inside a listed library, or about the list itself.</summary>
    public IReadOnlyList<Diagnostic> Unlocated => _unlocated;

    /// <summary>The contracts with no problem of their own and none with another contract, with their declarations.</summary>
    public IReadOnlyList<(ContractModel Model, INamedTypeSymbol Type)> Valid { get; private set; } = [];

    /// <summary>The library names of <c>SparContracts</c>, as the build passes them: separated by semicolons.</summary>
    public static IReadOnlyList<string> ParseList(string? list) =>
        string.IsNullOrWhiteSpace(list)
            ? []
            : [.. list!.Split(';').Select(n => n.Trim()).Where(n => n.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase)];

    public static ContractProblems Find(Compilation compilation, IReadOnlyList<string> libraries)
    {
        var problems = new ContractProblems(compilation);
        var symbols = new Dictionary<string, INamedTypeSymbol>(StringComparer.Ordinal);
        var models = new List<ContractModel>();
        foreach (var type in Contracts(compilation, libraries, problems))
        {
            var model = ContractAnalysis.Analyze(type, problems.Add, symbols);
            if (model is not null)
            {
                models.Add(model);
            }
        }

        var (valid, setProblems) = ContractSet.Check(models);
        foreach (var group in setProblems.GroupBy(p => p.Group))
        {
            problems.AddClash(group.ToList(), symbols);
        }

        problems.Valid = [.. valid.Select(m => (m, symbols[m.CSharp]))];
        return problems;
    }

    /// <summary>
    /// The contracts an app serves, in the order it reads them: its own, then each listed library's public ones. A
    /// listed name the project does not reference, a library with no public contract, and a contract that is not
    /// public are SPAR006.
    /// </summary>
    public static IEnumerable<INamedTypeSymbol> Contracts(Compilation compilation, IReadOnlyList<string> libraries, ContractProblems? problems = null) =>
        AllTypes(compilation.Assembly.GlobalNamespace).Where(IsContract).Concat(Referenced(compilation, libraries, problems));

    /// <summary>The public contracts of the listed libraries, which the generator adds to the project's own.</summary>
    public static IEnumerable<INamedTypeSymbol> Referenced(Compilation compilation, IReadOnlyList<string> libraries, ContractProblems? problems = null)
    {
        var referenced = compilation.SourceModule.ReferencedAssemblySymbols
            .GroupBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        foreach (var name in libraries)
        {
            if (!referenced.TryGetValue(name, out var library))
            {
                problems?.AddUnlocated(Diagnostic.Create(Descriptors.ListedLibrary, Location.None,
                    $"SparContracts lists '{name}', which the project does not reference. Reference it, or remove it from SparContracts"));
                continue;
            }

            var contracts = AllTypes(library.GlobalNamespace).Where(IsContract).ToList();
            var served = contracts.Where(IsPublic).ToList();
            foreach (var hidden in contracts.Where(c => !IsPublic(c)))
            {
                problems?.AddUnlocated(Diagnostic.Create(Descriptors.ListedLibrary, Location.None,
                    $"In referenced assembly '{library.Name}': contract '{hidden.Name}' is not public, so the app cannot serve it. Make it public, or move it out of the library"));
            }

            if (contracts.Count == 0)
            {
                problems?.AddUnlocated(Diagnostic.Create(Descriptors.ListedLibrary, Location.None,
                    $"SparContracts lists '{library.Name}', which declares no contract. Remove it from SparContracts"));
            }

            foreach (var type in served)
            {
                yield return type;
            }
        }
    }

    /// <summary>The problems a type's own declaration contains.</summary>
    public IReadOnlyList<Diagnostic> In(INamedTypeSymbol type) =>
        _byType.TryGetValue(type, out var diagnostics) ? diagnostics : [];

    /// <summary>
    /// Files a diagnostic under the type its symbol belongs to (a parameter or method under its type), once per
    /// location and message. One about a symbol outside the project is restated for its library.
    /// </summary>
    private void Add(ISymbol symbol, Diagnostic diagnostic)
    {
        var location = diagnostic.Location;
        if (!InProject(location))
        {
            var library = symbol.ContainingAssembly?.Name ?? "";
            var restated = Descriptors.InReferencedAssembly(diagnostic.Descriptor);
            AddUnlocated(Diagnostic.Create(restated, Location.None, library, diagnostic.GetMessage()));
            return;
        }

        var owner = symbol as INamedTypeSymbol ?? symbol.ContainingType;
        if (owner is null || !Seen(diagnostic))
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

    /// <summary>
    /// One clash: reported at each of its subjects declared in the project; when none is, once, for the library of
    /// the first (plan section 2: "no location when all are referenced").
    /// </summary>
    private void AddClash(List<SetProblem> clash, Dictionary<string, INamedTypeSymbol> symbols)
    {
        var placed = false;
        foreach (var problem in clash)
        {
            if (symbols.TryGetValue(problem.Subject, out var subject) && subject.Locations.FirstOrDefault(InProject) is { } location)
            {
                Add(subject, Diagnostic.Create(problem.Descriptor, location, problem.Arguments));
                placed = true;
            }
        }

        if (!placed && symbols.TryGetValue(clash[0].Subject, out var first))
        {
            Add(first, Diagnostic.Create(clash[0].Descriptor, Location.None, clash[0].Arguments));
        }
    }

    /// <summary>
    /// A place in this project. A library referenced as a project has source locations too, in its own compilation,
    /// which the project cannot report at.
    /// </summary>
    private bool InProject(Location location) =>
        location.SourceTree is { } tree && _compilation.ContainsSyntaxTree(tree);

    private void AddUnlocated(Diagnostic diagnostic)
    {
        if (Seen(diagnostic))
        {
            _unlocated.Add(diagnostic);
            _all.Add(diagnostic);
        }
    }

    private bool Seen(Diagnostic diagnostic)
    {
        var location = diagnostic.Location;
        var path = location.SourceTree?.FilePath ?? "";
        return _seen.Add((diagnostic.Id, path, location.SourceSpan.Start, diagnostic.GetMessage()));
    }

    private static bool IsPublic(INamedTypeSymbol type)
    {
        for (var current = type; current is not null; current = current.ContainingType)
        {
            if (current.DeclaredAccessibility != Accessibility.Public)
            {
                return false;
            }
        }

        return true;
    }

    private static IEnumerable<INamedTypeSymbol> AllTypes(INamespaceSymbol root)
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

            if (current is INamedTypeSymbol type)
            {
                yield return type;
            }
        }
    }

    private static bool IsContract(INamedTypeSymbol type) =>
        type.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == "Spar.SparContractAttribute");
}
