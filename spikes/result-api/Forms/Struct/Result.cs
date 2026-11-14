using System.Diagnostics.CodeAnalysis;

namespace Spar;

/// <summary>
/// Struct form with user-defined implicit conversions. <c>default</c> is a distinct invalid state: every member except
/// <see cref="ToString"/> throws on it.
/// </summary>
public readonly struct Result<T, TError>
    where TError : class
{
    private const byte Uninitialized = 0;
    private const byte SuccessState = 1;
    private const byte ErrorState = 2;

    private readonly T _value;
    private readonly TError? _error;
    private readonly byte _state;

    public bool IsSuccess => CheckedState() == SuccessState;

    public bool IsError => CheckedState() == ErrorState;

    public T Value => CheckedState() == SuccessState
        ? _value
        : throw new InvalidOperationException($"The result is an error, not a value: {_error}.");

    public TError Error => CheckedState() == ErrorState
        ? _error!
        : throw new InvalidOperationException($"The result is a value, not an error: {_value}.");

    private Result(T value)
    {
        _value = value;
        _error = null;
        _state = SuccessState;
    }

    private Result(TError error)
    {
        ArgumentNullException.ThrowIfNull(error);

        _value = default!;
        _error = error;
        _state = ErrorState;
    }

    public static implicit operator Result<T, TError>(T value) => new(value);

    public static implicit operator Result<T, TError>(TError error) => new(error);

    public bool TryGetValue([MaybeNullWhen(false)] out T value)
    {
        value = _value;
        return CheckedState() == SuccessState;
    }

    public bool TryGetError([NotNullWhen(true)] out TError? error)
    {
        error = _error;
        return CheckedState() == ErrorState;
    }

    public TResult Match<TResult>(Func<T, TResult> success, Func<TError, TResult> error)
    {
        ArgumentNullException.ThrowIfNull(success);
        ArgumentNullException.ThrowIfNull(error);

        return CheckedState() == SuccessState ? success(_value) : error(_error!);
    }

    public void Switch(Action<T> success, Action<TError> error)
    {
        ArgumentNullException.ThrowIfNull(success);
        ArgumentNullException.ThrowIfNull(error);

        if (CheckedState() == SuccessState)
        {
            success(_value);
            return;
        }

        error(_error!);
    }

    public override string ToString() => _state switch
    {
        SuccessState => $"Success({_value})",
        ErrorState => $"Error({_error})",
        _ => "default(Result): neither a value nor an error",
    };

    private byte CheckedState() => _state != Uninitialized
        ? _state
        : throw new InvalidOperationException(
            "default(Result) is neither a value nor an error. Return a value or an error case.");
}

public readonly struct Result<TError>
    where TError : class
{
    private const byte Uninitialized = 0;
    private const byte SuccessState = 1;
    private const byte ErrorState = 2;

    private readonly TError? _error;
    private readonly byte _state;

    public bool IsSuccess => CheckedState() == SuccessState;

    public bool IsError => CheckedState() == ErrorState;

    public TError Error => CheckedState() == ErrorState
        ? _error!
        : throw new InvalidOperationException("The result is a success, not an error.");

    private Result(TError? error)
    {
        _error = error;
        _state = error is null ? SuccessState : ErrorState;
    }

    public static implicit operator Result<TError>(Success success) => new(null);

    public static implicit operator Result<TError>(TError error) =>
        new(error ?? throw new ArgumentNullException(nameof(error)));

    public bool TryGetError([NotNullWhen(true)] out TError? error)
    {
        error = _error;
        return CheckedState() == ErrorState;
    }

    public override string ToString() => _state switch
    {
        SuccessState => "Success",
        ErrorState => $"Error({_error})",
        _ => "default(Result): neither a success nor an error",
    };

    private byte CheckedState() => _state != Uninitialized
        ? _state
        : throw new InvalidOperationException(
            "default(Result) is neither a success nor an error. Return Result.Success or an error case.");
}

/// <summary>The success of a command that returns no value.</summary>
public readonly record struct Success;

public static class Result
{
    /// <summary>Lets a command with no value write <c>return Result.Success;</c>.</summary>
    public static Success Success => default;
}
