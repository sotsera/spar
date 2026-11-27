namespace Spar.Generator.Tests;

/// <summary>What the generator writes, and that it compiles, for every supported shape and both variant kinds.</summary>
public sealed class GeneratorTests
{
    private const string Program = """
        using System.Threading;
        using System.Threading.Tasks;
        using Spar;

        var app = SparApplication.Create(args, _ => { });
        return app.Run();

        public sealed record Item(string Name);

        """;

    [Fact]
    public void Every_supported_shape_generates_code_that_compiles()
    {
        var run = GeneratorHarness.Generate(Program + Fixtures.Catalog);

        Assert.Empty(run.Errors);
        Assert.Equal(["Spar.BuildIdentity.g.cs", "Spar.ContractShape.g.cs", "Spar.Contracts.g.cs", "Spar.Create.g.cs"], run.Files.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Run_is_handed_the_windows_host_the_build_references()
    {
        const string Host = """
            namespace Spar.Windows
            {
                public sealed class SparWindowsHost : Spar.Runtime.ISparHost
                {
                    public int Run(Spar.SparApp app) => 0;
                }
            }
            """;

        var run = GeneratorHarness.Generate(Program + Host);
        var test = GeneratorHarness.Generate(Program + Host, "Test");

        Assert.Empty(run.Errors);
        Assert.Contains("return new global::Spar.Windows.SparWindowsHost();", run.Files["Spar.Run.g.cs"], StringComparison.Ordinal);
        Assert.Contains("IsWindowsVersionAtLeast(10, 0, 17763)", run.Files["Spar.Run.g.cs"], StringComparison.Ordinal);

        // Older Windows cannot load the host: Spar's stand-in reports "OS too old" (exit 7) instead of "no host".
        Assert.Contains("return new global::Spar.Runtime.UnsupportedWindowsHost();", run.Files["Spar.Run.g.cs"], StringComparison.Ordinal);

        // The test variant gets the host too: its test session opens the app's window for the Window surface.
        Assert.Empty(test.Errors);
        Assert.Contains("return new global::Spar.Windows.SparWindowsHost();", test.Files["Spar.Run.g.cs"], StringComparison.Ordinal);
    }

    [Fact]
    public void Run_is_handed_the_linux_host_behind_its_own_check_when_the_build_references_both()
    {
        const string Hosts = """
            namespace Spar.Windows
            {
                public sealed class SparWindowsHost : Spar.Runtime.ISparHost
                {
                    public int Run(Spar.SparApp app) => 0;
                }
            }

            namespace Spar.Linux
            {
                public sealed class SparLinuxHost : Spar.Runtime.ISparHost
                {
                    public int Run(Spar.SparApp app) => 0;
                }
            }
            """;
        const string LinuxOnly = """
            namespace Spar.Linux
            {
                public sealed class SparLinuxHost : Spar.Runtime.ISparHost
                {
                    public int Run(Spar.SparApp app) => 0;
                }
            }
            """;

        var both = GeneratorHarness.Generate(Program + Hosts);
        var linux = GeneratorHarness.Generate(Program + LinuxOnly);

        Assert.Empty(both.Errors);
        var code = both.Files["Spar.Run.g.cs"];
        Assert.Contains("if (global::System.OperatingSystem.IsLinux())", code, StringComparison.Ordinal);
        Assert.Contains("return new global::Spar.Linux.SparLinuxHost();", code, StringComparison.Ordinal);
        Assert.Contains("return new global::Spar.Windows.SparWindowsHost();", code, StringComparison.Ordinal);

        // A build for Linux only keeps no Windows host, nor Windows' "OS too old" stand-in.
        Assert.Empty(linux.Errors);
        Assert.Contains("return new global::Spar.Linux.SparLinuxHost();", linux.Files["Spar.Run.g.cs"], StringComparison.Ordinal);
        Assert.DoesNotContain("Windows", linux.Files["Spar.Run.g.cs"], StringComparison.Ordinal);
    }

    [Fact]
    public void A_development_build_with_the_loopback_host_asks_it_first()
    {
        const string Hosts = """
            namespace Spar.Windows
            {
                public sealed class SparWindowsHost : Spar.Runtime.ISparHost
                {
                    public int Run(Spar.SparApp app) => 0;
                }
            }

            namespace Spar.Loopback
            {
                public sealed class SparLoopbackHost : Spar.Runtime.ISparHost
                {
                    public static Spar.Runtime.ISparHost? Chosen() => null;

                    public int Run(Spar.SparApp app) => 0;
                }
            }
            """;

        var run = GeneratorHarness.Generate(Program + Hosts);

        Assert.Empty(run.Errors);
        var code = run.Files["Spar.Run.g.cs"];
        var loopback = code.IndexOf("if (global::Spar.Loopback.SparLoopbackHost.Chosen() is { } loopback)", StringComparison.Ordinal);
        Assert.InRange(loopback, 0, code.IndexOf("return new global::Spar.Windows.SparWindowsHost();", StringComparison.Ordinal));
    }

    [Fact]
    public void The_app_id_the_build_checked_reaches_the_catalog()
    {
        var withId = GeneratorHarness.Generate(Program + Fixtures.Catalog, appId: "com.example.SqlTool");
        var withoutId = GeneratorHarness.Generate(Program + Fixtures.Catalog);

        Assert.Empty(withId.Errors);
        Assert.Contains("AppId = \"com.example.SqlTool\",", withId.Files["Spar.Contracts.g.cs"], StringComparison.Ordinal);
        Assert.DoesNotContain("AppId =", withoutId.Files["Spar.Contracts.g.cs"], StringComparison.Ordinal);
    }

    [Fact]
    public void Two_contracts_with_one_name_are_left_out_and_the_rest_still_compiles()
    {
        const string Contracts = """
            [SparContract] public sealed class Items { [SparCommand] public Task PingAsync() => Task.CompletedTask; }
            [SparContract] public interface IItemsService { Task PingAsync(); }
            [SparContract] public sealed class Notes { [SparCommand] public Task PingAsync() => Task.CompletedTask; }
            """;

        var run = GeneratorHarness.Generate(Program + Contracts);

        Assert.Empty(run.Errors);
        var contracts = run.Files["Spar.Contracts.g.cs"];
        Assert.Contains("\"notes\"", contracts, StringComparison.Ordinal);
        Assert.DoesNotContain("\"items\"", contracts, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_command_is_in_the_catalog_with_its_wire_name_and_time_limit()
    {
        var contracts = GeneratorHarness.Generate(Program + Fixtures.Catalog).Files["Spar.Contracts.g.cs"];

        string[] expected =
        [
            "new(\"catalog\", \"find\", 0, HandleCatalogFind),",
            "new(\"catalog\", \"remove\", 30, HandleCatalogRemove) { SerialGroup = \"catalog-writes\" },",
            "new(\"catalog\", \"list\", 0, HandleCatalogList),",
            "new(\"catalog\", \"count\", 0, HandleCatalogCount),",
            "new(\"catalog\", \"clear\", 0, HandleCatalogClear),",
            "new(\"catalog\", \"rename\", 0, HandleCatalogRename),",
            "new(\"catalog\", \"rows\", 0, null) { StreamHandler = HandleCatalogRows },",
            "new(\"catalog\", \"scan\", 0, null) { StreamHandler = HandleCatalogScan },",
            "new(\"catalog\", \"watch\", 0, null) { StreamHandler = HandleCatalogWatch, Capacity = 32 },",
            "new(\"catalog\", \"level\", 0, null) { StreamHandler = HandleCatalogLevel, Capacity = 1, LatestOnly = true },",
            "new(\"ioStats\", \"read\", 0, HandleIoStatsRead),",
        ];
        Assert.All(expected, entry => Assert.Contains(entry, contracts, StringComparison.Ordinal));
        Assert.DoesNotContain("notACommand", contracts, StringComparison.Ordinal);
    }

    [Fact]
    public void Only_class_contracts_are_registered_by_the_generated_code_interfaces_are_listed_for_the_start_and_every_contract_type_is_named()
    {
        var contracts = GeneratorHarness.Generate(Program + Fixtures.Catalog).Files["Spar.Contracts.g.cs"];

        Assert.Contains("AddScoped<global::CatalogService>(services);", contracts, StringComparison.Ordinal);
        Assert.DoesNotContain("AddScoped<global::IIOStatsService>", contracts, StringComparison.Ordinal);
        Assert.Contains("Interfaces =\n            [\n                new(\"ioStats\", \"IIOStatsService\", typeof(global::IIOStatsService)),\n            ],\n", contracts, StringComparison.Ordinal);
        Assert.DoesNotContain("new(\"catalog\", \"CatalogService\"", contracts, StringComparison.Ordinal);
        Assert.Contains("new(typeof(global::CatalogService), \"catalog\"),", contracts, StringComparison.Ordinal);
        Assert.Contains("new(typeof(global::IIOStatsService), \"ioStats\"),", contracts, StringComparison.Ordinal);
    }

    [Fact]
    public void Error_codes_are_camel_case_or_their_SparName()
    {
        var contracts = GeneratorHarness.Generate(Program + Fixtures.Catalog).Files["Spar.Contracts.g.cs"];

        Assert.Contains("writer.WriteString(\"code\"u8, \"missing\"u8);", contracts, StringComparison.Ordinal);
        Assert.Contains("writer.WriteString(\"code\"u8, \"locked\"u8);", contracts, StringComparison.Ordinal);
        Assert.Contains("writer.WriteString(\"code\"u8, \"tooBig\"u8);", contracts, StringComparison.Ordinal);
    }

    [Fact]
    public void The_test_entry_point_and_clients_exist_only_in_the_Test_variant()
    {
        var development = GeneratorHarness.Generate(Program + Fixtures.Catalog, "Development");
        var production = GeneratorHarness.Generate(Program + Fixtures.Catalog, "Production");
        var test = GeneratorHarness.Generate(Program + Fixtures.Catalog, "Test");

        Assert.DoesNotContain("Spar.TestEntryPoint.g.cs", development.Files.Keys);
        Assert.DoesNotContain("Spar.TestEntryPoint.g.cs", production.Files.Keys);
        Assert.Empty(test.Errors);
        Assert.Contains("public partial class Program : global::Spar.ISparEntryPoint", test.Files["Spar.TestEntryPoint.g.cs"], StringComparison.Ordinal);
        Assert.Contains("public sealed class CatalogClient(", test.Files["Spar.TestClients.g.cs"], StringComparison.Ordinal);
        Assert.Contains("public sealed class IoStatsClient(", test.Files["Spar.TestClients.g.cs"], StringComparison.Ordinal);
    }

    [Fact]
    public void An_invalid_command_is_left_out_and_its_contract_still_works()
    {
        const string contract = """
            [SparContract]
            public sealed class Mixed
            {
                [SparCommand]
                public Task<Item> GoodAsync(Item request) => Task.FromResult(request);

                [SparCommand]
                public Item Bad(Item request) => request;
            }
            """;

        var contracts = GeneratorHarness.Generate(Program + contract).Files["Spar.Contracts.g.cs"];

        Assert.Contains("new(\"mixed\", \"good\", 0, HandleMixedGood),", contracts, StringComparison.Ordinal);
        Assert.DoesNotContain("\"bad\"", contracts, StringComparison.Ordinal);
    }

    [Fact]
    public void Generated_files_use_only_LF_line_endings()
    {
        var run = GeneratorHarness.Generate(Program + Fixtures.Catalog, "Test");

        Assert.All(run.Files.Values, text => Assert.DoesNotContain('\r', text));
    }


    [Fact]
    public void A_settings_record_gets_its_serializer_and_a_catalog_line()
    {
        const string source = """
            using Spar;

            var app = SparApplication.Create(args, spar => spar.Settings(new Prefs("System", 50000, null)));
            return app.Run();

            public sealed record Prefs(string Theme, int BatchSize, Layout? Layout);

            public sealed record Layout(int Width, int Height);

            """;

        var run = GeneratorHarness.Generate(source);

        Assert.Empty(run.Errors);
        var contracts = run.Files["Spar.Contracts.g.cs"];
        Assert.Contains("new global::Spar.Runtime.SparSettingsSerializer<global::Prefs>(SparSerializers.ReadRecord_Prefs, SparSerializers.WriteRecord_Prefs),", contracts, StringComparison.Ordinal);
        Assert.Contains("public static global::Layout ReadRecord_Layout(", contracts, StringComparison.Ordinal);
    }

    [Fact]
    public void A_blob_result_is_written_in_a_blob_scope_and_a_resource_hands_its_blob_to_the_host()
    {
        const string source = """
            using System.Threading.Tasks;
            using Spar;

            var app = SparApplication.Create(args, _ => { });
            return app.Run();

            public sealed record Export(string Title, SparBlob File, SparBlob? Preview);

            public sealed record PhotoKey(int Album, int Number);

            [SparContract]
            public sealed class FilesService
            {
                [SparCommand]
                public Task<Export> ExportAsync(string title) => null!;

                [SparCommand]
                public Task<string> NameAsync() => null!;

                [SparCommand, SparResource]
                public Task<SparBlob?> PhotoAsync(PhotoKey request) => null!;
            }

            """;

        var run = GeneratorHarness.Generate(source);

        Assert.Empty(run.Errors);
        var contracts = run.Files["Spar.Contracts.g.cs"];
        Assert.Contains("new(\"files\", \"photo\", 0, HandleFilesPhoto) { IsResource = true },", contracts, StringComparison.Ordinal);
        Assert.Contains("using var blobs = global::Spar.Runtime.SparBlobWrite.Begin(call);", Handler(contracts, "HandleFilesExport"), StringComparison.Ordinal);
        Assert.DoesNotContain("SparBlobWrite", Handler(contracts, "HandleFilesName"), StringComparison.Ordinal);
        Assert.Contains("call.Resource = result;", Handler(contracts, "HandleFilesPhoto"), StringComparison.Ordinal);
        Assert.Contains("global::Spar.Runtime.SparBlobWrite.Write(writer, value);", contracts, StringComparison.Ordinal);
    }

    [Fact]
    public void A_request_with_grants_is_read_in_a_grant_scope_and_grants_are_written_both_ways()
    {
        const string source = """
            using System.Collections.Generic;
            using System.Threading.Tasks;
            using Spar;

            var app = SparApplication.Create(args, _ => { });
            return app.Run();

            public sealed record ImportRequest(FileGrant File, IReadOnlyList<FolderGrant> Folders, string Note);

            public sealed record Picked(FileGrant File, FolderGrant? Folder);

            [SparContract]
            public sealed class ImportsService
            {
                [SparCommand]
                public Task<int> ImportAsync(ImportRequest request) => null!;

                [SparCommand]
                public Task<long> SizeAsync(FileGrant file) => null!;

                [SparCommand]
                public Task<Picked> PickAsync(string title) => null!;
            }

            """;

        var run = GeneratorHarness.Generate(source);

        Assert.Empty(run.Errors);
        var contracts = run.Files["Spar.Contracts.g.cs"];
        Assert.Contains("using var grants = global::Spar.Runtime.SparGrants.Begin(call);", Handler(contracts, "HandleImportsImport"), StringComparison.Ordinal);
        Assert.Contains("using var grants = global::Spar.Runtime.SparGrants.Begin(call);", Handler(contracts, "HandleImportsSize"), StringComparison.Ordinal);
        Assert.DoesNotContain("SparGrants.Begin", Handler(contracts, "HandleImportsPick"), StringComparison.Ordinal);
        Assert.Contains("global::Spar.Runtime.SparGrants.ReadFile(ref reader)", contracts, StringComparison.Ordinal);
        Assert.Contains("global::Spar.Runtime.SparGrants.ReadFolder(ref reader)", contracts, StringComparison.Ordinal);
        Assert.Contains("global::Spar.Runtime.SparGrants.Write(writer, value);", contracts, StringComparison.Ordinal);
    }

    [Fact]
    public void The_fingerprint_changes_with_the_wire_shape_and_only_with_it()
    {
        var original = Fingerprint(Program + Fixtures.Catalog);
        var reformatted = Fingerprint(Program + Fixtures.Catalog.Replace("public Task ClearAsync() => Task.CompletedTask;", "public Task ClearAsync()\n{\n    return Task.CompletedTask;\n}", StringComparison.Ordinal));
        var changed = Fingerprint(Program + Fixtures.Catalog.Replace("int Count,", "long Count,", StringComparison.Ordinal));
        var resumes = Fingerprint(Program + Fixtures.Catalog.Replace("LatestOnly = true, Resume = SubscriptionResume.Manual", "LatestOnly = true", StringComparison.Ordinal));
        var bigger = Fingerprint(Program + Fixtures.Catalog.Replace("Capacity = 32", "Capacity = 64", StringComparison.Ordinal));

        Assert.Equal(original, reformatted);
        Assert.NotEqual(original, changed);

        // The page depends on a subscription's resume and events, not on the size of the backend's buffer.
        Assert.NotEqual(original, resumes);
        Assert.Equal(original, bigger);
    }

    /// <summary>One generated handler's text, from its signature to the next handler or the end.</summary>
    private static string Handler(string contracts, string name)
    {
        var start = contracts.IndexOf($" {name}(global::Spar.Runtime.SparCallContext", StringComparison.Ordinal);
        Assert.True(start >= 0, name);
        var end = contracts.IndexOf("private static async", start + 1, StringComparison.Ordinal);
        return end < 0 ? contracts[start..] : contracts[start..end];
    }

    private static string Fingerprint(string source)
    {
        var contracts = GeneratorHarness.Generate(source).Files["Spar.Contracts.g.cs"];
        var start = contracts.IndexOf("Catalog { get; } = new(\n            \"", StringComparison.Ordinal);
        Assert.True(start >= 0, "no catalog");
        return contracts.Substring(start + "Catalog { get; } = new(\n            \"".Length, 16);
    }
}
