using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Spar;

/// <summary>
/// The outcome of a command with expected errors: a value of <typeparamref name="T"/> or one case of
/// <typeparamref name="TError"/>, a <c>closed record</c>. A C# union, so <c>return value;</c> and
/// <c>return new SomeError.Case(…);</c> convert implicitly, and a <c>switch</c> over the result is exhaustive when
/// it handles <typeparamref name="T"/> and every error case.
/// </summary>
/// <remarks>
/// <c>default(Result&lt;T, TError&gt;)</c> is neither a value nor an error. Every member except
/// <see cref="ToString"/> throws on it, and a union <c>switch</c> over it throws.
/// </remarks>
[Union]
public readonly struct Result<T, TError> : Result<T, TError>.IUnionMembers
    where TError : class
{
    private const byte Uninitialized = 0;
    private const byte SuccessState = 1;
    private const byte ErrorState = 2;

    private readonly T _value;
    private readonly TError? _error;
    private readonly byte _state;

    // False only for a success holding null, which has no union value. Computed once, so pattern matching never
    // tests a value-type T for null (unoptimized JIT code boxes it for that test).
    private readonly bool _hasUnionValue;

    public bool IsSuccess => CheckedState() == SuccessState;

    public bool IsError => CheckedState() == ErrorState;

    /// <summary>The value. Throws when the result is an error; the message names the error.</summary>
    public T Value => CheckedState() == SuccessState
        ? _value
        : throw new InvalidOperationException($"The result is an error, not a value: {_error}.");

    /// <summary>The error. Throws when the result is a value; the message shows the value.</summary>
    public TError Error => CheckedState() == ErrorState
        ? _error!
        : throw new InvalidOperationException($"The result is a value, not an error: {_value}.");

    // The compiler reads these for patterns. They throw on default too, so a switch over default(Result) fails with
    // the same message instead of falling through. A success holding null has no union value, as the spec requires.
    object? IUnionMembers.Value => CheckedState() == SuccessState ? _value : _error;

    bool IUnionMembers.HasValue => CheckedState() != Uninitialized && _hasUnionValue;

    private Result(T value)
    {
        _value = value;
        _error = null;
        _state = SuccessState;
        _hasUnionValue = value is not null;
    }

    private Result(TError error)
    {
        ArgumentNullException.ThrowIfNull(error);

        _value = default!;
        _error = error;
        _state = ErrorState;
        _hasUnionValue = true;
    }

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

    bool IUnionMembers.TryGetValue([MaybeNullWhen(false)] out T value)
    {
        value = _value;
        return CheckedState() == SuccessState && _hasUnionValue;
    }

    bool IUnionMembers.TryGetValue([NotNullWhen(true)] out TError? value)
    {
        value = _error;
        return CheckedState() == ErrorState;
    }

    private byte CheckedState() => _state != Uninitialized
        ? _state
        : throw new InvalidOperationException(
            "default(Result) is neither a value nor an error. Return a value or an error case.");

    /// <summary>
    /// The members the compiler uses for union conversions and patterns. They sit on this provider interface so
    /// that the result's own <see cref="Value"/> stays typed.
    /// </summary>
    public interface IUnionMembers
    {
        object? Value { get; }

        bool HasValue { get; }

        static Result<T, TError> Create(T value) => new(value);

        static Result<T, TError> Create(TError error) => new(error);

        bool TryGetValue([MaybeNullWhen(false)] out T value);

        bool TryGetValue([NotNullWhen(true)] out TError? value);
    }
}

/// <summary>
/// The outcome of a command with expected errors and no value: <see cref="Spar.Success"/> or one case of
/// <typeparamref name="TError"/>. A command writes <c>return Result.Success;</c> or
/// <c>return new SomeError.Case(…);</c>.
/// </summary>
[Union]
public readonly struct Result<TError> : Result<TError>.IUnionMembers
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

    object? IUnionMembers.Value => CheckedState() == SuccessState ? Result.Success : _error;

    bool IUnionMembers.HasValue => CheckedState() != Uninitialized;

    private Result(TError? error)
    {
        _error = error;
        _state = error is null ? SuccessState : ErrorState;
    }

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

    bool IUnionMembers.TryGetValue(out Success value)
    {
        value = default;
        return CheckedState() == SuccessState;
    }

    bool IUnionMembers.TryGetValue([NotNullWhen(true)] out TError? value)
    {
        value = _error;
        return CheckedState() == ErrorState;
    }

    private byte CheckedState() => _state != Uninitialized
        ? _state
        : throw new InvalidOperationException(
            "default(Result) is neither a success nor an error. Return Result.Success or an error case.");

    public interface IUnionMembers
    {
        object? Value { get; }

        bool HasValue { get; }

        static Result<TError> Create(Success success) => new(null);

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
