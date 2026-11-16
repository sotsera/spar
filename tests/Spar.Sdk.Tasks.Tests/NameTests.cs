namespace Spar.Sdk.Tasks.Tests;

/// <summary>
/// How names reach the client: the contract analysis checks them (its diagnostics are tested in Spar.Generator.Tests),
/// and the client step reports the same diagnostics, at the same places, and writes nothing.
/// </summary>
public sealed class NameTests
{
    [Fact]
    public void A_type_named_like_another_contracts_shared_type_is_fine_when_the_contract_does_not_import_it()
    {
        const string Source = """
            using System.Threading.Tasks;
            using Spar;

            namespace A { public sealed record Note(string Text); }
            namespace B { public sealed record Note(int Id); }

            [SparContract]
            public sealed class DraftsService
            {
                [SparCommand]
                public Task<A.Note> ReadAsync(string id) => throw new System.NotImplementedException();
            }

            [SparContract]
            public sealed class ArchiveService
            {
                [SparCommand]
                public Task<A.Note> ReadAsync(string id) => throw new System.NotImplementedException();
            }

            [SparContract]
            public sealed class LegacyService
            {
                [SparCommand]
                public Task<B.Note> ReadAsync(string id) => throw new System.NotImplementedException();
            }
            """;

        var layout = ClientHarness.Plan(Source);

        Assert.Equal(["global::A.Note"], layout.Shared.Select(t => t.CSharp));
    }

    [Theory]
    [InlineData("[SparContract(\"sdk\")]", "Item")]
    [InlineData("[SparContract(\"readItem\")]", "Item")]
    [InlineData("[SparContract]", "any")]
    [InlineData("[SparContract]", "Call")]
    public void A_name_problem_is_the_client_steps_error_too(string attribute, string type)
    {
        var source = $$"""
            using System.Threading.Tasks;
            using Spar;

            public sealed record {{type}}(string Name);

            {{attribute}}
            public sealed class ItemsService
            {
                [SparCommand]
                public Task<{{type}}> GetAsync(string id) => throw new System.NotImplementedException();
            }
            """;
        Assert.Empty(ClientHarness.Errors(source));

        var read = ClientHarness.Read(source);

        Assert.Equal(["SPAR004"], read.Problems.Select(p => p.Id));
    }

    [Fact]
    public void Two_shared_types_with_one_name_leave_both_contracts_out()
    {
        const string Source = """
            using System.Threading.Tasks;
            using Spar;

            namespace A { public sealed record Note(string Text); }
            namespace B { public sealed record Note(int Id); }

            [SparContract]
            public sealed class FirstService
            {
                [SparCommand]
                public Task<A.Note> ReadAsync(string id) => throw new System.NotImplementedException();
            }

            [SparContract]
            public sealed class SecondService
            {
                [SparCommand]
                public Task<A.Note> ReadAsync(string id) => throw new System.NotImplementedException();
            }

            [SparContract]
            public sealed class ThirdService
            {
                [SparCommand]
                public Task<B.Note> ReadAsync(string id) => throw new System.NotImplementedException();
            }

            [SparContract]
            public sealed class FourthService
            {
                [SparCommand]
                public Task<B.Note> ReadAsync(string id) => throw new System.NotImplementedException();
            }
            """;

        var read = ClientHarness.Read(Source);

        Assert.Equal(["SPAR040", "SPAR040"], read.Problems.Select(p => p.Id));
        Assert.Empty(read.Contracts);
    }

    [Fact]
    public void SparName_on_a_type_is_its_TypeScript_name()
    {
        const string Source = """
            using System.Threading.Tasks;
            using Spar;

            namespace A { [SparName("DraftNote")] public sealed record Note(string Text); }
            namespace B { public sealed record Note(int Id); }

            [SparContract]
            public sealed class NotesService
            {
                [SparCommand]
                public Task<A.Note> ReadAsync(B.Note request) => throw new System.NotImplementedException();
            }
            """;

        var files = ClientHarness.Emit(Source);

        Assert.Contains("export interface DraftNote {", files["notes.d.ts"], StringComparison.Ordinal);
        Assert.Contains("export interface Note {", files["notes.d.ts"], StringComparison.Ordinal);
        Assert.Contains("function readDraftNote(value) {", files["notes.js"], StringComparison.Ordinal);
    }

    [Fact]
    public void A_listed_librarys_contract_gets_its_module_and_shares_its_types()
    {
        var library = ClientHarness.CompileLibrary("Lib", """
            using System.Threading.Tasks;
            using Spar;

            namespace Lib;

            public sealed record Job(string Name);

            [SparContract]
            public sealed class JobsService
            {
                [SparCommand]
                public Task<Job> GetAsync(string id) => Task.FromResult(new Job(id));
            }
            """);
        const string Source = """
            using System.Threading.Tasks;
            using Spar;

            [SparContract]
            public sealed class QueueService
            {
                [SparCommand]
                public Task<Lib.Job> NextAsync() => throw new System.NotImplementedException();
            }
            """;

        var files = ClientHarness.Emit(Source, library);

        Assert.StartsWith("// Generated by Spar from Lib (a referenced library). Do not edit.\n", files["jobs.js"], StringComparison.Ordinal);
        Assert.Contains("export interface Job {", files["shared.d.ts"], StringComparison.Ordinal);
        Assert.Contains("import { readJob, writeJob } from \"./shared.js\";", files["jobs.js"], StringComparison.Ordinal);
        Assert.Contains("import { readJob, writeJob } from \"./shared.js\";", files["queue.js"], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_listed_librarys_comments_come_from_the_documentation_file_beside_it(bool documented)
    {
        const string Library = """
            using System.Threading.Tasks;
            using Spar;

            namespace Lib;

            /// <summary>A job in the queue.</summary>
            /// <param name="Name">What the job is called.</param>
            public sealed record Job(string Name);

            /// <summary>Hands out jobs.</summary>
            [SparContract]
            public sealed class JobsService
            {
                /// <summary>The job with this <c>id</c>.</summary>
                [SparCommand]
                public Task<Job> GetAsync(string id) => Task.FromResult(new Job(id));
            }
            """;
        var folder = Repository.Scratch("library-docs");
        try
        {
            var library = ClientHarness.WriteLibrary("Lib", Library, folder, documented);

            var jobs = ClientHarness.Emit("", library)["jobs.d.ts"];

            string[] comments =
            [
                "/** A job in the queue. */\nexport interface Job {\n  /** What the job is called. */\n  readonly name: string;",
                "/** Hands out jobs. Wire name: jobs. */",
                "  /** The job with this `id`. */\n  readonly get:",
            ];
            Assert.All(comments, comment => Assert.Equal(documented, jobs.Contains(comment, StringComparison.Ordinal)));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void Every_SDK_import_is_a_name_no_type_may_take()
    {
        var orders = ClientHarness.Emit(Fixtures.Shop)["orders.d.ts"];

        var imported = orders.Split('\n')[1];

        Assert.Equal($"import type {{ {string.Join(", ", Client.TypeScriptNames.SdkImports)} }} from \"@spar/app/sdk\";", imported);
    }
}
