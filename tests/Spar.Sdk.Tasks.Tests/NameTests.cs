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

    [Theory]
    [InlineData("sdk")]
    [InlineData("shared")]
    [InlineData("spar")]
    [InlineData("delete")]
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
    public void A_type_named_like_an_SDK_type_is_SPAR040()
    {
        const string Source = """
            using System.Threading.Tasks;
            using Spar;

            public sealed record Call(string Number);

            [SparContract]
            public sealed class PhoneService
            {
                [SparCommand]
                public Task<Call> DialAsync(string number) => throw new System.NotImplementedException();
            }
            """;

        Assert.Empty(ClientHarness.Errors(Source));
        var layout = ClientHarness.Plan(Source);

        var contract = Assert.Single(layout.Contracts);
        Assert.Equal(["global::Call"], contract.Model.Records.Select(r => r.CSharp));
        var problem = Assert.Single(layout.Problems);
        Assert.Equal("SPAR040", problem.Code);
    }
}
