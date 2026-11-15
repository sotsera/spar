namespace Spar;

/// <summary>Stand-in for Spar's <c>Result</c>: a value or a declared error.</summary>
public readonly record struct Result<T, TError>
{
    private Result(T? value, TError? error, bool isSuccess)
    {
        Value = value;
        Error = error;
        IsSuccess = isSuccess;
    }

    public T? Value { get; }

    public TError? Error { get; }

    public bool IsSuccess { get; }

    public static implicit operator Result<T, TError>(T value) => new(value, default, true);

    public static implicit operator Result<T, TError>(TError error) => new(default, error, false);
}
