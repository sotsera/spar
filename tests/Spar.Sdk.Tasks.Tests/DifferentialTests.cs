using System.Diagnostics;

namespace Spar.Sdk.Tasks.Tests;

/// <summary>
/// The differential test of the generated JavaScript (decision "Generated JavaScript is validated against tsc in
/// Spar's own tests"; plan section 2, "Generated client"): the typed twin of each module (<see cref="TypeScriptTwin"/>)
/// compiles with <c>tsc --declaration</c> under TypeScript 6 and 7, so every reader, writer and decoder type-checks
/// against the declared types; and what <c>tsc</c> writes, printed without comments or layout, is exactly the
/// <c>.js</c> and <c>.d.ts</c> the client step wrote. It checks the modules Spar generates, not the user's build.
/// </summary>
public sealed class DifferentialTests
{
    public static TheoryData<string, string> Cases => new()
    {
        { "typescript", nameof(Fixtures.Shop) },
        { "typescript", nameof(Fixtures.Edges) },
        { "typescript7", nameof(Fixtures.Shop) },
        { "typescript7", nameof(Fixtures.Edges) },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task The_generated_modules_are_what_tsc_writes_from_their_typed_twin(string compiler, string fixture)
    {
        var source = fixture == nameof(Fixtures.Shop) ? Fixtures.Shop : Fixtures.Edges;
        var directory = Repository.Scratch("differential");
        try
        {
            var package = Path.Combine(directory, "package");
            ClientHarness.Write(source, Path.Combine(directory, "app"), package);
            var twin = Path.Combine(directory, "twin");
            var files = WriteTwin(source, package, twin);

            var (compiled, compileOutput) = await NodeAsync(Path.Combine(Repository.Client, "node_modules", compiler, "bin", "tsc"),
                "-p", Path.Combine(twin, "tsconfig.json"), "--pretty", "false");
            Assert.True(compiled == 0, $"The typed twin does not compile with {compiler}:\n{compileOutput}");

            string[] compared = [.. files.SelectMany(f => new[] { Path.ChangeExtension(f, ".js"), Path.ChangeExtension(f, ".d.ts") })];
            string[] comparison =
            [
                Path.Combine(Repository.Root, "tests", "Spar.Sdk.Tasks.Tests", "TypeScript", "compare.cjs"),
                Path.Combine(Repository.Client, "node_modules", "typescript"), package, Path.Combine(twin, "out"), .. compared,
            ];
            var (same, differences) = await NodeAsync(comparison);
            Assert.True(same == 0, differences);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// The twin in a folder of its own beside the package's <c>package.json</c> and SDK, so it imports
    /// <c>@spar/app/sdk</c> and <c>./sdk/…</c> as the generated modules do; returns the twin's file names.
    /// </summary>
    private static IReadOnlyList<string> WriteTwin(string source, string package, string twin)
    {
        Directory.CreateDirectory(twin);
        File.Copy(Path.Combine(package, "package.json"), Path.Combine(twin, "package.json"));
        CopyFolder(Path.Combine(package, "sdk"), Path.Combine(twin, "sdk"));

        var files = TypeScriptTwin.Files(ClientHarness.Plan(source));
        foreach (var (name, text) in files)
        {
            File.WriteAllText(Path.Combine(twin, name), text);
        }

        var list = string.Join(", ", files.Keys.Select(name => $"\"{name}\""));
        File.WriteAllText(Path.Combine(twin, "tsconfig.json"), $$"""
            {
              "compilerOptions": {
                "target": "ES2022",
                "lib": ["ES2024", "DOM"],
                "module": "NodeNext",
                "moduleResolution": "NodeNext",
                "strict": true,
                "exactOptionalPropertyTypes": true,
                "verbatimModuleSyntax": true,
                "skipLibCheck": false,
                "declaration": true,
                "rootDir": ".",
                "outDir": "out",
                "newLine": "lf",
                "types": []
              },
              "files": [{{list}}]
            }
            """);
        return [.. files.Keys];
    }

    private static void CopyFolder(string source, string target)
    {
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var destination = Path.Combine(target, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination);
        }
    }

    private static async Task<(int Exit, string Output)> NodeAsync(params string[] arguments)
    {
        var start = new ProcessStartInfo("node") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        var errors = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        await process.WaitForExitAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromMinutes(2));
        return (process.ExitCode, await output + await errors);
    }
}
