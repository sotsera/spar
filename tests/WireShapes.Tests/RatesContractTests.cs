using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Spar;
using Spar.Testing;
using WireShapes.Library;

namespace WireShapes.Tests;

/// <summary>
/// An interface contract declared in a listed library and implemented by the app: dispatch resolves the interface from
/// the call's scope, so the app's own registration decides what runs, and an app without one does not start.
/// </summary>
public sealed class RatesContractTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_apps_implementation_answers_the_librarys_interface_contract()
    {
        await using var app = await SparTestApp.StartAsync<Program>(cancellationToken: Token);
        var rates = new RatesClient(app);

        var euro = await rates.RateAsync("EUR", Token);
        var unknown = await rates.RateAsync("XYZ", Token);

        Assert.Equal(100, Rate(euro));
        Assert.Equal("XYZ", Assert.IsType<RateError.Unknown>(Error(unknown)).Currency);
    }

    [Fact]
    public async Task A_test_replaces_the_implementation_like_any_service()
    {
        await using var app = await SparTestApp.StartAsync<Program>(
            o => o.Services(services => services.AddScoped<IRatesService, OneRate>()), Token);

        var rate = await new RatesClient(app).RateAsync("EUR", Token);

        Assert.Equal(1, Rate(rate));
    }

    [Fact]
    public async Task Without_an_implementation_the_app_does_not_start()
    {
        var start = SparTestApp.StartAsync<Program>(
            o => o.Services(services => services.RemoveAll<IRatesService>()), Token);

        var refused = await Assert.ThrowsAsync<InvalidOperationException>(() => start);

        Assert.StartsWith(
            "The contract 'rates' is the interface WireShapes.Library.IRatesService, and the app registers no implementation of it.",
            refused.Message, StringComparison.Ordinal);
    }

    private static long Rate(Result<long, RateError> result) => result switch
    {
        long cents => cents,
        RateError error => throw new InvalidOperationException($"Expected a rate, got {error}."),
    };

    private static RateError Error(Result<long, RateError> result) => result switch
    {
        long cents => throw new InvalidOperationException($"Expected an error, got {cents}."),
        RateError error => error,
    };

    private sealed class OneRate : IRatesService
    {
        public Task<Result<long, RateError>> RateAsync(string currency, CancellationToken cancellationToken) =>
            Task.FromResult<Result<long, RateError>>(1);
    }
}
