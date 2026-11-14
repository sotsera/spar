using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Simplification;

namespace Spar.Analyzers.CodeFixes;

/// <summary>
/// SPAR050: replace a default Result with Result.Success or an error case that has a parameterless constructor.
/// SPAR051: use the throwing LINQ method (First, Last, Single, ElementAt) or Nullable.Value.
/// SPAR053: declare the member as Result? or make it required. Both make the compiler list every place to update.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(DefaultResultCodeFix))]
[Shared]
public sealed class DefaultResultCodeFix : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds =>
    [
        Descriptors.DefaultResult.Id,
        Descriptors.MayReturnDefault.Id,
        Descriptors.UnassignedMember.Id,
    ];

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        var model = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null || model is null)
        {
            return;
        }

        foreach (var diagnostic in context.Diagnostics)
        {
            var node = root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true);
            switch (diagnostic.Id)
            {
                case "SPAR050":
                    RegisterExplicitCase(context, diagnostic, node, model);
                    break;
                case "SPAR051":
                    RegisterStrictCall(context, diagnostic, node);
                    break;
                case "SPAR053":
                    RegisterMemberFixes(context, diagnostic, node);
                    break;
            }
        }
    }

    private static void RegisterExplicitCase(CodeFixContext context, Diagnostic diagnostic, SyntaxNode node, SemanticModel model)
    {
        if (node is not (LiteralExpressionSyntax or DefaultExpressionSyntax or BaseObjectCreationExpressionSyntax))
        {
            return;
        }

        var expression = (ExpressionSyntax)node;
        var info = model.GetTypeInfo(expression, context.CancellationToken);
        if ((info.Type ?? info.ConvertedType) is not INamedTypeSymbol
            {
                Name: "Result",
                ContainingNamespace: { Name: "Spar", ContainingNamespace.IsGlobalNamespace: true },
            } result)
        {
            return;
        }

        // An explicitly typed default(R) or new R() may sit in a var declaration, so keep the type with a cast that
        // the simplifier removes where the target type already makes it redundant.
        var keepType = expression is DefaultExpressionSyntax or ObjectCreationExpressionSyntax;
        var resultType = SyntaxFactory.ParseTypeName(result.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
            .WithAdditionalAnnotations(Simplifier.Annotation);

        if (result.Arity == 1)
        {
            var success = SyntaxFactory.ParseExpression("global::Spar.Result.Success");
            Register(context, diagnostic, "Use Result.Success", "Result.Success", expression, Wrap(success, resultType, keepType));
        }

        var error = (INamedTypeSymbol)result.TypeArguments[result.Arity - 1];
        foreach (var errorCase in error.GetTypeMembers())
        {
            if (!IsCaseWithoutArguments(errorCase, error))
            {
                continue;
            }

            var caseName = errorCase.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var creation = SyntaxFactory.ObjectCreationExpression(
                    SyntaxFactory.ParseTypeName(caseName).WithAdditionalAnnotations(Simplifier.Annotation))
                .WithArgumentList(SyntaxFactory.ArgumentList());
            var title = $"Use error case '{error.Name}.{errorCase.Name}'";
            Register(context, diagnostic, title, "case:" + caseName, expression, Wrap(creation, resultType, keepType));
        }
    }

    private static void RegisterStrictCall(CodeFixContext context, Diagnostic diagnostic, SyntaxNode node)
    {
        if (node is not InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax access } invocation)
        {
            return;
        }

        var name = access.Name.Identifier.ValueText;
        if (name == "GetValueOrDefault" && invocation.ArgumentList.Arguments.Count == 0)
        {
            var value = SyntaxFactory.MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                access.Expression,
                SyntaxFactory.IdentifierName("Value"));
            Register(context, diagnostic, "Use .Value (throws when there is no value)", "Value", invocation, value.WithTriviaFrom(invocation));
            return;
        }

        var strict = name switch
        {
            "FirstOrDefault" => "First",
            "LastOrDefault" => "Last",
            "SingleOrDefault" => "Single",
            "ElementAtOrDefault" => "ElementAt",
            _ => null,
        };
        if (strict is null)
        {
            return;
        }

        var renamed = access.Name is GenericNameSyntax generic
            ? (SimpleNameSyntax)generic.WithIdentifier(SyntaxFactory.Identifier(strict))
            : SyntaxFactory.IdentifierName(strict);
        var replacement = invocation.WithExpression(access.WithName(renamed.WithTriviaFrom(access.Name)));
        Register(context, diagnostic, $"Use {strict}() (throws when nothing is found)", "strict", invocation, replacement);
    }

    private static void RegisterMemberFixes(CodeFixContext context, Diagnostic diagnostic, SyntaxNode node)
    {
        TypeSyntax? type = node switch
        {
            VariableDeclaratorSyntax { Parent: VariableDeclarationSyntax declaration } => declaration.Type,
            PropertyDeclarationSyntax property => property.Type,
            _ => null,
        };
        if (type is null || type is NullableTypeSyntax)
        {
            return;
        }

        var nullable = SyntaxFactory.NullableType(type.WithoutTrivia()).WithTriviaFrom(type);
        context.RegisterCodeFix(
            CodeAction.Create(
                "Declare as Result? (unset is null)",
                token => ReplaceAsync(context.Document, type, nullable, token),
                "nullable"),
            diagnostic);

        if (node is PropertyDeclarationSyntax { Parent: TypeDeclarationSyntax } declared
            && !declared.Modifiers.Any(SyntaxKind.StaticKeyword)
            && !declared.Modifiers.Any(SyntaxKind.RequiredKeyword)
            && declared.AccessorList is { } accessors
            && accessors.Accessors.Any(a => a.IsKind(SyntaxKind.SetAccessorDeclaration) || a.IsKind(SyntaxKind.InitAccessorDeclaration)))
        {
            var required = declared.AddModifiers(SyntaxFactory.Token(SyntaxKind.RequiredKeyword).WithTrailingTrivia(SyntaxFactory.Space));
            context.RegisterCodeFix(
                CodeAction.Create("Make the property required", token => ReplaceAsync(context.Document, declared, required, token), "required"),
                diagnostic);
        }
    }

    private static bool IsCaseWithoutArguments(INamedTypeSymbol candidate, INamedTypeSymbol error)
    {
        if (candidate.IsAbstract || !SymbolEqualityComparer.Default.Equals(candidate.BaseType, error))
        {
            return false;
        }

        foreach (var constructor in candidate.InstanceConstructors)
        {
            if (constructor.Parameters.Length == 0
                && constructor.DeclaredAccessibility is Accessibility.Public or Accessibility.Internal)
            {
                return true;
            }
        }

        return false;
    }

    private static ExpressionSyntax Wrap(ExpressionSyntax value, TypeSyntax type, bool keepType)
    {
        var annotated = value.WithAdditionalAnnotations(Simplifier.Annotation);
        if (!keepType)
        {
            return annotated;
        }

        return SyntaxFactory.CastExpression(type, SyntaxFactory.ParenthesizedExpression(annotated).WithAdditionalAnnotations(Simplifier.Annotation))
            .WithAdditionalAnnotations(Simplifier.Annotation);
    }

    private static void Register(
        CodeFixContext context,
        Diagnostic diagnostic,
        string title,
        string key,
        SyntaxNode original,
        SyntaxNode replacement)
    {
        var annotated = replacement.WithTriviaFrom(original).WithAdditionalAnnotations(Simplifier.Annotation, Formatter.Annotation);
        context.RegisterCodeFix(
            CodeAction.Create(title, token => ReplaceAsync(context.Document, original, annotated, token), key),
            diagnostic);
    }

    private static async Task<Document> ReplaceAsync(Document document, SyntaxNode original, SyntaxNode replacement, CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null)
        {
            return document;
        }

        var changed = document.WithSyntaxRoot(root.ReplaceNode(original, replacement));
        changed = await Simplifier.ReduceAsync(changed, Simplifier.Annotation, cancellationToken: cancellationToken).ConfigureAwait(false);
        return await Formatter.FormatAsync(changed, Formatter.Annotation, cancellationToken: cancellationToken).ConfigureAwait(false);
    }
}
