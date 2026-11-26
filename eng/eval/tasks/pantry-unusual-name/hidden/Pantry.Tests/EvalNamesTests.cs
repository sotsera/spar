using Pantry.Greetings;
using Spar.Testing;

namespace Pantry.Tests;

/// <summary>The evaluation's hidden check: names in any script are greeted.</summary>
public sealed class EvalNamesTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("José")]
    [InlineData("Zoë")]
    [InlineData("Łukasz")]
    [InlineData("Ada")]
    public async Task A_name_is_greeted(string name)
    {
        await using var app = await SparTestApp.StartAsync<Program>(cancellationToken: Token);

        var result = await new GreetingClient(app).GreetAsync(name, Token);

        Assert.Equal(new Greeting($"Hello, {name}!"), result.Value);
    }

    [Fact]
    public async Task An_empty_name_is_still_a_declared_error()
    {
        await using var app = await SparTestApp.StartAsync<Program>(cancellationToken: Token);

        var result = await new GreetingClient(app).GreetAsync("  ", Token);

        Assert.IsType<GreetError.NameMissing>(result.Error);
    }
}
