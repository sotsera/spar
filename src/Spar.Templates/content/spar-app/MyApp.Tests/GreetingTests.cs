using MyApp.Greetings;
using Spar.Testing;

namespace MyApp.Tests;

/// <summary>Command tests: the app's own Program.cs in this process, the command through the generated typed client.</summary>
public sealed class GreetingTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_name_is_greeted_without_its_spaces()
    {
        await using var app = await SparTestApp.StartAsync<Program>(cancellationToken: Token);

        var result = await new GreetingClient(app).GreetAsync("  Ada ", Token);

        Assert.Equal(new Greeting("Hello, Ada!"), result.Value);
    }

    [Fact]
    public async Task An_empty_name_is_a_declared_error()
    {
        await using var app = await SparTestApp.StartAsync<Program>(cancellationToken: Token);

        var result = await new GreetingClient(app).GreetAsync(" ", Token);

        Assert.IsType<GreetError.NameMissing>(result.Error);
    }
}
