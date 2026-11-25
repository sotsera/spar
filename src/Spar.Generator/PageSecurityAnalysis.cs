using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Spar.Hosting;

namespace Spar.Generator;

/// <summary>
/// The page security <c>Program.cs</c> declares (plan section 3b), as the build reads it without running code:
/// <c>spar.ContentSecurity(csp => …)</c> and <c>spar.Permissions(p => …)</c>, each a statement of its own directly in
/// the <c>SparApplication.Create</c> configuration, with constant values. Normalized as <c>CspBuilder</c> does at run
/// time (one rule, <see cref="CspSourceRule"/>), so the Development check finds the two equal. Sources keep the order
/// first declared; permissions follow the enum's order.
/// </summary>
internal sealed record PageSecurityModel(
    EquatableArray<string> FontSrc,
    EquatableArray<string> ImgSrc,
    EquatableArray<string> ConnectSrc,
    EquatableArray<string> MediaSrc,
    EquatableArray<string> ScriptSrc,
    EquatableArray<string> StyleSrc,
    EquatableArray<string> FrameSrc,
    bool AllowInlineStyles,
    bool AllowEval,
    bool AllowWebAssembly,
    bool StyleNonce,
    bool TrustedTypes,
    bool Integrity,
    string Listener,
    string Endpoint,
    EquatableArray<string> Permissions);

/// <summary>What one <c>Create</c> call declares, and the located problems found while reading it.</summary>
internal sealed record PageSecurityRead(PageSecurityModel Model, IReadOnlyList<Diagnostic> Problems);

/// <summary>
/// Reads the page security of a <c>SparApplication.Create</c> call for the generator, the analyzer and the client step
/// (one analysis, so the three agree), and finds the calls that sit somewhere the build cannot read (SPAR101).
/// </summary>
internal static class PageSecurityAnalysis
{
    private const string BuilderType = "Spar.SparBuilder";
    private const string CspType = "Spar.CspBuilder";
    private const string PermissionsType = "Spar.PermissionsBuilder";
    private const string ReportsType = "Spar.CspReports";

    private static readonly string[] ReportNames = ["Always", "DevelopmentOnly", "Off"];

    /// <summary>Whether <paramref name="invocation"/> is <c>SparApplication.Create(args, configure)</c>.</summary>
    public static bool IsCreate(InvocationExpressionSyntax invocation, SemanticModel model, CancellationToken cancellationToken) =>
        invocation.Expression is MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Create" }
        && MethodOf(invocation, model, cancellationToken) is { } method
        && method.ContainingType.ToDisplayString() == "Spar.SparApplication"
        && method.Parameters.Length == 2;

    /// <summary>
    /// The method a call binds to, or its one candidate while the code around it has an error (as the user types), so
    /// the analysis keeps reading the rest of the configuration.
    /// </summary>
    internal static IMethodSymbol? MethodOf(InvocationExpressionSyntax invocation, SemanticModel model, CancellationToken cancellationToken)
    {
        var info = model.GetSymbolInfo(invocation, cancellationToken);
        return info.Symbol as IMethodSymbol ?? (info.CandidateSymbols.Length == 1 ? info.CandidateSymbols[0] as IMethodSymbol : null);
    }

    /// <summary>The first <c>Create</c> call in the compilation's sources, read; null when there is none.</summary>
    public static PageSecurityRead? Find(Compilation compilation, CancellationToken cancellationToken)
    {
        foreach (var tree in compilation.SyntaxTrees)
        {
            var model = compilation.GetSemanticModel(tree);
            foreach (var invocation in tree.GetRoot(cancellationToken).DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                if (IsCreate(invocation, model, cancellationToken))
                {
                    var read = Read(invocation, model, cancellationToken);
                    var misplaced = Misplaced(compilation, cancellationToken);
                    return read with { Problems = [.. misplaced, .. read.Problems] };
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Whether the compilation is an app, whose top-level statements configure Spar (SPAR060): only there must the page
    /// security sit directly in <c>Create</c>. A library or a test project may call the builder's methods freely; the
    /// Development check catches a declaration the build did not read.
    /// </summary>
    public static bool IsApp(Compilation compilation, CancellationToken cancellationToken) =>
        compilation.GetEntryPoint(cancellationToken)?.Name == WellKnownMemberNames.TopLevelStatementsEntryPointMethodName;

    /// <summary>Every <c>ContentSecurity</c> or <c>Permissions</c> call of an app the build cannot read (SPAR101).</summary>
    public static IEnumerable<Diagnostic> Misplaced(Compilation compilation, CancellationToken cancellationToken)
    {
        if (!IsApp(compilation, cancellationToken))
        {
            yield break;
        }

        foreach (var tree in compilation.SyntaxTrees)
        {
            var model = compilation.GetSemanticModel(tree);
            foreach (var invocation in tree.GetRoot(cancellationToken).DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                if (MisplacedCall(invocation, model, cancellationToken) is { } problem)
                {
                    yield return problem;
                }
            }
        }
    }

    /// <summary>SPAR101 for a <c>ContentSecurity</c> or <c>Permissions</c> call not directly in the <c>Create</c> configuration.</summary>
    public static Diagnostic? MisplacedCall(InvocationExpressionSyntax invocation, SemanticModel model, CancellationToken cancellationToken)
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax { Name.Identifier.ValueText: "ContentSecurity" or "Permissions" } access
            || !IsBuilderMethod(invocation, model, cancellationToken)
            || IsDirectlyInCreate(invocation, model, cancellationToken))
        {
            return null;
        }

        return Diagnostic.Create(Descriptors.NotDirectlyInCreate, access.Name.GetLocation(), access.Name.Identifier.ValueText);
    }

    /// <summary>Reads what one <c>Create</c> call's configuration declares; values that break a rule are reported and left out.</summary>
    public static PageSecurityRead Read(InvocationExpressionSyntax create, SemanticModel model, CancellationToken cancellationToken)
    {
        var reader = new Reader(model, cancellationToken);
        if (create.ArgumentList.Arguments.Count == 2 && create.ArgumentList.Arguments[1].Expression is LambdaExpressionSyntax lambda
            && Parameter(lambda) is { } spar)
        {
            foreach (var statement in TopLevel(lambda))
            {
                foreach (var call in Chain(statement, spar))
                {
                    reader.Builder(call);
                }
            }
        }

        return new PageSecurityRead(reader.Model(), reader.Problems);
    }

    /// <summary>
    /// Whether the call sits directly in the configuration: in a chain of builder calls on the lambda's parameter, which
    /// is a statement of the lambda's block or its expression body, the lambda being <c>Create</c>'s second argument.
    /// </summary>
    internal static bool IsDirectlyInCreate(InvocationExpressionSyntax call, SemanticModel model, CancellationToken cancellationToken)
    {
        ExpressionSyntax top = call;
        while (top.Parent is MemberAccessExpressionSyntax outerAccess && outerAccess.Expression == top
            && outerAccess.Parent is InvocationExpressionSyntax outer && IsBuilderMethod(outer, model, cancellationToken))
        {
            top = outer;
        }

        var root = ((MemberAccessExpressionSyntax)call.Expression).Expression;
        while (root is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax inner } invocation
            && IsBuilderMethod(invocation, model, cancellationToken))
        {
            root = inner.Expression;
        }

        var lambda = top.Parent switch
        {
            ExpressionStatementSyntax { Parent: BlockSyntax { Parent: LambdaExpressionSyntax inBlock } } => inBlock,
            LambdaExpressionSyntax body when body.ExpressionBody == top => body,
            _ => null,
        };
        return lambda is not null
            && root is IdentifierNameSyntax name
            && Parameter(lambda)?.Identifier.ValueText == name.Identifier.ValueText
            && lambda.Parent is ArgumentSyntax { Parent: ArgumentListSyntax { Parent: InvocationExpressionSyntax create } } argument
            && create.ArgumentList.Arguments.IndexOf(argument) == 1
            && IsCreate(create, model, cancellationToken);
    }

    private static bool IsBuilderMethod(InvocationExpressionSyntax invocation, SemanticModel model, CancellationToken cancellationToken) =>
        MethodOf(invocation, model, cancellationToken)?.ContainingType.ToDisplayString() == BuilderType;

    internal static ParameterSyntax? Parameter(LambdaExpressionSyntax lambda) => lambda switch
    {
        SimpleLambdaExpressionSyntax simple => simple.Parameter,
        ParenthesizedLambdaExpressionSyntax { ParameterList.Parameters.Count: 1 } parenthesized => parenthesized.ParameterList.Parameters[0],
        _ => null,
    };

    /// <summary>The lambda's statements as expressions: its expression body, or its block's expression statements.</summary>
    internal static IEnumerable<ExpressionSyntax> TopLevel(LambdaExpressionSyntax lambda)
    {
        if (lambda.ExpressionBody is { } body)
        {
            return [body];
        }

        return lambda.Block?.Statements.OfType<ExpressionStatementSyntax>().Select(s => s.Expression) ?? [];
    }

    /// <summary>
    /// The calls of a chain on <paramref name="parameter"/>, in the order they run (<c>p.A().B()</c> gives A, then B);
    /// empty when the expression is not such a chain.
    /// </summary>
    internal static List<InvocationExpressionSyntax> Chain(ExpressionSyntax expression, ParameterSyntax parameter)
    {
        var calls = new List<InvocationExpressionSyntax>();
        var current = expression;
        while (current is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax access } invocation)
        {
            calls.Add(invocation);
            current = access.Expression;
        }

        var rooted = current is IdentifierNameSyntax name && name.Identifier.ValueText == parameter.Identifier.ValueText;
        if (!rooted)
        {
            calls.Clear();
        }

        calls.Reverse();
        return calls;
    }

    /// <summary>Collects one configuration's declarations and problems.</summary>
    private sealed class Reader(SemanticModel model, CancellationToken cancellationToken)
    {
        private readonly Dictionary<CspDirective, List<string>> _sources = new()
        {
            [CspDirective.Font] = [],
            [CspDirective.Image] = [],
            [CspDirective.Connect] = [],
            [CspDirective.Media] = [],
            [CspDirective.Script] = [],
            [CspDirective.Style] = [],
            [CspDirective.Frame] = [],
        };

        /// <summary>The declared permissions by their enum value, so they are written in the enum's order, as at run time.</summary>
        private readonly SortedDictionary<int, string> _permissions = new();
        private bool _allowInlineStyles;
        private bool _allowEval;
        private bool _allowWebAssembly;
        private bool _styleNonce;
        private bool _withoutTrustedTypes;
        private bool _withoutIntegrity;
        private string _listener = "Always";
        private string _endpoint = "Always";
        private Location? _inlineStyles;
        private Location? _nonce;

        public List<Diagnostic> Problems { get; } = [];

        public void Builder(InvocationExpressionSyntax call)
        {
            if (MethodOf(call, model, cancellationToken) is not { } method
                || method.ContainingType.ToDisplayString() != BuilderType)
            {
                return;
            }

            var argument = call.ArgumentList.Arguments.Count == 1 ? call.ArgumentList.Arguments[0].Expression : null;
            switch (method.Name)
            {
                case "ContentSecurity":
                    Configuration(argument, call, CspType, "csp => csp.FontSrc(\"https://fonts.gstatic.com\")", Csp);
                    break;
                case "Permissions":
                    Configuration(argument, call, PermissionsType, "p => p.Allow(Permission.ClipboardRead)", Permission);
                    break;
            }
        }

        public PageSecurityModel Model()
        {
            if (_inlineStyles is not null && _nonce is not null)
            {
                Problems.Add(Diagnostic.Create(Descriptors.NonceWithInlineStyles, _nonce));
            }

            return new PageSecurityModel(
                _sources[CspDirective.Font].ToEquatable(),
                _sources[CspDirective.Image].ToEquatable(),
                _sources[CspDirective.Connect].ToEquatable(),
                _sources[CspDirective.Media].ToEquatable(),
                _sources[CspDirective.Script].ToEquatable(),
                _sources[CspDirective.Style].ToEquatable(),
                _sources[CspDirective.Frame].ToEquatable(),
                _allowInlineStyles,
                _allowEval,
                _allowWebAssembly,
                _styleNonce,
                !_withoutTrustedTypes,
                !_withoutIntegrity,
                _listener,
                _endpoint,
                _permissions.Values.ToEquatable());
        }

        /// <summary>The builder method's argument: a lambda whose statements are chains of the options' methods.</summary>
        private void Configuration(
            ExpressionSyntax? argument,
            InvocationExpressionSyntax call,
            string optionsType,
            string example,
            System.Action<InvocationExpressionSyntax, IMethodSymbol> read)
        {
            if (argument is not LambdaExpressionSyntax lambda || Parameter(lambda) is not { } parameter)
            {
                Shape(argument ?? (SyntaxNode)call, $"pass a lambda that calls the options' methods directly, such as {example}");
                return;
            }

            var statements = new List<SyntaxNode>();
            if (lambda.ExpressionBody is { } body)
            {
                statements.Add(body);
            }
            else if (lambda.Block is { } block)
            {
                statements.AddRange(block.Statements);
            }

            foreach (var statement in statements)
            {
                var expression = statement switch
                {
                    ExpressionStatementSyntax expressionStatement => expressionStatement.Expression,
                    ExpressionSyntax plain => plain,
                    _ => null,
                };
                var calls = expression is null ? [] : Chain(expression, parameter);
                if (calls.Count == 0)
                {
                    Shape(statement, $"write only calls of the options' methods on '{parameter.Identifier.ValueText}', such as {example}");
                    continue;
                }

                foreach (var option in calls)
                {
                    if (MethodOf(option, model, cancellationToken) is { } method
                        && method.ContainingType.ToDisplayString() == optionsType)
                    {
                        read(option, method);
                    }
                    else
                    {
                        Shape(option, $"'{option}' is not a method of the options; write only calls of their methods, such as {example}");
                    }
                }
            }
        }

        private void Csp(InvocationExpressionSyntax call, IMethodSymbol method)
        {
            switch (method.Name)
            {
                case "FontSrc":
                    Sources(call, CspDirective.Font);
                    break;
                case "ImgSrc":
                    Sources(call, CspDirective.Image);
                    break;
                case "ConnectSrc":
                    Sources(call, CspDirective.Connect);
                    break;
                case "MediaSrc":
                    Sources(call, CspDirective.Media);
                    break;
                case "ScriptSrc":
                    Sources(call, CspDirective.Script);
                    break;
                case "StyleSrc":
                    Sources(call, CspDirective.Style);
                    break;
                case "FrameSrc":
                    Sources(call, CspDirective.Frame);
                    break;
                case "AllowInlineStyles":
                    _allowInlineStyles = true;
                    _inlineStyles ??= NameOf(call);
                    break;
                case "AllowEval":
                    _allowEval = true;
                    break;
                case "AllowWebAssembly":
                    _allowWebAssembly = true;
                    break;
                case "StyleNonce":
                    _styleNonce = true;
                    _nonce ??= NameOf(call);
                    break;
                case "WithoutTrustedTypes":
                    _withoutTrustedTypes = true;
                    break;
                case "WithoutIntegrity":
                    _withoutIntegrity = true;
                    break;
                case "Reporting":
                    Reporting(call, method);
                    break;
            }
        }

        private void Sources(InvocationExpressionSyntax call, CspDirective directive)
        {
            var into = _sources[directive];
            foreach (var argument in call.ArgumentList.Arguments)
            {
                if (argument.Expression is ArrayCreationExpressionSyntax or ImplicitArrayCreationExpressionSyntax or CollectionExpressionSyntax)
                {
                    Problems.Add(Diagnostic.Create(Descriptors.NotConstant, argument.GetLocation(), argument.ToString(),
                        "Pass each source as an argument of its own"));
                    continue;
                }

                var constant = model.GetConstantValue(argument.Expression, cancellationToken);
                if (!constant.HasValue || (constant.Value is not null and not string))
                {
                    Problems.Add(Diagnostic.Create(Descriptors.NotConstant, argument.GetLocation(), argument.ToString(),
                        "Write the source itself, or a const string"));
                    continue;
                }

                var checkedSource = CspSourceRule.Check(constant.Value as string, directive);
                if (checkedSource.Problem is { } problem)
                {
                    Problems.Add(Diagnostic.Create(Descriptors.InvalidValue, argument.GetLocation(), problem));
                }
                else if (!into.Contains(checkedSource.Normalized!))
                {
                    into.Add(checkedSource.Normalized!);
                }
            }
        }

        private void Reporting(InvocationExpressionSyntax call, IMethodSymbol method)
        {
            var arguments = call.ArgumentList.Arguments;
            for (var index = 0; index < arguments.Count; index++)
            {
                var argument = arguments[index];
                var name = argument.NameColon?.Name.Identifier.ValueText
                    ?? (index < method.Parameters.Length ? method.Parameters[index].Name : "");
                var value = ReportsValue(argument);
                if (value is null)
                {
                    continue;
                }

                if (name == "listener")
                {
                    _listener = value;
                }
                else if (name == "endpoint")
                {
                    _endpoint = value;
                }
            }
        }

        private string? ReportsValue(ArgumentSyntax argument)
        {
            var constant = model.GetConstantValue(argument.Expression, cancellationToken);
            if (!constant.HasValue || constant.Value is not int value)
            {
                Problems.Add(Diagnostic.Create(Descriptors.NotConstant, argument.GetLocation(), argument.ToString(),
                    "Write a CspReports member, such as CspReports.DevelopmentOnly"));
                return null;
            }

            if (value < 0 || value >= ReportNames.Length || model.GetTypeInfo(argument.Expression, cancellationToken).Type?.ToDisplayString() != ReportsType)
            {
                Problems.Add(Diagnostic.Create(Descriptors.InvalidValue, argument.GetLocation(),
                    $"'{argument}' is not a CspReports member: write CspReports.Always, CspReports.DevelopmentOnly or CspReports.Off."));
                return null;
            }

            return ReportNames[value];
        }

        private void Permission(InvocationExpressionSyntax call, IMethodSymbol method)
        {
            if (method.Name != "Allow" || call.ArgumentList.Arguments.Count != 1)
            {
                return;
            }

            var argument = call.ArgumentList.Arguments[0];
            var constant = model.GetConstantValue(argument.Expression, cancellationToken);
            var type = model.GetTypeInfo(argument.Expression, cancellationToken).Type as INamedTypeSymbol;
            if (!constant.HasValue || constant.Value is not int value || type is null)
            {
                Problems.Add(Diagnostic.Create(Descriptors.NotConstant, argument.GetLocation(), argument.ToString(),
                    "Write a Permission member, such as Permission.ClipboardRead"));
                return;
            }

            var member = type.GetMembers().OfType<IFieldSymbol>().FirstOrDefault(f => f.HasConstantValue && Equals(f.ConstantValue, value));
            if (member is null)
            {
                Problems.Add(Diagnostic.Create(Descriptors.InvalidValue, argument.GetLocation(),
                    $"'{argument}' is not a Permission member: write one by name, such as Permission.ClipboardRead."));
                return;
            }

            _permissions[value] = member.Name;
        }

        private void Shape(SyntaxNode node, string fix) =>
            Problems.Add(Diagnostic.Create(Descriptors.UnsupportedShape, node.GetLocation(), fix));

        private static Location NameOf(InvocationExpressionSyntax call) =>
            call.Expression is MemberAccessExpressionSyntax access ? access.Name.GetLocation() : call.GetLocation();
    }
}
