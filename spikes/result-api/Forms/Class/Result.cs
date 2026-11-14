using System.Diagnostics.CodeAnalysis;

namespace Spar;

/// <summary>
/// Class form with user-defined implicit conversions. There is no default instance: an unset result is
/// <see langword="null"/>, which nullable analysis reports.
/// </summary>
public sealed class Result<T, TError>
    where TError : class
{
    private readonly T _value;
    private readonly TError? _error;

    public bool IsSuccess => _error is null;

    public bool IsError => _error is not null;

    public T Value => _error is null
        ? _value
        : throw new InvalidOperationException($"The result is an error, not a value: {_error}.");

    public TError Error => _error
        ?? throw new InvalidOperationException($"The result is a value, not an error: {_value}.");

    private Result(T value, TError? error)
    {
        _value = value;
        _error = error;
    }

    public static implicit operator Result<T, TError>(T value) => new(value, null);

    public static implicit operator Result<T, TError>(TError error) =>
        new(default!, error ?? throw new ArgumentNullException(nameof(error)));

    public bool TryGetValue([MaybeNullWhen(false)] out T value)
    {
        value = _value;
        return _error is null;
    }

    public bool TryGetError([NotNullWhen(true)] out TError? error)
    {
        error = _error;
        return error is not null;
    }

    public TResult Match<TResult>(Func<T, TResult> success, Func<TError, TResult> error)
    {
        ArgumentNullException.ThrowIfNull(success);
        ArgumentNullException.ThrowIfNull(error);

        return _error is null ? success(_value) : error(_error);
    }

    public void Switch(Action<T> success, Action<TError> error)
    {
        ArgumentNullException.ThrowIfNull(success);
        ArgumentNullException.ThrowIfNull(error);

        if (_error is null)
        {
            success(_value);
            return;
        }

        error(_error);
    }

    public override string ToString() => _error is null ? $"Success({_value})" : $"Error({_error})";
}

public sealed class Result<TError>
    where TError : class
{
    // A success carries nothing, so one instance per error type serves every call.
    private static readonly Result<TError> s_success = new(null);

    private readonly TError? _error;

    public bool IsSuccess => _error is null;

    public bool IsError => _error is not null;

    public TError Error => _error ?? throw new InvalidOperationException("The result is a success, not an error.");

    private Result(TError? error)
    {
        _error = error;
    }

    public static implicit operator Result<TError>(Success success) => s_success;

    public static implicit operator Result<TError>(TError error) =>
        new(error ?? throw new ArgumentNullException(nameof(error)));

    public bool TryGetError([NotNullWhen(true)] out TError? error)
    {
        error = _error;
        return error is not null;
    }

    public override string ToString() => _error is null ? "Success" : $"Error({_error})";
}

/// <summary>The success of a command that returns no value.</summary>
public readonly record struct Success;

public static class Result
{
    /// <summary>Lets a command with no value write <c>return Result.Success;</c>.</summary>
    public static Success Success => default;
}
