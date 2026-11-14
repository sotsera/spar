#:property PublishAot=false

// Compare `union` and `closed record` error declarations inside a Result<T, TError> with implicit conversions.
using System;

Console.WriteLine($"union PlanError is value type: {typeof(PlanError).IsValueType}");
Console.WriteLine($"closed SaveError is value type: {typeof(SaveError).IsValueType}");
Console.WriteLine(Describe(Commands.PlanUnion(missing: true)));
Console.WriteLine(Describe(Commands.SaveClosed(empty: true)));

static string Describe<T, E>(Result<T, E> result) => result.IsSuccess ? $"ok {result.Value}" : $"error {result.Error}";

public sealed record Result<T, TError>
{
    private Result(bool ok, T? value, TError? error) { IsSuccess = ok; Value = value; Error = error; }
    public bool IsSuccess { get; }
    public T? Value { get; }
    public TError? Error { get; }
    public static implicit operator Result<T, TError>(T value) => new(true, value, default);
    public static implicit operator Result<T, TError>(TError error) => new(false, default, error);
}

// --- union: independent case types, combined; a case can be shared by several unions ---
public sealed record ConnectionNotFound(string ConnectionId);
public sealed record TableMissing(string Connection, string Table);
public union PlanError(ConnectionNotFound, TableMissing);
public union RunError(ConnectionNotFound, TableMissing);

// --- closed record: one hierarchy, cases derive from the base ---
public closed record SaveError
{
    public sealed record InvalidText(string Reason) : SaveError;
    public sealed record Duplicate(string ExistingId) : SaveError;
}

public static class Commands
{
    public static Result<string, PlanError> PlanUnion(bool missing)
    {
        if (missing)
        {
#if TRY_DIRECT
            return new ConnectionNotFound("src");            // case -> union -> Result: two conversions
#else
            return (PlanError)new ConnectionNotFound("src"); // explicit step through the union
#endif
        }

        return "plan";
    }

    public static Result<string, SaveError> SaveClosed(bool empty)
    {
        if (empty)
        {
            return new SaveError.InvalidText("Enter a note."); // derived -> base is not a user conversion
        }

        return "saved";
    }
}
