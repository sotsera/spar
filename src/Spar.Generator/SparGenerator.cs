using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Spar.Generator.Emit;

namespace Spar.Generator;

/// <summary>
/// Spar's source generator (plan section 2): every contract's dispatch, serializers and catalog, with the page security
/// <c>Program.cs</c> declares (plan section 3b); the interceptor that hands the catalog to <c>SparApplication.Create</c>;
/// the interceptor that hands <c>app.Run()</c> the desktop host the build references (outside the Test variant); and, in
/// the Test variant, the test entry point and typed test clients.
/// It reports nothing itself: the analyzer shows problems at their locations.
/// </summary>
[Generator]
public sealed class SparGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var own = context.SyntaxProvider.ForAttributeWithMetadataName(
                "Spar.SparContractAttribute",
                static (node, _) => node is ClassDeclarationSyntax or InterfaceDeclarationSyntax,
                static (attributed, _) => ContractAnalysis.Analyze((INamedTypeSymbol)attributed.TargetSymbol))
            .Where(static contract => contract is not null)
            .Collect();

        // The listed libraries' contracts are read from the compilation's references, so this step reruns with every
        // compilation; its result compares by value, so what follows it reruns only when they change.
        var libraries = context.AnalyzerConfigOptionsProvider.Select(static (options, _) =>
            options.GlobalOptions.TryGetValue(ContractLibraries.Property, out var listed) ? listed : "");
        var referenced = context.CompilationProvider.Combine(libraries).Select(static (input, _) =>
            ContractProblems.Referenced(input.Left, ContractProblems.ParseList(input.Right))
                .Select(type => ContractAnalysis.Analyze(type))
                .Where(model => model is not null)
                .Select(model => model!)
                .ToEquatable());

        var contracts = own.Combine(referenced)
            .Select(static (input, _) => ContractSet.Valid(input.Left.Select(model => model!).Concat(input.Right)).ToImmutableArray());

        var createCalls = context.SyntaxProvider.CreateSyntaxProvider(
                static (node, _) => node is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Create" } },
                static (syntax, cancellationToken) => CreateCall(syntax, cancellationToken))
            .Where(static call => call is not null)
            .Collect();

        // The page security Program.cs declares, read from the first Create call; null when there is none.
        var security = context.SyntaxProvider.CreateSyntaxProvider(
                static (node, _) => node is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Create" } },
                static (syntax, cancellationToken) => PageSecurity(syntax, cancellationToken))
            .Where(static model => model is not null)
            .Collect()
            .Select(static (models, _) => models.FirstOrDefault());

        var runCalls = context.SyntaxProvider.CreateSyntaxProvider(
                static (node, _) => node is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Run" } },
                static (syntax, cancellationToken) => RunCall(syntax, cancellationToken))
            .Where(static call => call is not null)
            .Collect();

        var isTest = context.AnalyzerConfigOptionsProvider.Select(static (options, _) =>
            options.GlobalOptions.TryGetValue("build_property.SparVariant", out var variant) && variant == "Test");
        var hosts = context.CompilationProvider.Select(static (compilation, _) =>
            new HostsModel(compilation.GetTypeByMetadataName(HostEmitter.WindowsHost) is not null));
        var entry = context.CompilationProvider.Select(static (compilation, cancellationToken) => EntryPoint(compilation, cancellationToken));

        context.RegisterSourceOutput(contracts.Combine(security), static (output, input) => EmitContracts(output, input.Left, input.Right));
        context.RegisterSourceOutput(createCalls, static (output, calls) => EmitInterceptors(output, calls!));
        context.RegisterSourceOutput(runCalls.Combine(hosts).Combine(isTest), static (output, input) =>
        {
            var ((calls, found), test) = input;

            // The test variant runs the app through its test session, never in a window.
            if (!test)
            {
                HostEmitter.Emit(output, calls!, found);
            }
        });
        context.RegisterSourceOutput(contracts.Combine(entry).Combine(isTest), static (output, input) =>
        {
            var ((models, entryPoint), test) = input;
            if (!test)
            {
                return;
            }

            if (entryPoint is { TopLevel: true, ReturnsInt: true })
            {
                output.AddSource("Spar.TestEntryPoint.g.cs", TestEmitter.EntryPoint(entryPoint));
            }

            output.AddSource("Spar.TestClients.g.cs", TestEmitter.Clients(models));
        });
    }

    private static void EmitContracts(SourceProductionContext output, ImmutableArray<ContractModel> models, PageSecurityModel? security)
    {
        var contracts = models.OrderBy(c => c.WireName, System.StringComparer.Ordinal).ToList();
        var types = contracts.SelectMany(c => c.Types).GroupBy(t => t.Key).Select(g => g.First()).OrderBy(t => t.Key, System.StringComparer.Ordinal).ToList();
        var records = contracts.SelectMany(c => c.Records).GroupBy(r => r.Key).Select(g => g.First()).ToList();
        var errors = contracts.SelectMany(c => c.Errors).GroupBy(e => e.Key).Select(g => g.First()).OrderBy(e => e.Key, System.StringComparer.Ordinal).ToList();
        var enums = contracts.SelectMany(c => c.Enums).GroupBy(e => e.Key).Select(g => g.First()).ToList();

        var code = new CodeWriter()
            .Line("// <auto-generated/> Spar: contract dispatch, serializers and catalog.")
            .Line("#nullable enable")
            .Line()
            .Open("namespace Spar.Generated");
        CatalogEmitter.Emit(code, contracts, security);
        code.Line();
        SerializerEmitter.Emit(code, types, records, errors, enums);
        code.Close();
        output.AddSource("Spar.Contracts.g.cs", code.ToString());
    }

    private static void EmitInterceptors(SourceProductionContext output, ImmutableArray<CreateCallModel> calls)
    {
        if (calls.Length == 0)
        {
            return;
        }

        var code = new CodeWriter()
            .Line("// <auto-generated/> Spar: connects SparApplication.Create to the app's generated contracts.")
            .Line("#nullable enable")
            .Line()
            .Open("namespace Spar.Generated")
            .Open("file static class SparCreateInterceptor");
        foreach (var call in calls)
        {
            code.Line($"[global::System.Runtime.CompilerServices.InterceptsLocation({call.Version}, \"{call.Data}\")]");
        }

        code.Line("public static global::Spar.SparApp Create(string[] args, global::System.Action<global::Spar.SparBuilder> configure) =>");
        code.Line("    global::Spar.SparApplication.Create(args, configure, SparContracts.Catalog);");
        code.Close()
            .Close()
            .Line()
            .Open("namespace System.Runtime.CompilerServices")
            .Line("[global::System.AttributeUsage(global::System.AttributeTargets.Method, AllowMultiple = true)]")
            .Open("file sealed class InterceptsLocationAttribute : global::System.Attribute")
            .Open("public InterceptsLocationAttribute(int version, string data)")
            .Line("_ = version;")
            .Line("_ = data;")
            .Close()
            .Close()
            .Close();
        output.AddSource("Spar.Create.g.cs", code.ToString());
    }

    private static CreateCallModel? CreateCall(GeneratorSyntaxContext syntax, CancellationToken cancellationToken)
    {
        var invocation = (InvocationExpressionSyntax)syntax.Node;
        if (syntax.SemanticModel.GetSymbolInfo(invocation, cancellationToken).Symbol is not IMethodSymbol method
            || method.ContainingType.ToDisplayString() != "Spar.SparApplication"
            || method.Parameters.Length != 2)
        {
            return null;
        }

        var location = syntax.SemanticModel.GetInterceptableLocation(invocation, cancellationToken);
        return location is null ? null : new CreateCallModel(location.Version, location.Data);
    }

    private static PageSecurityModel? PageSecurity(GeneratorSyntaxContext syntax, CancellationToken cancellationToken)
    {
        var invocation = (InvocationExpressionSyntax)syntax.Node;
        return PageSecurityAnalysis.IsCreate(invocation, syntax.SemanticModel, cancellationToken)
            ? PageSecurityAnalysis.Read(invocation, syntax.SemanticModel, cancellationToken).Model
            : null;
    }

    private static RunCallModel? RunCall(GeneratorSyntaxContext syntax, CancellationToken cancellationToken)
    {
        var invocation = (InvocationExpressionSyntax)syntax.Node;
        if (syntax.SemanticModel.GetSymbolInfo(invocation, cancellationToken).Symbol is not IMethodSymbol method
            || method.ContainingType.ToDisplayString() != "Spar.SparApp"
            || method.Parameters.Length != 0)
        {
            return null;
        }

        var location = syntax.SemanticModel.GetInterceptableLocation(invocation, cancellationToken);
        return location is null ? null : new RunCallModel(location.Version, location.Data);
    }

    private static EntryPointModel? EntryPoint(Compilation compilation, CancellationToken cancellationToken)
    {
        var method = compilation.GetEntryPoint(cancellationToken);
        if (method is null)
        {
            return null;
        }

        return new EntryPointModel(
            method.Name == WellKnownMemberNames.TopLevelStatementsEntryPointMethodName,
            method.ReturnType.SpecialType == SpecialType.System_Int32,
            method.ContainingType.Name,
            method.Name);
    }
}
