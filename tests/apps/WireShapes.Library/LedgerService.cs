using Spar;

namespace WireShapes.Library;

/// <summary>A posting to an account, in cents.</summary>
public sealed record Entry(string Account, long Cents);

/// <summary>Why a posting was refused.</summary>
public closed record LedgerError
{
    /// <summary>The posting would take the account below zero.</summary>
    public sealed record Overdrawn(long Balance) : LedgerError;
}

/// <summary>Keeps balances for the app's lifetime; the app registers it, and the contract gets it from DI.</summary>
public sealed class Ledger
{
    private readonly Dictionary<string, long> _balances = new(StringComparer.Ordinal);
    private readonly Lock _lock = new();

    /// <summary>Adds the entry unless the balance would go below zero; either way, returns the balance after.</summary>
    public bool TryPost(Entry entry, out long balance)
    {
        lock (_lock)
        {
            var current = _balances.GetValueOrDefault(entry.Account);
            if (current + entry.Cents < 0)
            {
                balance = current;
                return false;
            }

            balance = current + entry.Cents;
            _balances[entry.Account] = balance;
            return true;
        }
    }
}

/// <summary>A contract declared in a library, created per call from the call's scope like the app's own.</summary>
[SparContract]
public sealed class LedgerService(Ledger ledger, ISparCall call)
{
    /// <summary>Posts an entry and returns the account with its new balance, or why it was refused.</summary>
    [SparCommand]
    public Task<Result<Entry, LedgerError>> PostAsync(Entry request)
    {
        if (!ledger.TryPost(request, out var balance))
        {
            return Task.FromResult<Result<Entry, LedgerError>>(new LedgerError.Overdrawn(balance));
        }

        return Task.FromResult<Result<Entry, LedgerError>>(request with { Cents = balance });
    }

    /// <summary>The contract and command the call arrived with, as dispatch filled them in.</summary>
    [SparCommand]
    public Task<string> WhoAsync() => Task.FromResult($"{call.Contract}.{call.Command}");
}
