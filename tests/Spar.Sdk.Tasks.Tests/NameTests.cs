namespace Spar.Sdk.Tasks.Tests;

/// <summary>Names the page cannot use stop the client step with the diagnostic of plan section 2.</summary>
public sealed class NameTests
{
    [Fact]
    public void Two_types_with_one_TypeScript_name_in_a_contract_are_SPAR040()
    {
        const string Source = """
            using System.Threading.Tasks;
            using Spar;

            namespace A { public sealed record Note(string Text); }
            namespace B { public sealed record Note(int Id); }

            [SparContract]
            public sealed class NotesService
            {
                [SparCommand]
                public Task<A.Note> ReadAsync(B.Note request) => throw new System.NotImplementedException();
            }
            """;

        var problems = ClientHarness.Plan(Source).Problems;

        var problem = Assert.Single(problems);
        Assert.Equal("SPAR040", problem.Code);
        Assert.Equal("Contract 'notes' uses two types named 'Note' in TypeScript: A.Note and B.Note; rename one", problem.Message);
    }

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

        Assert.Empty(layout.Problems);
        Assert.Equal(["global::A.Note"], layout.Shared.Select(t => t.CSharp));
    }

    [Fact]
    public void Two_shared_types_with_one_name_are_SPAR040()
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
                public Task<A.Note> ReadAsync(B.Note request) => throw new System.NotImplementedException();
            }

            [SparContract]
            public sealed class SecondService
            {
                [SparCommand]
                public Task<A.Note> ReadAsync(B.Note request) => throw new System.NotImplementedException();
            }
            """;

        var problems = ClientHarness.Plan(Source).Problems.Select(p => p.Message);

        Assert.Contains("The contracts share two types named 'Note' in TypeScript: A.Note and B.Note; rename one", problems);
    }

    [Theory]
    [InlineData("sdk")]
    [InlineData("shared")]
    [InlineData("spar")]
    [InlineData("delete")]
    [InlineData("wire")]
    [InlineData("invoke")]
    [InlineData("noErrors")]
    [InlineData("pingDecoder")]
    public void A_reserved_contract_name_is_SPAR003(string name)
    {
        var source = $$"""
            using System.Threading.Tasks;
            using Spar;

            [SparContract("{{name}}")]
            public sealed class SomeService
            {
                [SparCommand]
                public Task PingAsync() => Task.CompletedTask;
            }
            """;

        var problem = Assert.Single(ClientHarness.Plan(source).Problems);

        Assert.Equal(("SPAR003", "Contracts.cs"), (problem.Code, problem.SourceFile));
    }

    [Fact]
    public void A_contract_named_like_one_of_its_readers_is_SPAR003()
    {
        const string Source = """
            using System.Threading.Tasks;
            using Spar;

            public sealed record Item(string Name);

            [SparContract("readItem")]
            public sealed class ItemsService
            {
                [SparCommand]
                public Task<Item> GetAsync(string id) => throw new System.NotImplementedException();
            }
            """;

        var problem = Assert.Single(ClientHarness.Plan(Source).Problems);

        Assert.Equal("Contract name 'readItem' is a name its generated module uses itself; give the contract another name with [SparContract(\"…\")]", problem.Message);
    }

    [Theory]
    [InlineData("any", "any")]
    [InlineData("@string", "string")]
    [InlineData("@default", "default")]
    [InlineData("undefined", "undefined")]
    public void A_type_named_with_a_reserved_word_is_SPAR003_at_its_declaration(string csharp, string typeScript)
    {
        var source = $$"""
            using System.Threading.Tasks;
            using Spar;

            public sealed record {{csharp}}(string Name);

            [SparContract]
            public sealed class WordsService
            {
                [SparCommand]
                public Task<{{csharp}}> GetAsync(string id) => throw new System.NotImplementedException();
            }
            """;
        Assert.Empty(ClientHarness.Errors(source));

        var problem = Assert.Single(ClientHarness.Plan(source).Problems);

        Assert.Equal(("SPAR003", "Contracts.cs", 4), (problem.Code, problem.SourceFile, problem.Line));
        Assert.Equal($"Type '{csharp}' is named '{typeScript}', a reserved word in TypeScript; rename it", problem.Message);
    }

    [Theory]
    [InlineData("Call")]
    [InlineData("CallOptions")]
    public void A_type_named_like_an_SDK_import_is_SPAR040_at_its_declaration(string name)
    {
        var source = $$"""
            using System.Threading.Tasks;
            using Spar;

            public sealed record {{name}}(string Number);

            [SparContract]
            public sealed class PhoneService
            {
                [SparCommand]
                public Task<{{name}}> DialAsync(string number) => throw new System.NotImplementedException();
            }
            """;
        Assert.Empty(ClientHarness.Errors(source));

        var problem = Assert.Single(ClientHarness.Plan(source).Problems);

        Assert.Equal(("SPAR040", 4), (problem.Code, problem.Line));
        Assert.Equal($"Type '{name}' has the name of the Spar SDK type '{name}', which the generated modules import; rename it", problem.Message);
    }

    [Fact]
    public void Every_SDK_import_is_a_name_no_type_may_take()
    {
        var orders = ClientHarness.Emit(Fixtures.Shop)["orders.d.ts"];

        var imported = orders.Split('\n')[1];

        Assert.Equal($"import type {{ {string.Join(", ", Client.TypeScriptNames.SdkImports)} }} from \"@spar/app/sdk\";", imported);
    }
}
