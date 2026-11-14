using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Spar.Security.Generator;

/// <summary>One finding of the analysis, with its location.</summary>
public sealed class PolicyDiagnostic
{
    public PolicyDiagnostic(DiagnosticDescriptor descriptor, Location location, string argument)
    {
        Descriptor = descriptor;
        Location = location;
        Argument = argument;
    }

    public DiagnosticDescriptor Descriptor { get; }

    public Location Location { get; }

    public string Argument { get; }

    public Diagnostic ToDiagnostic() => Diagnostic.Create(Descriptor, Location, Argument);
}

/// <summary>What one <c>ContentSecurity</c> call adds: directive sources, permissions, Trusted Types.</summary>
public sealed class PolicyAdditions
{
    public List<KeyValuePair<string, string>> Sources { get; } = new();

    public List<string> Allowed { get; } = new();

    public bool TrustedTypes { get; set; }

    public List<PolicyDiagnostic> Diagnostics { get; } = new();
}

/// <summary>
/// The single analysis behind the generator, the analyzer and the extractor. It reads
/// <c>spar.ContentSecurity(csp =&gt; csp.X(...).Y(...))</c> without running code: the call must be a statement directly
/// in the lambda passed to <c>SparApplication.Create</c>, and every value must be a compile-time constant.
/// </summary>
public static class PolicyAnalysis
{
    public const string BuilderType = "Spar.SparBuilder";
    public const string ApplicationType = "Spar.SparApplication";
    public const string SecurityBuilderType = "Spar.ContentSecurityBuilder";

    public static readonly DiagnosticDescriptor NotDirect = new(
        "SPAR101", "ContentSecurity must be declared directly in SparApplication.Create",
        "ContentSecurity must be a statement directly in the SparApplication.Create configuration lambda ({0}), so the build can read it without running code",
        "Spar.Security", DiagnosticSeverity.Error, isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor NotConstant = new(
        "SPAR102", "Policy values must be constants",
        "'{0}' is not a compile-time constant; use a literal or a const",
        "Spar.Security", DiagnosticSeverity.Error, isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UnsupportedShape = new(
        "SPAR103", "Unsupported ContentSecurity configuration",
        "Unsupported ContentSecurity configuration: {0}; write csp => csp.Method(...).Method(...)",
        "Spar.Security", DiagnosticSeverity.Error, isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor InvalidSource = new(
        "SPAR104", "Invalid policy source",
        "'{0}' is not a valid source expression (no spaces, ';' or ',')",
        "Spar.Security", DiagnosticSeverity.Error, isEnabledByDefault: true);

    public static readonly ImmutableArray<DiagnosticDescriptor> All =
        ImmutableArray.Create(NotDirect, NotConstant, UnsupportedShape, InvalidSource);

    private static readonly Dictionary<string, string> s_directives = new(StringComparer.Ordinal)
    {
        ["ScriptSrc"] = "script-src",
        ["StyleSrc"] = "style-src",
        ["ImgSrc"] = "img-src",
        ["FontSrc"] = "font-src",
        ["ConnectSrc"] = "connect-src",
        ["FrameSrc"] = "frame-src",
        ["MediaSrc"] = "media-src",
        ["WorkerSrc"] = "worker-src",
    };

    private static readonly string[] s_features =
    {
        "camera", "microphone", "geolocation", "usb", "serial", "hid", "bluetooth", "payment", "display-capture",
    };

    /// <summary>Cheap syntax filter: any invocation of a member named ContentSecurity.</summary>
    public static bool IsCandidate(SyntaxNode node) =>
        node is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax { Name.Identifier.ValueText: "ContentSecurity" } };

    /// <summary>Analyses one candidate invocation; returns null when it is not Spar's ContentSecurity.</summary>
    public static PolicyAdditions? Analyze(InvocationExpressionSyntax invocation, SemanticModel model, CancellationToken cancellationToken)
    {
        if (model.GetSymbolInfo(invocation, cancellationToken).Symbol is not IMethodSymbol method
            || method.ContainingType.ToDisplayString() != BuilderType)
        {
            return null;
        }

        var result = new PolicyAdditions();
        var where = DirectPlacement(invocation, model, cancellationToken);
        if (where is not null)
        {
            result.Diagnostics.Add(new PolicyDiagnostic(NotDirect, invocation.GetLocation(), where));
            return result;
        }

        if (invocation.ArgumentList.Arguments.Count != 1
            || invocation.ArgumentList.Arguments[0].Expression is not LambdaExpressionSyntax lambda
            || ParameterName(lambda) is not { } parameter)
        {
            result.Diagnostics.Add(new PolicyDiagnostic(UnsupportedShape, invocation.ArgumentList.GetLocation(), "the argument must be a lambda"));
            return result;
        }

        var chains = new List<ExpressionSyntax>();
        if (lambda.ExpressionBody is { } body)
        {
            chains.Add(body);
        }
        else if (lambda.Block is { } block)
        {
            foreach (var statement in block.Statements)
            {
                if (statement is ExpressionStatementSyntax expression)
                {
                    chains.Add(expression.Expression);
                }
                else
                {
                    result.Diagnostics.Add(new PolicyDiagnostic(UnsupportedShape, statement.GetLocation(), "only builder calls may appear in the lambda"));
                }
            }
        }

        foreach (var chain in chains)
        {
            ReadChain(chain, parameter, model, result, cancellationToken);
        }

        return result;
    }

    /// <summary>Null when the call is a statement (or the expression body) of the lambda given to SparApplication.Create.</summary>
    private static string? DirectPlacement(InvocationExpressionSyntax invocation, SemanticModel model, CancellationToken cancellationToken)
    {
        // Allow a fluent chain on the builder: spar.ContentSecurity(...).Other(...).
        SyntaxNode node = invocation;
        while (node.Parent is MemberAccessExpressionSyntax { Parent: InvocationExpressionSyntax outer })
        {
            node = outer;
        }

        LambdaExpressionSyntax? lambda = null;
        if (node.Parent is ExpressionStatementSyntax { Parent: BlockSyntax { Parent: LambdaExpressionSyntax inBlock } })
        {
            lambda = inBlock;
        }
        else if (node.Parent is LambdaExpressionSyntax inExpression && inExpression.ExpressionBody == node)
        {
            lambda = inExpression;
        }

        if (lambda is null)
        {
            var container = invocation.Ancestors().FirstOrDefault(a => a is StatementSyntax && a is not BlockSyntax and not ExpressionStatementSyntax);
            return container is null ? "it is not inside the configuration lambda" : $"it is inside {Describe(container)}";
        }

        if (lambda.Parent is not ArgumentSyntax { Parent: ArgumentListSyntax { Parent: InvocationExpressionSyntax create } }
            || model.GetSymbolInfo(create, cancellationToken).Symbol is not IMethodSymbol { Name: "Create" } createMethod
            || createMethod.ContainingType.ToDisplayString() != ApplicationType)
        {
            return "the enclosing lambda is not the one passed to SparApplication.Create";
        }

        // The builder must be the lambda's own parameter, not something captured.
        var receiver = ((MemberAccessExpressionSyntax)invocation.Expression).Expression;
        while (receiver is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax inner })
        {
            receiver = inner.Expression;
        }

        return receiver is IdentifierNameSyntax identifier && identifier.Identifier.ValueText == ParameterName(lambda)
            ? null
            : "it is not called on the configuration lambda's parameter";
    }

    private static string Describe(SyntaxNode statement) => statement switch
    {
        IfStatementSyntax => "an if statement",
        ForEachStatementSyntax or ForStatementSyntax or WhileStatementSyntax or DoStatementSyntax => "a loop",
        TryStatementSyntax => "a try statement",
        LocalFunctionStatementSyntax => "a local function",
        ReturnStatementSyntax => "a return statement",
        _ => "a " + statement.Kind(),
    };

    private static string? ParameterName(LambdaExpressionSyntax lambda) => lambda switch
    {
        SimpleLambdaExpressionSyntax simple => simple.Parameter.Identifier.ValueText,
        ParenthesizedLambdaExpressionSyntax { ParameterList.Parameters.Count: 1 } parenthesized =>
            parenthesized.ParameterList.Parameters[0].Identifier.ValueText,
        _ => null,
    };

    private static void ReadChain(ExpressionSyntax expression, string parameter, SemanticModel model, PolicyAdditions result, CancellationToken cancellationToken)
    {
        var calls = new List<InvocationExpressionSyntax>();
        var current = expression;
        while (current is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax access } call)
        {
            calls.Add(call);
            current = access.Expression;
        }

        if (current is not IdentifierNameSyntax root || root.Identifier.ValueText != parameter || calls.Count == 0)
        {
            result.Diagnostics.Add(new PolicyDiagnostic(UnsupportedShape, expression.GetLocation(), "a builder call chain on the lambda parameter was expected"));
            return;
        }

        calls.Reverse();
        foreach (var call in calls)
        {
            if (model.GetSymbolInfo(call, cancellationToken).Symbol is not IMethodSymbol method
                || method.ContainingType.ToDisplayString() != SecurityBuilderType)
            {
                result.Diagnostics.Add(new PolicyDiagnostic(UnsupportedShape, call.GetLocation(), "unknown builder method"));
                continue;
            }

            var values = new List<object?>();
            foreach (var argument in call.ArgumentList.Arguments)
            {
                var constant = model.GetConstantValue(argument.Expression, cancellationToken);
                if (!constant.HasValue)
                {
                    result.Diagnostics.Add(new PolicyDiagnostic(NotConstant, argument.GetLocation(), argument.Expression.ToString()));
                    continue;
                }

                values.Add(constant.Value);
                if (constant.Value is string source && (source.Length == 0 || source.IndexOfAny(new[] { ' ', ';', ',', '\t', '\n' }) >= 0))
                {
                    result.Diagnostics.Add(new PolicyDiagnostic(InvalidSource, argument.GetLocation(), source));
                }
            }

            switch (method.Name)
            {
                case "TrustedTypes":
                    result.TrustedTypes = true;
                    break;
                case "Allow":
                    foreach (var value in values)
                    {
                        if (value is int index && index >= 0 && index < s_features.Length)
                        {
                            result.Allowed.Add(s_features[index]);
                        }
                    }

                    break;
                default:
                    if (s_directives.TryGetValue(method.Name, out var directive))
                    {
                        foreach (var value in values.OfType<string>())
                        {
                            result.Sources.Add(new KeyValuePair<string, string>(directive, value));
                        }
                    }

                    break;
            }
        }
    }

    /// <summary>The same text rules as the runtime's <c>PolicyText</c>.</summary>
    public static (string Csp, string Permissions) Compose(IEnumerable<PolicyAdditions> additions)
    {
        var all = additions.ToList();
        string[] order =
        {
            "default-src", "script-src", "style-src", "img-src", "font-src", "connect-src", "worker-src", "manifest-src",
            "frame-src", "media-src", "base-uri", "form-action", "frame-ancestors", "object-src",
        };
        var baseValues = new Dictionary<string, string>
        {
            ["default-src"] = "'none'", ["script-src"] = "'self'", ["style-src"] = "'self'", ["img-src"] = "'self'",
            ["font-src"] = "'self'", ["connect-src"] = "'self'", ["worker-src"] = "'self'", ["manifest-src"] = "'self'",
            ["base-uri"] = "'none'", ["form-action"] = "'none'", ["frame-ancestors"] = "'none'", ["object-src"] = "'none'",
        };
        var parts = new List<string>();
        foreach (var directive in order)
        {
            var sources = new List<string>();
            if (baseValues.TryGetValue(directive, out var value))
            {
                sources.Add(value);
            }

            foreach (var source in all.SelectMany(a => a.Sources).Where(s => s.Key == directive).Select(s => s.Value))
            {
                if (!sources.Contains(source))
                {
                    sources.Add(source);
                }
            }

            if (sources.Count > 0)
            {
                parts.Add(directive + " " + string.Join(" ", sources));
            }
        }

        if (all.Any(a => a.TrustedTypes))
        {
            parts.Add("require-trusted-types-for 'script'");
        }

        var allowed = new HashSet<string>(all.SelectMany(a => a.Allowed));
        var permissions = string.Join(", ", s_features.Select(f => f + (allowed.Contains(f) ? "=(self)" : "=()")));
        return (string.Join("; ", parts), permissions);
    }

    /// <summary>Runs the analysis over a whole compilation (the extractor's entry point).</summary>
    public static (string Csp, string Permissions, List<PolicyDiagnostic> Diagnostics) AnalyzeCompilation(Compilation compilation, CancellationToken cancellationToken)
    {
        var additions = new List<PolicyAdditions>();
        foreach (var tree in compilation.SyntaxTrees)
        {
            var model = compilation.GetSemanticModel(tree);
            foreach (var invocation in tree.GetRoot(cancellationToken).DescendantNodes().OfType<InvocationExpressionSyntax>().Where(IsCandidate))
            {
                if (Analyze(invocation, model, cancellationToken) is { } found)
                {
                    additions.Add(found);
                }
            }
        }

        var (csp, permissions) = Compose(additions);
        return (csp, permissions, additions.SelectMany(a => a.Diagnostics).ToList());
    }

    public static string Literal(string value) => SymbolDisplay.FormatLiteral(value, quote: true);
}
