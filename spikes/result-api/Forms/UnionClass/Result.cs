using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Spar;

/// <summary>
/// Class form declared as a C# union through a member provider. There is no default instance: an unset result is
/// <see langword="null"/>, which nullable analysis reports.
/// </summary>
[Union]
public sealed class Result<T, TError> : Result<T, TError>.IUnionMembers
    where TError : class
{
    private readonly T _value;
    private readonly TError? _error;

    // False only for a success holding null, which has no union value. Computed once, so pattern matching never
    // tests a value-type T for null (unoptimized JIT code boxes it for that test).
    private readonly bool _hasUnionValue;

    public bool IsSuccess => _error is null;

    public bool IsError => _error is not null;

    public T Value => _error is null
        ? _value
        : throw new InvalidOperationException($"The result is an error, not a value: {_error}.");

    public TError Error => _error
        ?? throw new InvalidOperationException($"The result is a value, not an error: {_value}.");

    object? IUnionMembers.Value => _error is null ? _value : _error;

    bool IUnionMembers.HasValue => _hasUnionValue;

    private Result(T value, TError? error)
    {
        _value = value;
        _error = error;
        _hasUnionValue = error is not null || value is not null;
    }

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

    bool IUnionMembers.TryGetValue([MaybeNullWhen(false)] out T value)
    {
        value = _value;
        return _error is null && _hasUnionValue;
    }

    bool IUnionMembers.TryGetValue([NotNullWhen(true)] out TError? value)
    {
        value = _error;
        return value is not null;
    }

    public interface IUnionMembers
    {
        object? Value { get; }

        bool HasValue { get; }

        static Result<T, TError> Create(T value) => new(value, null);

        static Result<T, TError> Create(TError error) =>
            new(default!, error ?? throw new ArgumentNullException(nameof(error)));

        bool TryGetValue([MaybeNullWhen(false)] out T value);

        bool TryGetValue([NotNullWhen(true)] out TError? value);
    }
}

[Union]
public sealed class Result<TError> : Result<TError>.IUnionMembers
    where TError : class
{
    // A success carries nothing, so one instance per error type serves every call.
    private static readonly Result<TError> s_success = new(null);

    private readonly TError? _error;

    public bool IsSuccess => _error is null;

    public bool IsError => _error is not null;

    public TError Error => _error ?? throw new InvalidOperationException("The result is a success, not an error.");

    object? IUnionMembers.Value => _error is null ? Result.Success : _error;

    bool IUnionMembers.HasValue => true;

    private Result(TError? error)
    {
        _error = error;
    }

    public bool TryGetError([NotNullWhen(true)] out TError? error)
    {
        error = _error;
        return error is not null;
    }

    public override string ToString() => _error is null ? "Success" : $"Error({_error})";

    bool IUnionMembers.TryGetValue(out Success value)
    {
        value = default;
        return _error is null;
    }

    bool IUnionMembers.TryGetValue([NotNullWhen(true)] out TError? value)
    {
        value = _error;
        return value is not null;
    }

    public interface IUnionMembers
    {
        object? Value { get; }

        bool HasValue { get; }

        static Result<TError> Create(Success success) => s_success;

        static Result<TError> Create(TError error) => new(error ?? throw new ArgumentNullException(nameof(error)));

        bool TryGetValue(out Success value);

        bool TryGetValue([NotNullWhen(true)] out TError? value);
    }
}

/// <summary>The success of a command that returns no value.</summary>
public readonly record struct Success;

public static class Result
{
    /// <summary>Lets a command with no value write <c>return Result.Success;</c>.</summary>
    public static Success Success => default;
}
