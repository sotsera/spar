using System.Diagnostics.CodeAnalysis;

namespace Spar;

/// <summary>
/// The outcome of a command with expected errors: a value or one case of <typeparamref name="TError"/>, a
/// <c>closed record</c>. Both convert implicitly, so a command returns either directly.
/// </summary>
public readonly struct Result<T, TError>
    where TError : class
{
    private readonly T _value;
    private readonly TError? _error;

    private Result(T value, TError? error)
    {
        _value = value;
        _error = error;
    }

    [MemberNotNullWhen(true, nameof(Error))]
    public bool IsError => _error is not null;

    public T Value => _error is null ? _value : throw new InvalidOperationException("The result is an error.");

    public TError? Error => _error;

    public static implicit operator Result<T, TError>(T value) => new(value, null);

    public static implicit operator Result<T, TError>(TError error) =>
        new(default!, error ?? throw new ArgumentNullException(nameof(error)));

    public static Result<T, TError> Success(T value) => new(value, null);

    public static Result<T, TError> Failure(TError error) =>
        new(default!, error ?? throw new ArgumentNullException(nameof(error)));

    public TResult Match<TResult>(Func<T, TResult> success, Func<TError, TResult> failure) =>
        _error is null ? success(_value) : failure(_error);
}
