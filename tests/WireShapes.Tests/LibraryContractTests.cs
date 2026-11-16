using System.Text;
using Spar;
using Spar.Runtime;
using Spar.Testing;
using WireShapes.Library;

namespace WireShapes.Tests;

/// <summary>
/// A contract declared in a library the app lists in <c>SparContracts</c> (decision "Contracts from referenced
/// libraries: the app opts in"), called at run time: its dispatch, serializers and DI registration are generated in the
/// app, and it runs like the app's own contracts.
/// </summary>
public sealed class LibraryContractTests : IAsyncLifetime
{
    private SparTestApp _app = null!;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _app = await SparTestApp.StartAsync<Program>(cancellationToken: Token);

    public ValueTask DisposeAsync() => _app.DisposeAsync();

    [Fact]
    public async Task The_library_command_runs_with_the_apps_services_and_its_declared_error()
    {
        var ledger = new LedgerClient(_app);

        var first = await ledger.PostAsync(new Entry("cash", 9_007_199_254_740_993), Token);
        var second = await ledger.PostAsync(new Entry("cash", -9_007_199_254_740_994), Token);

        Assert.Equal(new Entry("cash", 9_007_199_254_740_993), Balance(first));
        var overdrawn = Assert.IsType<LedgerError.Overdrawn>(Error(second));
        Assert.Equal(9_007_199_254_740_993, overdrawn.Balance);
    }

    [Fact]
    public async Task Dispatch_fills_the_call_for_a_library_contract()
    {
        var who = await new LedgerClient(_app).WhoAsync(Token);

        Assert.Equal("ledger.who", who);
    }

    [Fact]
    public async Task The_library_contract_follows_the_wire_rules()
    {
        var posted = await _app.InvokeAsync("ledger", "post", Encoding.UTF8.GetBytes("{\"account\":\"a\",\"cents\":\"5\"}"), Token);
        var refused = await _app.InvokeAsync("ledger", "post", Encoding.UTF8.GetBytes("{\"account\":\"a\",\"cents\":\"-6\"}"), Token);
        var asNumber = await _app.InvokeAsync("ledger", "post", Encoding.UTF8.GetBytes("{\"account\":\"a\",\"cents\":5}"), Token);

        Assert.Equal("ok {\"account\":\"a\",\"cents\":\"5\"}", posted.ToString());
        Assert.Equal("error {\"code\":\"overdrawn\",\"balance\":\"5\"}", refused.ToString());
        Assert.Equal(("rejected", "invalid-request"), (asNumber.Failure?.Kind, asNumber.Failure?.Code));
    }

    private static Entry Balance(Result<Entry, LedgerError> result) => result switch
    {
        Entry entry => entry,
        LedgerError error => throw new InvalidOperationException($"Expected a balance, got {error}."),
    };

    private static LedgerError Error(Result<Entry, LedgerError> result) => result switch
    {
        Entry entry => throw new InvalidOperationException($"Expected an error, got {entry}."),
        LedgerError error => error,
    };
}
