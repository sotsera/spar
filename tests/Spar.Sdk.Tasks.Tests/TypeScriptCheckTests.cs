using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Spar.Sdk.Tasks.Tests;

/// <summary>
/// The generated package as an app's TypeScript sees it (plan section 2, "Generated client"; test type 9): installed
/// as <c>node_modules/@spar/app</c>, compiled by TypeScript 6 and 7 at the strictest settings with <c>skipLibCheck</c>
/// off, under bundler and Node module resolution. A consumer compiles cleanly, and each deliberate mistake fails with
/// its exact code on its line. The <c>.js</c> modules load in Node and export what the declarations say.
/// </summary>
public sealed partial class TypeScriptCheckTests
{
    public static TheoryData<string, string> Matrix => new()
    {
        { "typescript", "Bundler" },
        { "typescript", "NodeNext" },
        { "typescript7", "Bundler" },
        { "typescript7", "NodeNext" },
    };

    [Theory]
    [MemberData(nameof(Matrix))]
    public async Task A_consumer_compiles_with_no_error(string compiler, string resolution)
    {
        var directory = Prepare("consumer.ts", resolution);
        try
        {
            var (exit, output) = await RunAsync(Tsc(compiler), "-p", Path.Combine(directory, "tsconfig.json"), "--pretty", "false");

            Assert.True(exit == 0, output);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [MemberData(nameof(Matrix))]
    public async Task Each_mistake_fails_with_its_code_on_its_line(string compiler, string resolution)
    {
        var directory = Prepare("mistakes.ts", resolution);
        try
        {
            var (_, output) = await RunAsync(Tsc(compiler), "-p", Path.Combine(directory, "tsconfig.json"), "--pretty", "false");

            var expected = File.ReadAllLines(Path.Combine(directory, "mistakes.ts"))
                .Select((line, index) => (Line: index + 1, Match: Expectation().Match(line)))
                .Where(e => e.Match.Success)
                .Select(e => $"{e.Line} {e.Match.Groups[1].Value}");
            var actual = Reported().Matches(output).Select(m => $"{m.Groups[1].Value} {m.Groups[2].Value}").Distinct();
            Assert.Equal(expected.Order(StringComparer.Ordinal), actual.Order(StringComparer.Ordinal));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task The_modules_load_in_Node_and_export_each_command()
    {
        var directory = Prepare("consumer.ts", "NodeNext");
        try
        {
            var package = new Uri(Path.Combine(directory, "node_modules", "@spar", "app") + Path.DirectorySeparatorChar);
            const string Script = """
                const base = process.argv[1];
                const { orders } = await import(new URL("orders.js", base));
                const { stock } = await import(new URL("stock.js", base));
                const { fingerprint } = await import(new URL("spar.js", base));
                console.log([Object.keys(orders).join(","), Object.keys(stock).join(","), typeof fingerprint, Object.isFrozen(orders)].join("|"));
                """;

            var (exit, output) = await RunAsync("--input-type=module", "-e", Script, package.AbsoluteUri);

            Assert.True(exit == 0, output);
            Assert.Equal("place,cancel,clear,count|find,list|string|true", output.Trim());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Go_to_definition_on_a_generated_name_opens_its_CSharp_declaration()
    {
        var directory = Prepare("consumer.ts", "Bundler");
        try
        {
            File.Copy(Path.Combine(Repository.Root, "tests", "Spar.Sdk.Tasks.Tests", "TypeScript", "definitions.cjs"), Path.Combine(directory, "definitions.cjs"));
            string[][] probes =
            [
                ["orders.place(basket)", "place"],
                ["return orders.place", "orders"],
                ["outOfStock: error", "outOfStock"],
                ["const basket: Basket", "Basket"],
                ["{ sku: \"A-100\", stock: 3n", "sku"],
                [".unwrap()).price", "price"],
                ["stock.list()", "list"],
            ];
            var typeScript = Path.Combine(Repository.Client, "node_modules", "typescript");

            var (exit, output) = await RunAsync(Path.Combine(directory, "definitions.cjs"), typeScript, directory, System.Text.Json.JsonSerializer.Serialize(probes, ProbeJson.Default.StringArrayArray));

            Assert.True(exit == 0, output);
            var landed = System.Text.Json.JsonDocument.Parse(output).RootElement;
            Assert.Equal($"app/Contracts.cs:{LineOf("PlaceAsync")}", landed.GetProperty("orders.place(basket)").GetString());
            Assert.Equal($"app/Contracts.cs:{LineOf("class OrdersService")}", landed.GetProperty("return orders.place").GetString());
            Assert.Equal($"app/Contracts.cs:{LineOf("record OutOfStock")}", landed.GetProperty("outOfStock: error").GetString());
            Assert.Equal($"app/Contracts.cs:{LineOf("record Basket")}", landed.GetProperty("const basket: Basket").GetString());
            Assert.Equal($"app/Contracts.cs:{LineOf("record Item(")}", landed.GetProperty("{ sku: \"A-100\", stock: 3n").GetString());
            Assert.Equal($"app/Contracts.cs:{LineOf("record Item(")}", landed.GetProperty(".unwrap()).price").GetString());
            Assert.Equal($"app/Contracts.cs:{LineOf("ListAsync")}", landed.GetProperty("stock.list()").GetString());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>The 1-based line of the Shop fixture that holds <paramref name="text"/>.</summary>
    private static int LineOf(string text) =>
        Fixtures.Shop.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').ToList().FindIndex(line => line.Contains(text, StringComparison.Ordinal)) + 1;

    /// <summary>A scratch app: the Shop package under <c>node_modules/@spar/app</c>, one TypeScript file, its tsconfig.</summary>
    private static string Prepare(string file, string resolution)
    {
        var directory = Repository.Scratch(Path.GetFileNameWithoutExtension(file));
        ClientHarness.Write(Fixtures.Shop, Path.Combine(directory, "app"), Path.Combine(directory, "node_modules", "@spar", "app"));
        File.Copy(Path.Combine(Repository.Root, "tests", "Spar.Sdk.Tasks.Tests", "TypeScript", file), Path.Combine(directory, file));
        File.WriteAllText(Path.Combine(directory, "package.json"), "{ \"private\": true, \"type\": \"module\" }\n");

        var module = resolution == "NodeNext" ? "NodeNext" : "ESNext";
        File.WriteAllText(Path.Combine(directory, "tsconfig.json"), $$"""
            {
              "compilerOptions": {
                "target": "ES2022",
                "lib": ["ES2024", "DOM"],
                "module": "{{module}}",
                "moduleResolution": "{{resolution}}",
                "strict": true,
                "exactOptionalPropertyTypes": true,
                "noUncheckedIndexedAccess": true,
                "noImplicitOverride": true,
                "noImplicitReturns": true,
                "noPropertyAccessFromIndexSignature": true,
                "noFallthroughCasesInSwitch": true,
                "noUnusedLocals": true,
                "noUnusedParameters": true,
                "verbatimModuleSyntax": true,
                "isolatedModules": true,
                "skipLibCheck": false,
                "noEmit": true,
                "types": []
              },
              "files": ["{{file}}"]
            }
            """);
        return directory;
    }

    private static string Tsc(string package)
    {
        var tsc = Path.Combine(Repository.Client, "node_modules", package, "bin", "tsc");
        return File.Exists(tsc) ? tsc : throw new InvalidOperationException($"Run 'npm ci' in src/Spar.Client: {tsc} is missing.");
    }

    private static async Task<(int Exit, string Output)> RunAsync(params string[] arguments)
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

    [GeneratedRegex(@"// expect (TS\d+)")]
    private static partial Regex Expectation();

    [GeneratedRegex(@"mistakes\.ts\((\d+),\d+\): error (TS\d+)")]
    private static partial Regex Reported();
}

/// <summary>Source-generated JSON for the probes, as the trim and AOT analyzers require.</summary>
[System.Text.Json.Serialization.JsonSerializable(typeof(string[][]))]
internal sealed partial class ProbeJson : System.Text.Json.Serialization.JsonSerializerContext;
