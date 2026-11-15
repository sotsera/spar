namespace Spar.Sdk.Tasks.Tests;

/// <summary>
/// How names reach the client: the contract analysis checks them (its diagnostics are tested in Spar.Generator.Tests),
/// and the client step writes nothing when it found a problem, so the compile reports it at its declaration.
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
    public void A_name_problem_is_counted_so_the_client_step_writes_nothing(string attribute, string type)
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

        Assert.True(read.Problems > 0);
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

        Assert.Equal(2, read.Problems);
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
    public void Every_SDK_import_is_a_name_no_type_may_take()
    {
        var orders = ClientHarness.Emit(Fixtures.Shop)["orders.d.ts"];

        var imported = orders.Split('\n')[1];

        Assert.Equal($"import type {{ {string.Join(", ", Client.TypeScriptNames.SdkImports)} }} from \"@spar/app/sdk\";", imported);
    }
}
