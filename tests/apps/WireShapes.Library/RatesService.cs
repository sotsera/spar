using Spar;

namespace WireShapes.Library;

/// <summary>Why a rate could not be given.</summary>
public closed record RateError
{
    /// <summary>The app has no rate for this currency.</summary>
    public sealed record Unknown(string Currency) : RateError;
}

/// <summary>
/// Exchange rates for the ledger. The library declares the contract and the app implements it: an interface contract's
/// implementation comes from the app's own DI registration.
/// </summary>
[SparContract]
public interface IRatesService
{
    /// <summary>How many cents one unit of <paramref name="currency"/> is worth, or why there is no rate.</summary>
    /// <param name="currency">An ISO 4217 code, such as EUR.</param>
    /// <param name="cancellationToken">Ends the wait when the page aborts the call.</param>
    Task<Result<long, RateError>> RateAsync(string currency, CancellationToken cancellationToken);
}
