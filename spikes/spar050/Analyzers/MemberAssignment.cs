using System.Collections.Concurrent;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Spar.Analyzers;

/// <summary>
/// SPAR053: a field or auto-property that holds a Result and can be read before anything assigns it. It mirrors what
/// nullable analysis (CS8618) does for a non-nullable reference: every constructor must assign the member, or it needs
/// an initializer, or it is required; otherwise the opt-out is Result&lt;…&gt;?. Fields that are never written at all
/// are left to the compiler's CS0649, so one problem gets one diagnostic.
/// </summary>
internal static class MemberAssignment
{
    public static void OnTypeStart(SymbolStartAnalysisContext context, ResultTypes types)
    {
        var type = (INamedTypeSymbol)context.Symbol;
        if (type.TypeKind is not (TypeKind.Class or TypeKind.Struct))
        {
            return;
        }

        var candidates = new List<Candidate>();
        foreach (var member in type.GetMembers())
        {
            if (member is not IFieldSymbol { IsConst: false } field || !types.HoldsResult(field.Type))
            {
                continue;
            }

            var declared = field.AssociatedSymbol ?? field;
            if (DefaultResultAnalyzer.IsInSource(declared) && !IsPositionalParameter(declared))
            {
                candidates.Add(new Candidate(field, declared, types.FindResult(field.Type, 0)!));
            }
        }

        if (candidates.Count == 0)
        {
            return;
        }

        var state = new TypeState(type, candidates);
        context.RegisterOperationBlockAction(state.OnBlock);
        context.RegisterSymbolEndAction(state.Report);
    }

    /// <summary>A static field initializer that reads a static Result field before that field is initialized.</summary>
    public static void AnalyzeStaticRead(OperationAnalysisContext context, ResultTypes types)
    {
        var reference = (IFieldReferenceOperation)context.Operation;
        if (context.ContainingSymbol is not IFieldSymbol { IsStatic: true } initialized)
        {
            return;
        }

        var read = reference.Field;
        if (!read.IsStatic
            || read.IsConst
            || SymbolEqualityComparer.Default.Equals(read, initialized)
            || !SymbolEqualityComparer.Default.Equals(read.ContainingType, initialized.ContainingType)
            || MaybeDefaultCalls.IsWrite(reference)
            || InsideFunction(reference)
            || !types.HoldsResult(read.Type))
        {
            return;
        }

        // Static initializers run in text order, before the static constructor body.
        var readLocation = read.Locations[0];
        var initializedLocation = initialized.Locations[0];
        var initializedEarlier = HasInitializer(read)
            && readLocation.SourceTree == initializedLocation.SourceTree
            && readLocation.SourceSpan.Start < initializedLocation.SourceSpan.Start;
        if (initializedEarlier)
        {
            return;
        }

        var result = types.FindResult(read.Type, 0)!;
        var diagnostic = Diagnostic.Create(
            Descriptors.UnassignedMember,
            reference.Syntax.GetLocation(),
            read.Name,
            $"is read by the initializer of '{initialized.Name}' before it is initialized",
            ResultTypes.Display(result));
        context.ReportDiagnostic(diagnostic);
    }

    internal static bool HasInitializer(ISymbol member)
    {
        foreach (var reference in member.DeclaringSyntaxReferences)
        {
            switch (reference.GetSyntax())
            {
                case VariableDeclaratorSyntax { Initializer: not null }:
                case PropertyDeclarationSyntax { Initializer: not null }:
                    return true;
            }
        }

        return false;
    }

    private static bool IsPositionalParameter(ISymbol member)
    {
        foreach (var reference in member.DeclaringSyntaxReferences)
        {
            if (reference.GetSyntax() is ParameterSyntax)
            {
                return true;
            }
        }

        return false;
    }

    private static bool InsideFunction(IOperation operation)
    {
        for (var parent = operation.Parent; parent is not null; parent = parent.Parent)
        {
            if (parent is IAnonymousFunctionOperation or ILocalFunctionOperation)
            {
                return true;
            }
        }

        return false;
    }

    private sealed class Candidate
    {
        public Candidate(IFieldSymbol field, ISymbol declared, ITypeSymbol result)
        {
            Field = field;
            Declared = declared;
            Result = result;
        }

        /// <summary>The storage: the field itself, or the property's backing field.</summary>
        public IFieldSymbol Field { get; }

        /// <summary>What the user declared: the field, or the auto-property.</summary>
        public ISymbol Declared { get; }

        public ITypeSymbol Result { get; }

        public bool Matches(ISymbol symbol) =>
            SymbolEqualityComparer.Default.Equals(symbol, Field) || SymbolEqualityComparer.Default.Equals(symbol, Declared);
    }

    private sealed class ConstructorFacts
    {
        public ConstructorFacts(IMethodSymbol? chainedTo, HashSet<ISymbol> assigned)
        {
            ChainedTo = chainedTo;
            Assigned = assigned;
        }

        public IMethodSymbol? ChainedTo { get; }

        public HashSet<ISymbol> Assigned { get; }
    }

    private sealed class TypeState
    {
        private readonly INamedTypeSymbol _type;
        private readonly List<Candidate> _candidates;
        private readonly ConcurrentDictionary<IMethodSymbol, ConstructorFacts> _constructors = new(SymbolEqualityComparer.Default);
        private readonly ConcurrentDictionary<ISymbol, bool> _written = new(SymbolEqualityComparer.Default);

        public TypeState(INamedTypeSymbol type, List<Candidate> candidates)
        {
            _type = type;
            _candidates = candidates;
        }

        public void OnBlock(OperationBlockAnalysisContext context)
        {
            foreach (var block in context.OperationBlocks)
            {
                RecordWrites(block);
            }

            if (context.OwningSymbol is not IMethodSymbol { MethodKind: MethodKind.Constructor or MethodKind.StaticConstructor } constructor
                || !SymbolEqualityComparer.Default.Equals(constructor.ContainingType, _type))
            {
                return;
            }

            // The analyzer driver passes a constructor's body and its : this(…)/: base(…) initializer as separate
            // blocks; both hang off one IConstructorBodyOperation.
            foreach (var block in context.OperationBlocks)
            {
                var body = block as IConstructorBodyOperation ?? block.Parent as IConstructorBodyOperation;
                if (body is not null)
                {
                    _constructors[constructor] = Analyze(body);
                    return;
                }
            }
        }

        public void Report(SymbolAnalysisContext context)
        {
            foreach (var candidate in _candidates)
            {
                var reason = FindReason(candidate);
                if (reason is null)
                {
                    continue;
                }

                var display = ResultTypes.Display(candidate.Result);
                var diagnostic = Diagnostic.Create(
                    Descriptors.UnassignedMember,
                    candidate.Declared.Locations[0],
                    candidate.Declared.Name,
                    reason,
                    display);
                context.ReportDiagnostic(diagnostic);
            }
        }

        private string? FindReason(Candidate candidate)
        {
            var field = candidate.Field;
            var neverWritten = !_written.ContainsKey(field) && !_written.ContainsKey(candidate.Declared);
            var compilerReports = neverWritten
                && field.AssociatedSymbol is null
                && field.DeclaredAccessibility is Accessibility.Private or Accessibility.Internal;

            // [ThreadStatic]: an initializer or the static constructor runs on one thread only. CA2019 reports the
            // initializer, CS0649 a field nothing writes; the rest is ours.
            if (HasAttribute(field, "System.ThreadStaticAttribute"))
            {
                return HasInitializer(field) || compilerReports
                    ? null
                    : "is [ThreadStatic], so it is default on every thread until that thread assigns it";
            }

            if (IsRequired(candidate.Declared))
            {
                return RequiredReason(candidate);
            }

            if (HasInitializer(candidate.Declared))
            {
                return null;
            }

            // A field nothing writes is CS0649's report, not ours (the compiler checks private and internal fields).
            if (compilerReports)
            {
                return null;
            }

            if (field.IsStatic)
            {
                foreach (var constructor in _type.StaticConstructors)
                {
                    if (AssignedBy(constructor, candidate, 0))
                    {
                        return null;
                    }
                }

                return "is not assigned by an initializer or the static constructor";
            }

            foreach (var constructor in _type.InstanceConstructors)
            {
                if (IsZeroingOrCopy(constructor))
                {
                    continue;
                }

                if (!AssignedBy(constructor, candidate, 0))
                {
                    return constructor.IsImplicitlyDeclared
                        ? "is not assigned by the implicit constructor"
                        : $"is not assigned by constructor '{Signature(constructor)}'";
                }
            }

            // A struct without constructors: default(S) and new S() are reported where they are written.
            return null;
        }

        private string? RequiredReason(Candidate candidate)
        {
            foreach (var constructor in _type.InstanceConstructors)
            {
                if (HasAttribute(constructor, "System.Diagnostics.CodeAnalysis.SetsRequiredMembersAttribute")
                    && !AssignedBy(constructor, candidate, 0))
                {
                    return $"is required, but constructor '{Signature(constructor)}' is marked [SetsRequiredMembers] and does not assign it";
                }
            }

            return null;
        }

        private bool IsZeroingOrCopy(IMethodSymbol constructor)
        {
            if (!constructor.IsImplicitlyDeclared)
            {
                return false;
            }

            // A struct's implicit parameterless constructor zeroes; default(S) is reported at the creation instead.
            if (_type.TypeKind == TypeKind.Struct && constructor.Parameters.Length == 0)
            {
                return true;
            }

            // A record's copy constructor copies every field from an existing instance.
            return constructor.Parameters.Length == 1
                && SymbolEqualityComparer.Default.Equals(constructor.Parameters[0].Type, _type);
        }

        private bool AssignedBy(IMethodSymbol constructor, Candidate candidate, int depth)
        {
            if (depth > 8 || !_constructors.TryGetValue(constructor, out var facts))
            {
                return false;
            }

            foreach (var assigned in facts.Assigned)
            {
                if (candidate.Matches(assigned))
                {
                    return true;
                }
            }

            return facts.ChainedTo is not null && AssignedBy(facts.ChainedTo, candidate, depth + 1);
        }

        private ConstructorFacts Analyze(IConstructorBodyOperation body)
        {
            IMethodSymbol? chainedTo = null;
            if (body.Initializer is not null)
            {
                foreach (var operation in body.Initializer.DescendantsAndSelf())
                {
                    if (operation is IInvocationOperation { TargetMethod: { MethodKind: MethodKind.Constructor } target }
                        && SymbolEqualityComparer.Default.Equals(target.ContainingType, _type)
                        && !target.IsImplicitlyDeclared)
                    {
                        chainedTo = target;
                        break;
                    }
                }
            }

            var assigned = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
            var statements = (IOperation?)body.BlockBody ?? body.ExpressionBody;
            if (statements is not null)
            {
                foreach (var candidate in _candidates)
                {
                    if (DefiniteAssignment.Assigns(statements, candidate.Matches))
                    {
                        assigned.Add(candidate.Declared);
                    }
                }
            }

            return new ConstructorFacts(chainedTo, assigned);
        }

        private void RecordWrites(IOperation block)
        {
            foreach (var operation in block.DescendantsAndSelf())
            {
                if (operation is IMemberReferenceOperation reference && MaybeDefaultCalls.IsWrite(reference))
                {
                    foreach (var candidate in _candidates)
                    {
                        if (candidate.Matches(reference.Member))
                        {
                            _written[candidate.Field] = true;
                        }
                    }
                }
            }
        }

        private static bool IsRequired(ISymbol member) =>
            member is IFieldSymbol { IsRequired: true } or IPropertySymbol { IsRequired: true };

        private static bool HasAttribute(ISymbol symbol, string fullName)
        {
            foreach (var attribute in symbol.GetAttributes())
            {
                if (attribute.AttributeClass?.ToDisplayString() == fullName)
                {
                    return true;
                }
            }

            return false;
        }

        private static string Signature(IMethodSymbol constructor) =>
            constructor.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat);
    }
}
