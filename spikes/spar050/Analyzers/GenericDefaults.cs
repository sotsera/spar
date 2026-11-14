using System.Collections.Concurrent;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Spar.Analyzers;

/// <summary>
/// SPAR054: a generic method in source whose body creates default(T) (directly, through Activator.CreateInstance&lt;T&gt;(),
/// or by passing T on to another such method), called with T bound to a Result. The method body and the call are in
/// different operation blocks, so facts are collected during the compilation and joined at its end.
/// Calls already reported by SPAR051 (a T? return) or SPAR050 (a new() constraint) are skipped.
/// </summary>
internal sealed class GenericDefaults
{
    private readonly ResultTypes _types;
    private readonly MaybeDefaultCalls _calls;
    private readonly ConcurrentDictionary<Key, bool> _producers = new();
    private readonly ConcurrentBag<(Key Caller, Key Callee)> _edges = new();
    private readonly ConcurrentBag<Site> _sites = new();

    public GenericDefaults(ResultTypes types, MaybeDefaultCalls calls)
    {
        _types = types;
        _calls = calls;
    }

    public void Register(CompilationStartAnalysisContext context)
    {
        context.RegisterOperationAction(OnDefault, OperationKind.DefaultValue);
        context.RegisterOperationAction(OnInvocation, OperationKind.Invocation);
        context.RegisterCompilationEndAction(OnEnd);
    }

    private void OnDefault(OperationAnalysisContext context)
    {
        var operation = context.Operation;
        if (operation.IsImplicit
            || operation.Type is not ITypeParameterSymbol parameter
            || context.ContainingSymbol is not IMethodSymbol method
            || DefaultUse.IsDeclaredOrCompared(operation, method))
        {
            return;
        }

        _producers.TryAdd(new Key(method.OriginalDefinition, parameter.OriginalDefinition), true);
    }

    private void OnInvocation(OperationAnalysisContext context)
    {
        var invocation = (IInvocationOperation)context.Operation;
        var target = invocation.TargetMethod;
        if (!target.IsGenericMethod && !target.ContainingType.IsGenericType)
        {
            return;
        }

        var caller = context.ContainingSymbol as IMethodSymbol;
        if (IsActivatorCreateInstance(target) && target.TypeArguments[0] is ITypeParameterSymbol created && caller is not null)
        {
            _producers.TryAdd(new Key(caller.OriginalDefinition, created.OriginalDefinition), true);
            return;
        }

        var original = target.OriginalDefinition;
        if (!DefaultResultAnalyzer.IsInSource(original))
        {
            return;
        }

        foreach (var (parameter, argument) in Bindings(target))
        {
            var callee = new Key(original, parameter);
            if (argument is ITypeParameterSymbol forwarded)
            {
                if (caller is not null)
                {
                    _edges.Add((new Key(caller.OriginalDefinition, forwarded.OriginalDefinition), callee));
                }

                continue;
            }

            if (!parameter.HasConstructorConstraint && _types.HoldsResult(argument) && !_calls.ReturnsMaybeDefault(target))
            {
                _sites.Add(new Site(invocation.Syntax.GetLocation(), original, parameter, argument));
            }
        }
    }

    private void OnEnd(CompilationAnalysisContext context)
    {
        if (_sites.IsEmpty)
        {
            return;
        }

        // Close the producer set over "M<T> passes T to N<U>, and N produces default(U)".
        var produced = new HashSet<Key>(_producers.Keys);
        var edges = _edges.ToArray();
        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var (caller, callee) in edges)
            {
                if (produced.Contains(callee) && produced.Add(caller))
                {
                    changed = true;
                }
            }
        }

        var reported = new HashSet<Location>();
        foreach (var site in _sites)
        {
            if (!produced.Contains(new Key(site.Method, site.Parameter)) || !reported.Add(site.Location))
            {
                continue;
            }

            var diagnostic = Diagnostic.Create(
                Descriptors.GenericDefault,
                site.Location,
                site.Method.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat),
                site.Parameter.Name,
                ResultTypes.Display(site.Argument));
            context.ReportDiagnostic(diagnostic);
        }
    }

    private static bool IsActivatorCreateInstance(IMethodSymbol method) =>
        method is { Name: "CreateInstance", IsGenericMethod: true, TypeArguments.Length: 1 }
        && ResultTypes.FullMetadataName(method.ContainingType) == "System.Activator";

    /// <summary>Each type parameter of the method and of its containing types, with the argument bound at this call.</summary>
    private static IEnumerable<(ITypeParameterSymbol Parameter, ITypeSymbol Argument)> Bindings(IMethodSymbol target)
    {
        var original = target.OriginalDefinition;
        for (var i = 0; i < original.TypeParameters.Length; i++)
        {
            yield return (original.TypeParameters[i], target.TypeArguments[i]);
        }

        var constructed = target.ContainingType;
        var definition = original.ContainingType;
        while (constructed is not null && definition is not null)
        {
            for (var i = 0; i < definition.TypeParameters.Length; i++)
            {
                yield return (definition.TypeParameters[i], constructed.TypeArguments[i]);
            }

            constructed = constructed.ContainingType;
            definition = definition.ContainingType;
        }
    }

    private readonly struct Key : System.IEquatable<Key>
    {
        public Key(IMethodSymbol method, ITypeParameterSymbol parameter)
        {
            Method = method;
            Parameter = parameter;
        }

        public IMethodSymbol Method { get; }

        public ITypeParameterSymbol Parameter { get; }

        public bool Equals(Key other) =>
            SymbolEqualityComparer.Default.Equals(Method, other.Method)
            && SymbolEqualityComparer.Default.Equals(Parameter, other.Parameter);

        public override bool Equals(object? obj) => obj is Key other && Equals(other);

        public override int GetHashCode() =>
            (SymbolEqualityComparer.Default.GetHashCode(Method) * 31) ^ SymbolEqualityComparer.Default.GetHashCode(Parameter);
    }

    private sealed class Site
    {
        public Site(Location location, IMethodSymbol method, ITypeParameterSymbol parameter, ITypeSymbol argument)
        {
            Location = location;
            Method = method;
            Parameter = parameter;
            Argument = argument;
        }

        public Location Location { get; }

        public IMethodSymbol Method { get; }

        public ITypeParameterSymbol Parameter { get; }

        public ITypeSymbol Argument { get; }
    }
}
