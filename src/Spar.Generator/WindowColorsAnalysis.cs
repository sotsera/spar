using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Spar.Generator;

/// <summary>
/// The window's colours as <c>Program.cs</c> declares them (plan section 3d), <c>#rrggbb</c> in lower case: the light
/// and the dark theme's, for the page's theme style the Vite plugin writes. <see cref="Default"/> when the app sets none.
/// </summary>
internal sealed record WindowColorsModel(string Light, string Dark)
{
    /// <summary>The colours a window has when the app sets none, as <c>WindowOptions</c> has them at run time.</summary>
    public static WindowColorsModel Default { get; } = new("#ffffff", "#1f1f1f");
}

/// <summary>What one <c>Create</c> call declares, and the located problems found while reading it.</summary>
internal sealed record WindowColorsRead(WindowColorsModel Model, IReadOnlyList<Diagnostic> Problems);

/// <summary>
/// Reads <c>spar.Window(w => w.Background(light, dark))</c> without running code, for the client step and the analyzer
/// (SPAR106): the <c>Window</c> call a statement of its own directly in the <c>SparApplication.Create</c> configuration,
/// as page security is (<see cref="PageSecurityAnalysis"/>), its lambda's statements chains of the options' methods,
/// and the colours constant <c>#rgb</c> or <c>#rrggbb</c> strings. The last <c>Background</c> call wins, as at run time.
/// <c>Size</c> and <c>MinSize</c> are not read, so they may be computed.
/// </summary>
internal static class WindowColorsAnalysis
{
    private const string OptionsType = "Spar.WindowOptions";

    private static readonly Regex Colour = new("^#(?:[0-9A-Fa-f]{3}|[0-9A-Fa-f]{6})$", RegexOptions.CultureInvariant);

    /// <summary>The first <c>Create</c> call's colours in the compilation's sources; null when there is none.</summary>
    public static WindowColorsRead? Find(Compilation compilation, CancellationToken cancellationToken)
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

    /// <summary>Reads the colours one <c>Create</c> call's configuration declares; a value that breaks a rule is reported.</summary>
    public static WindowColorsRead Read(InvocationExpressionSyntax create, SemanticModel model, CancellationToken cancellationToken)
    {
        var problems = new List<Diagnostic>();
        var colours = WindowColorsModel.Default;
        if (create.ArgumentList.Arguments.Count != 2
            || create.ArgumentList.Arguments[1].Expression is not LambdaExpressionSyntax lambda
            || PageSecurityAnalysis.Parameter(lambda) is not { } spar)
        {
            return new WindowColorsRead(colours, problems);
        }

        foreach (var statement in PageSecurityAnalysis.TopLevel(lambda))
        {
            foreach (var call in PageSecurityAnalysis.Chain(statement, spar))
            {
                if (IsWindowCall(call, model, cancellationToken))
                {
                    colours = ReadWindow(call, model, cancellationToken, colours, problems);
                }
            }
        }

        return new WindowColorsRead(colours, problems);
    }

    /// <summary>Every <c>Background</c> call of an app that sits where the build cannot read it (SPAR106).</summary>
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

    /// <summary>
    /// SPAR106 for a <c>Background</c> call that is not in a chain on the parameter of a <c>spar.Window(…)</c> lambda
    /// that sits directly in the <c>Create</c> configuration.
    /// </summary>
    public static Diagnostic? MisplacedCall(InvocationExpressionSyntax invocation, SemanticModel model, CancellationToken cancellationToken)
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Background" } access
            || PageSecurityAnalysis.MethodOf(invocation, model, cancellationToken)?.ContainingType.ToDisplayString() != OptionsType
            || IsReadable(invocation, model, cancellationToken))
        {
            return null;
        }

        if (InKind(invocation))
        {
            return Diagnostic.Create(Descriptors.WindowColours, access.Name.GetLocation(),
                "A window kind takes the main window's colours, and this Background(…) call fails the start: set the colours once, in spar.Window(w => w.Background(light, dark))");
        }

        return Diagnostic.Create(Descriptors.WindowColours, access.Name.GetLocation(),
            "This Background(…) call is not where the build reads it: write it in a chain on the parameter of spar.Window(w => …), as a statement of its own directly in the SparApplication.Create configuration, such as spar.Window(w => w.Background(light: \"#ffffff\", dark: \"#1f1f1f\"))");
    }

    /// <summary>Whether the call is inside the lambda of a two-argument Window(kind, …) call.</summary>
    private static bool InKind(InvocationExpressionSyntax call) =>
        call.Ancestors().OfType<InvocationExpressionSyntax>().Any(outer =>
            outer.Expression is MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Window" } && outer.ArgumentList.Arguments.Count == 2);

    private static bool IsWindowCall(InvocationExpressionSyntax call, SemanticModel model, CancellationToken cancellationToken) =>
        call.Expression is MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Window" }
        && PageSecurityAnalysis.MethodOf(call, model, cancellationToken)?.ContainingType.ToDisplayString() == "Spar.SparBuilder";

    /// <summary>The colours of one <c>spar.Window(w => …)</c> call: the last <c>Background</c> in its statements.</summary>
    private static WindowColorsModel ReadWindow(
        InvocationExpressionSyntax window,
        SemanticModel model,
        CancellationToken cancellationToken,
        WindowColorsModel colours,
        List<Diagnostic> problems)
    {
        if (window.ArgumentList.Arguments.Count != 1
            || window.ArgumentList.Arguments[0].Expression is not LambdaExpressionSyntax lambda
            || PageSecurityAnalysis.Parameter(lambda) is not { } parameter)
        {
            return colours;
        }

        foreach (var statement in PageSecurityAnalysis.TopLevel(lambda))
        {
            foreach (var option in PageSecurityAnalysis.Chain(statement, parameter))
            {
                if (option.Expression is MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Background" }
                    && PageSecurityAnalysis.MethodOf(option, model, cancellationToken)?.ContainingType.ToDisplayString() == OptionsType)
                {
                    colours = ReadBackground(option, model, cancellationToken, colours, problems);
                }
            }
        }

        return colours;
    }

    private static WindowColorsModel ReadBackground(
        InvocationExpressionSyntax call,
        SemanticModel model,
        CancellationToken cancellationToken,
        WindowColorsModel colours,
        List<Diagnostic> problems)
    {
        string? light = null;
        string? dark = null;
        var arguments = call.ArgumentList.Arguments;
        for (var index = 0; index < arguments.Count; index++)
        {
            var argument = arguments[index];
            var name = argument.NameColon?.Name.Identifier.ValueText ?? (index == 0 ? "light" : "dark");
            var value = ColourOf(argument, model, cancellationToken, problems);
            if (name == "light")
            {
                light = value;
            }
            else
            {
                dark = value;
            }
        }

        return light is null || dark is null ? colours : new WindowColorsModel(light, dark);
    }

    /// <summary>One argument as <c>#rrggbb</c> in lower case, or null with its problem reported.</summary>
    private static string? ColourOf(ArgumentSyntax argument, SemanticModel model, CancellationToken cancellationToken, List<Diagnostic> problems)
    {
        var constant = model.GetConstantValue(argument.Expression, cancellationToken);
        if (!constant.HasValue || constant.Value is not string text)
        {
            problems.Add(Diagnostic.Create(Descriptors.WindowColours, argument.GetLocation(),
                $"'{argument.Expression}' is not a constant: write the colour itself, such as \"#1f1f1f\", or a const string"));
            return null;
        }

        if (!Colour.IsMatch(text))
        {
            problems.Add(Diagnostic.Create(Descriptors.WindowColours, argument.GetLocation(),
                $"'{text}' is not a colour of the form #rrggbb or #rgb"));
            return null;
        }

        var digits = text.Substring(1).ToLowerInvariant();
        return digits.Length == 3 ? $"#{digits[0]}{digits[0]}{digits[1]}{digits[1]}{digits[2]}{digits[2]}" : "#" + digits;
    }

    /// <summary>Whether a <c>Background</c> call is in a chain on a <c>spar.Window</c> lambda's parameter, that call readable.</summary>
    private static bool IsReadable(InvocationExpressionSyntax background, SemanticModel model, CancellationToken cancellationToken)
    {
        // Up the chain to its statement, then to the lambda whose parameter roots it.
        ExpressionSyntax top = background;
        while (top.Parent is MemberAccessExpressionSyntax outerAccess && outerAccess.Expression == top
            && outerAccess.Parent is InvocationExpressionSyntax outer)
        {
            top = outer;
        }

        var lambda = top.Parent switch
        {
            ExpressionStatementSyntax { Parent: BlockSyntax { Parent: LambdaExpressionSyntax inBlock } } => inBlock,
            LambdaExpressionSyntax body when body.ExpressionBody == top => body,
            _ => null,
        };
        if (lambda is null || PageSecurityAnalysis.Parameter(lambda) is not { } parameter
            || !PageSecurityAnalysis.Chain(top, parameter).Contains(background))
        {
            return false;
        }

        // The main window's call only: a kind takes the main window's colours, and its Background fails the start.
        return lambda.Parent is ArgumentSyntax { Parent: ArgumentListSyntax { Parent: InvocationExpressionSyntax window } }
            && window.ArgumentList.Arguments.Count == 1
            && IsWindowCall(window, model, cancellationToken)
            && PageSecurityAnalysis.IsDirectlyInCreate(window, model, cancellationToken);
    }
}
