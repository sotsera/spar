using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;

namespace Spar.Generator;

/// <summary>Finds contract types in a compilation and in the assemblies it references.</summary>
public static class ContractDiscovery
{
    /// <summary>Contracts declared in the compilation itself (the analyzer and the client tool; the generator uses its syntax provider).</summary>
    public static List<ContractModel> ReadLocal(Compilation compilation, CancellationToken cancellationToken) =>
        Read(compilation.Assembly, compilation, referenced: false, cancellationToken);

    /// <summary>
    /// Contracts in referenced assemblies that themselves reference the Spar runtime. Only public contracts are read:
    /// the application's generated dispatch must be able to call them.
    /// </summary>
    public static List<ContractModel> ReadReferenced(Compilation compilation, CancellationToken cancellationToken)
    {
        var contracts = new List<ContractModel>();
        foreach (var assembly in compilation.SourceModule.ReferencedAssemblySymbols.OrderBy(a => a.Name, System.StringComparer.Ordinal))
        {
            if (assembly.Modules.Any(m => m.ReferencedAssemblies.Any(r => r.Name == ContractReader.RuntimeAssembly)))
            {
                contracts.AddRange(Read(assembly, compilation, referenced: true, cancellationToken));
            }
        }

        return contracts;
    }

    private static List<ContractModel> Read(IAssemblySymbol assembly, Compilation compilation, bool referenced, CancellationToken cancellationToken)
    {
        var contracts = new List<ContractModel>();
        var pending = new Stack<INamespaceOrTypeSymbol>();
        pending.Push(assembly.GlobalNamespace);
        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var member in pending.Pop().GetMembers())
            {
                if (member is INamespaceSymbol ns)
                {
                    pending.Push(ns);
                    continue;
                }

                if (member is not INamedTypeSymbol type)
                {
                    continue;
                }

                pending.Push(type);
                if (referenced && type.DeclaredAccessibility != Accessibility.Public)
                {
                    continue;
                }

                var attribute = type.GetAttributes().FirstOrDefault(ContractReader.IsContractAttribute);
                if (attribute is not null)
                {
                    contracts.Add(ContractReader.Read(type, attribute, compilation, referenced, cancellationToken));
                }
            }
        }

        return contracts;
    }
}
