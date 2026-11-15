using System.Collections.Immutable;
using Microsoft.Build.Framework;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Spar.Sdk.Tasks.Client;

namespace Spar.Sdk.Tasks.Tests;

/// <summary>Compiles C# contracts in memory and runs the client step's analysis, layout and emitters on them.</summary>
internal static class ClientHarness
{
    private static readonly CSharpParseOptions ParseOptions = new(LanguageVersion.Preview, DocumentationMode.Parse);

    private static readonly Lazy<ImmutableArray<MetadataReference>> References = new(LoadReferences);

    /// <summary>The package's folder relative to the project's in an app: <c>obj/spar/client</c>.</summary>
    private const string SourcePrefix = "../../../";

    private static string DefaultProject => Path.Combine(Repository.Root, "app");

    /// <summary>
    /// The layout of <paramref name="source"/>, compiled as <c>Contracts.cs</c> in <paramref name="projectDirectory"/>;
    /// the contracts must have no problem.
    /// </summary>
    public static ClientLayout Plan(string source, string? projectDirectory = null)
    {
        var read = Read(source, projectDirectory);
        Assert.Empty(read.Problems);
        return ClientLayout.Plan(read.Contracts);
    }

    /// <summary>What the client step reads from <paramref name="source"/>: the valid contracts and the problem count.</summary>
    public static ContractRead Read(string source, string? projectDirectory = null)
    {
        var project = projectDirectory ?? DefaultProject;
        var compilation = Compile(source, project);
        return ContractSource.Read(compilation, project, TestContext.Current.CancellationToken);
    }

    /// <summary>The compiler's errors for <paramref name="source"/>, for a test that must start from valid C#.</summary>
    public static IReadOnlyList<string> Errors(string source) =>
        [.. Compile(source, DefaultProject).GetDiagnostics(TestContext.Current.CancellationToken)
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => d.ToString())];

    private static CSharpCompilation Compile(string source, string projectDirectory) =>
        CSharpCompilation.Create(
            "App",
            [CSharpSyntaxTree.ParseText(source, ParseOptions, path: Path.Combine(projectDirectory, "Contracts.cs"))],
            References.Value,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

    /// <summary>The package's files as text, by path; the SDK's own files are left out.</summary>
    public static SortedDictionary<string, string> Emit(string source)
    {
        var layout = Plan(source);

        var files = ClientPackage.Files(layout, new AbsolutePath(Repository.SdkBuild), new AbsolutePath(Repository.SdkSources), "1.2.3", SourcePrefix);
        var text = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var (path, bytes) in files.Where(f => !f.Key.StartsWith("sdk/", StringComparison.Ordinal)))
        {
            text[path] = System.Text.Encoding.UTF8.GetString(bytes);
        }

        return text;
    }

    /// <summary>
    /// Writes <paramref name="source"/> as <c>Contracts.cs</c> into <paramref name="projectDirectory"/>, and the whole
    /// package, the SDK included, into <paramref name="packageDirectory"/>, with maps from one to the other.
    /// </summary>
    public static void Write(string source, string projectDirectory, string packageDirectory)
    {
        Directory.CreateDirectory(projectDirectory);
        File.WriteAllText(Path.Combine(projectDirectory, "Contracts.cs"), source);

        var layout = Plan(source, projectDirectory);
        var prefix = Path.GetRelativePath(packageDirectory, projectDirectory).Replace('\\', '/') + "/";
        var files = ClientPackage.Files(layout, new AbsolutePath(Repository.SdkBuild), new AbsolutePath(Repository.SdkSources), "1.2.3", prefix);
        Directory.CreateDirectory(packageDirectory);
        ClientPackage.Sync(new AbsolutePath(packageDirectory), files);
    }

    /// <summary>The BCL and Spar: the runtime lists them among its trusted assemblies.</summary>
    private static ImmutableArray<MetadataReference> LoadReferences()
    {
        var trusted = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator);
        return [.. trusted.Where(path => !Path.GetFileName(path).StartsWith("Microsoft.CodeAnalysis", StringComparison.Ordinal))
            .Select(path => MetadataReference.CreateFromFile(path))];
    }
}

/// <summary>Folders of this repository the tests use, found from the test's output folder.</summary>
internal static class Repository
{
    private static readonly Lazy<string> RootPath = new(FindRoot);

    public static string Root => RootPath.Value;

    public static string Client => Path.Combine(Root, "src", "Spar.Client");

    /// <summary>The compiled SDK.</summary>
    public static string SdkBuild
    {
        get
        {
            var folder = Path.Combine(Client, "obj", "dist");
            return File.Exists(Path.Combine(folder, "sdk.js"))
                ? folder
                : throw new InvalidOperationException($"The SDK is not built: run 'npm run build' in src/Spar.Client ({folder}).");
        }
    }

    public static string SdkSources => Path.Combine(Client, "src");

    /// <summary>A disposable folder under <c>artifacts/client-check/</c>.</summary>
    public static string Scratch(string name) =>
        Path.Combine(Root, "artifacts", "client-check", $"{name}-{Guid.NewGuid():N}"[..(name.Length + 9)]);

    private static string FindRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Spar.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("These tests run from a build inside the Spar repository.");
    }
}
