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
/// the interceptor that hands <c>app.Run()</c> the desktop host the build references (in the Test variant too, for the
/// test session's Window surface); the build's identity, which the host logs and <c>spar doctor</c> reads from the file
/// (<see cref="IdentityEmitter"/>); in the Development variant, the contracts' shape, so a contract edit restarts the app
/// under Hot Reload instead of being patched (<see cref="ShapeEmitter"/>); and, in the Test variant, the test entry
/// point and typed test clients.
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
        var isDevelopment = context.AnalyzerConfigOptionsProvider.Select(static (options, _) =>
            options.GlobalOptions.TryGetValue("build_property.SparVariant", out var variant) && variant == "Development");
        var hosts = context.CompilationProvider.Select(static (compilation, _) =>
            new HostsModel(
                compilation.GetTypeByMetadataName(HostEmitter.WindowsHost) is not null,
                compilation.GetTypeByMetadataName(HostEmitter.LinuxHost) is not null,
                compilation.GetTypeByMetadataName(HostEmitter.LoopbackHost) is not null));
        var entry = context.CompilationProvider.Select(static (compilation, cancellationToken) => EntryPoint(compilation, cancellationToken));

        // The app id the build checked (SparAppId, plan section 3c), for the catalog; empty when the project has none.
        var appId = context.AnalyzerConfigOptionsProvider.Select(static (options, _) =>
            options.GlobalOptions.TryGetValue("build_property.SparAppId", out var id) ? id.Trim() : "");

        // A black-box test project's tested apps (SparBlackBoxTests): the build passes each as an AdditionalFiles item
        // with its name, app id, executable and folder as metadata; the Projects class names them for the fixture.
        var blackBox = context.AnalyzerConfigOptionsProvider.Select(static (options, _) =>
            options.GlobalOptions.TryGetValue("build_property.SparBlackBoxTests", out var value) && string.Equals(value.Trim(), "true", System.StringComparison.OrdinalIgnoreCase));
        var testedApps = context.AdditionalTextsProvider
            .Combine(context.AnalyzerConfigOptionsProvider)
            .Select(static (input, _) => TestedApp(input.Right.GetOptions(input.Left)))
            .Where(static app => app is not null)
            .Collect();
        context.RegisterSourceOutput(testedApps.Combine(blackBox), static (output, input) =>
        {
            var (apps, enabled) = input;
            if (enabled && apps.Length > 0)
            {
                output.AddSource("Spar.Projects.g.cs", ProjectsEmitter.Emit(apps.Select(app => app!).ToImmutableArray()));
            }
        });

        context.RegisterSourceOutput(contracts.Combine(security).Combine(appId).Combine(isDevelopment), static (output, input) =>
            EmitContracts(output, input.Left.Left.Left, input.Left.Left.Right, input.Left.Right, input.Right));

        // The build's identity (IdentityEmitter): the variant, the revision and the tree's state from the build, and the
        // contracts' fingerprint. Its own output, so an identity change leaves the contracts' code and shape alone.
        var identity = context.AnalyzerConfigOptionsProvider.Select(static (options, _) =>
        {
            string Read(string name) => options.GlobalOptions.TryGetValue("build_property." + name, out var value) ? value : "";
            return new IdentityModel(Read("SparVariant"), Read("SparBuildRevision"), Read("SparBuildDirty"));
        });
        context.RegisterSourceOutput(contracts.Combine(identity), static (output, input) =>
            output.AddSource("Spar.BuildIdentity.g.cs", IdentityEmitter.Emit(input.Right, CatalogEmitter.Fingerprint(input.Left))));
        context.RegisterSourceOutput(createCalls, static (output, calls) => EmitInterceptors(output, calls!));
        // Every variant hands Run its host: in the test variant the test session runs the app instead, and opens its
        // window through the host for the Window surface (plan section 5, level 5).
        context.RegisterSourceOutput(runCalls.Combine(hosts), static (output, input) =>
        {
            var (calls, found) = input;
            HostEmitter.Emit(output, calls!, found);
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

        // The scenes (SceneEmitter): in a project that references Spar.Testing, the valid [SparScene] methods, by name.
        var scenes = context.SyntaxProvider.ForAttributeWithMetadataName(
                SceneAnalysis.Attribute,
                static (node, _) => node is MethodDeclarationSyntax,
                static (attributed, _) => SceneAnalysis.Analyze((IMethodSymbol)attributed.TargetSymbol, attributed.Attributes[0]))
            .Where(static scene => scene is not null)
            .Collect()
            .Select(static (found, _) => found
                .Select(scene => scene!)
                .GroupBy(scene => scene.Name, System.StringComparer.Ordinal)
                .Where(group => group.Count() == 1)
                .Select(group => group.First())
                .OrderBy(scene => scene.Name, System.StringComparer.Ordinal)
                .ToEquatable());
        var sceneHost = context.CompilationProvider.Select(static (compilation, _) =>
            (Registry: compilation.GetTypeByMetadataName(SceneAnalysis.Registry) is not null,
             Xunit: compilation.GetTypeByMetadataName("Xunit.FactAttribute") is not null));
        context.RegisterSourceOutput(scenes.Combine(sceneHost), static (output, input) =>
        {
            var (found, host) = input;
            if (host.Registry)
            {
                output.AddSource("Spar.Scenes.g.cs", SceneEmitter.Emit(found, host.Xunit));
            }
        });
    }

    /// <summary>A tested app from its item's metadata (<c>SparBlackBoxApp</c> and the rest), or null for any other additional file.</summary>
    private static TestedAppModel? TestedApp(Microsoft.CodeAnalysis.Diagnostics.AnalyzerConfigOptions options)
    {
        string Read(string name) => options.TryGetValue("build_metadata.AdditionalFiles." + name, out var value) ? value.Trim() : "";

        if (!string.Equals(Read("SparBlackBoxApp"), "true", System.StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var name = Read("SparAppName");
        var appId = Read("SparAppId");
        var executable = Read("SparAppExecutable");
        return name.Length == 0 || appId.Length == 0 || executable.Length == 0
            ? null
            : new TestedAppModel(name, appId, executable, Read("SparAppDirectory"));
    }

    /// <param name="development">A Development build: the contracts' shape is added too (<see cref="ShapeEmitter"/>).</param>
    private static void EmitContracts(
        SourceProductionContext output,
        ImmutableArray<ContractModel> models,
        PageSecurityModel? security,
        string appId,
        bool development)
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
        CatalogEmitter.Emit(code, contracts, security, appId);
        code.Line();
        SerializerEmitter.Emit(code, types, records, errors, enums);
        code.Close();
        var source = code.ToString();
        output.AddSource("Spar.Contracts.g.cs", source);
        if (development)
        {
            output.AddSource("Spar.ContractShape.g.cs", ShapeEmitter.Emit(source));
        }
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
