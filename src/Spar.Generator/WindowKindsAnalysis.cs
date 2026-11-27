using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Spar.Generator;

/// <summary>A window kind's name, and whether it is a modal: null when the build cannot tell from its options.</summary>
internal sealed record WindowKindName(string Name, bool? Modal);

/// <summary>What one <c>Create</c> call declares as window kinds, in order, and the located problems found reading it.</summary>
internal sealed record WindowKindsRead(IReadOnlyList<WindowKindName> Kinds, IReadOnlyList<Diagnostic> Problems);

/// <summary>
/// Reads the window kinds <c>Program.cs</c> declares (<c>spar.Window("settings", w => …)</c>, decision "Secondary and
/// modal windows") without running code, for the client step's <c>WindowKind</c> union and the analyzer (SPAR107): each
/// call a statement of its own directly in the <c>SparApplication.Create</c> configuration, as page security is, and its
/// name a constant string the runtime takes (camelCase ASCII, not <c>main</c>). Of the options only <c>Modal()</c> is
/// read, for the <c>ModalKind</c> union. The runtime's own checks (a duplicate, a missing route) stay the start's.
/// </summary>
internal static class WindowKindsAnalysis
{
    private static readonly Regex Name = new("^[a-z][A-Za-z0-9]{0,63}$", RegexOptions.CultureInvariant);

    /// <summary>The first <c>Create</c> call's kinds in the compilation's sources; null when there is none.</summary>
    public static WindowKindsRead? Find(Compilation compilation, CancellationToken cancellationToken)
    {
        foreach (var tree in compilation.SyntaxTrees)
        {
            var model = compilation.GetSemanticModel(tree);
            foreach (var invocation in tree.GetRoot(cancellationToken).DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                if (PageSecurityAnalysis.IsCreate(invocation, model, cancellationToken))
                {
                    var read = Read(invocation, model, cancellationToken);
                    var misplaced = Misplaced(compilation, cancellationToken);
                    return read with { Problems = [.. misplaced, .. read.Problems] };
                }
            }
        }

        return null;
    }

    /// <summary>The kinds one <c>Create</c> call's configuration declares; a name that breaks a rule is reported.</summary>
    public static WindowKindsRead Read(InvocationExpressionSyntax create, SemanticModel model, CancellationToken cancellationToken)
    {
        var kinds = new List<WindowKindName>();
        var problems = new List<Diagnostic>();
        if (create.ArgumentList.Arguments.Count != 2
            || create.ArgumentList.Arguments[1].Expression is not LambdaExpressionSyntax lambda
            || PageSecurityAnalysis.Parameter(lambda) is not { } spar)
        {
            return new WindowKindsRead(kinds, problems);
        }

        foreach (var statement in PageSecurityAnalysis.TopLevel(lambda))
        {
            foreach (var call in PageSecurityAnalysis.Chain(statement, spar))
            {
                if (IsKindCall(call, model, cancellationToken)
                    && NameOf(call, model, cancellationToken, problems) is { } name
                    && !kinds.Any(kind => kind.Name == name))
                {
                    kinds.Add(new WindowKindName(name, IsModal(call, model, cancellationToken)));
                }
            }
        }

        return new WindowKindsRead(kinds, problems);
    }

    /// <summary>
    /// Whether a kind's options call <c>Modal()</c>: read from a lambda whose body is a chain of the options' methods on
    /// its parameter (<c>w =&gt; w.Route(…).Modal()</c>), or its statements of such chains; null for options in any
    /// other shape, which the page's unions then take as either.
    /// </summary>
    private static bool? IsModal(InvocationExpressionSyntax kindCall, SemanticModel model, CancellationToken cancellationToken)
    {
        if (kindCall.ArgumentList.Arguments[1].Expression is not LambdaExpressionSyntax lambda
            || PageSecurityAnalysis.Parameter(lambda) is not { } options)
        {
            return null;
        }

        var modal = false;
        foreach (var statement in PageSecurityAnalysis.TopLevel(lambda))
        {
            var calls = PageSecurityAnalysis.Chain(statement, options);
            if (calls.Count == 0)
            {
                return null;
            }

            foreach (var call in calls)
            {
                if (PageSecurityAnalysis.MethodOf(call, model, cancellationToken) is not { } method
                    || method.ContainingType.ToDisplayString() != "Spar.WindowOptions")
                {
                    return null;
                }

                modal |= method.Name == "Modal";
            }
        }

        return modal;
    }

    /// <summary>Every <c>spar.Window(kind, …)</c> call of an app that sits where the build cannot read it (SPAR107).</summary>
    public static IEnumerable<Diagnostic> Misplaced(Compilation compilation, CancellationToken cancellationToken)
    {
        if (!PageSecurityAnalysis.IsApp(compilation, cancellationToken))
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

    /// <summary>SPAR107 for a kind's <c>Window</c> call that is not directly in the <c>Create</c> configuration.</summary>
    public static Diagnostic? MisplacedCall(InvocationExpressionSyntax invocation, SemanticModel model, CancellationToken cancellationToken)
    {
        if (!IsKindCall(invocation, model, cancellationToken) || PageSecurityAnalysis.IsDirectlyInCreate(invocation, model, cancellationToken))
        {
            return null;
        }

        var access = (MemberAccessExpressionSyntax)invocation.Expression;
        return Diagnostic.Create(Descriptors.WindowKinds, access.Name.GetLocation(),
            "This spar.Window(kind, …) call is not where the build reads it: write it as a statement of its own directly in the SparApplication.Create configuration, on its builder parameter; not inside a condition, a loop, a helper method or another lambda");
    }

    /// <summary>A <c>SparBuilder.Window(string, …)</c> call: a kind, not the main window.</summary>
    private static bool IsKindCall(InvocationExpressionSyntax call, SemanticModel model, CancellationToken cancellationToken) =>
        call.Expression is MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Window" }
        && call.ArgumentList.Arguments.Count == 2
        && PageSecurityAnalysis.MethodOf(call, model, cancellationToken) is { } method
        && method.ContainingType.ToDisplayString() == "Spar.SparBuilder"
        && method.Parameters.Length == 2
        && method.Parameters[0].Type.SpecialType == SpecialType.System_String;

    private static string? NameOf(InvocationExpressionSyntax call, SemanticModel model, CancellationToken cancellationToken, List<Diagnostic> problems)
    {
        var argument = call.ArgumentList.Arguments[0];
        var constant = model.GetConstantValue(argument.Expression, cancellationToken);
        if (!constant.HasValue || constant.Value is not string name)
        {
            problems.Add(Diagnostic.Create(Descriptors.WindowKinds, argument.GetLocation(),
                $"'{argument.Expression}' is not a constant: write the kind's name itself, such as \"settings\", or a const string"));
            return null;
        }

        if (!Name.IsMatch(name) || name == "main")
        {
            problems.Add(Diagnostic.Create(Descriptors.WindowKinds, argument.GetLocation(),
                $"'{name}' is not a window kind's name: camelCase ASCII letters and digits, starting with a lower-case letter, at most 64 characters, and not 'main', the main window's name"));
            return null;
        }

        return name;
    }
}
