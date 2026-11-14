using Sample.Contracts;
using Spar;

namespace Sample.App;

public sealed record PaymentRequest(Money Amount, string Account);

public sealed record Receipt(Guid Id, Money Charged);

public closed record PaymentError
{
    public sealed record InsufficientFunds(Money Missing) : PaymentError;

    [SparName("accountLocked")]
    public sealed record Locked(string Account) : PaymentError;
}

/// <summary>Interface form with an explicit wire name ('billing' instead of 'billingApi').</summary>
[SparContract("billing")]
public interface IBillingApi
{
    Task<Money> ChargeAsync(Money amount, CancellationToken cancellationToken);

    Task<Result<Receipt, PaymentError>> PayAsync(PaymentRequest request, CancellationToken cancellationToken);
}

public sealed class BillingApi : IBillingApi
{
    public Task<Money> ChargeAsync(Money amount, CancellationToken cancellationToken) =>
        Task.FromResult(amount with { Amount = amount.Amount + 1 });

    public Task<Result<Receipt, PaymentError>> PayAsync(PaymentRequest request, CancellationToken cancellationToken)
    {
        Result<Receipt, PaymentError> result = request switch
        {
            { Account: "locked" } => new PaymentError.Locked(request.Account),
            { Amount.Amount: > 100 } => new PaymentError.InsufficientFunds(request.Amount with { Amount = request.Amount.Amount - 100 }),
            _ => new Receipt(Guid.Parse("00000000-0000-0000-0000-000000000001"), request.Amount),
        };
        return Task.FromResult(result);
    }
}

/// <summary>Interface form with a derived name: 'INotesService' becomes 'notes'.</summary>
[SparContract]
public interface INotesService
{
    Task<string> EchoAsync(string text, CancellationToken cancellationToken);
}

public sealed class NotesService : INotesService
{
    public Task<string> EchoAsync(string text, CancellationToken cancellationToken) => Task.FromResult(text);
}
