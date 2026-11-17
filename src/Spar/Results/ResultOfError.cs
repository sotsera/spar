using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Spar;

/// <summary>
/// The outcome of a command with declared errors and no value: <see cref="Spar.Success"/> or one case of
/// <typeparamref name="TError"/>. A command writes <c>return Result.Success;</c> or <c>return new SomeError.Case(…);</c>.
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

    private Result(TError? error)
    {
        _error = error;
        _state = error is null ? SuccessState : ErrorState;
    }

    public bool IsSuccess => CheckedState() == SuccessState;

    public bool IsError => CheckedState() == ErrorState;

    /// <summary>The error. Throws when the result is a success.</summary>
    public TError Error => CheckedState() == ErrorState
        ? _error!
        : throw new InvalidOperationException("The result is a success, not an error.");

    object? IUnionMembers.Value => CheckedState() == SuccessState ? Result.Success : _error;

    bool IUnionMembers.HasValue => CheckedState() != Uninitialized;

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
        : throw new InvalidOperationException("default(Result) is neither a success nor an error. Return Result.Success or an error case.");

    /// <summary>The members the compiler uses for union conversions and patterns.</summary>
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

/// <summary>Lets a command with no value write <c>return Result.Success;</c>.</summary>
public static class Result
{
    public static Success Success => default;
}
