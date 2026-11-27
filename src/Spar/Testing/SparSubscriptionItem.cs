using System.Diagnostics.CodeAnalysis;

namespace Spar;

/// <summary>
/// A subscription's status event as a typed test client sees it, the same ones a page's <c>events</c> handlers get (plan
/// section 2c, "The <c>events</c> slot"): <see cref="Reset"/> before values that replace earlier ones, <see cref="Gap"/>
/// when the buffer dropped values for a consumer that fell behind, and <see cref="Reconnecting"/> and
/// <see cref="Resumed"/> around an automatic resume.
/// </summary>
public closed record SparSubscriptionEvent
{
    /// <summary>What came before is stale: the values that follow start again.</summary>
    public sealed record Reset : SparSubscriptionEvent;

    /// <summary>The buffer dropped <paramref name="Dropped"/> values before the next one.</summary>
    public sealed record Gap(long Dropped) : SparSubscriptionEvent;

    /// <summary>The connection was lost; the client subscribes again, this being its <paramref name="Attempt"/>-th try.</summary>
    public sealed record Reconnecting(int Attempt) : SparSubscriptionEvent;

    /// <summary>The subscription runs again after a lost connection.</summary>
    public sealed record Resumed : SparSubscriptionEvent;
}

/// <summary>One item of a subscription without declared errors in a test: a value or a status event.</summary>
public readonly struct SparSubscriptionItem<T>
{
    private readonly T _value;

    internal SparSubscriptionItem(T value)
    {
        _value = value;
        IsValue = true;
        Event = null;
    }

    internal SparSubscriptionItem(SparSubscriptionEvent @event)
    {
        _value = default!;
        IsValue = false;
        Event = @event;
    }

    public bool IsValue { get; }

    /// <summary>The value; throws when the item is an event.</summary>
    public T Value => IsValue ? _value : throw new InvalidOperationException($"The item is an event, not a value: {Event}.");

    /// <summary>The status event, or null for a value.</summary>
    public SparSubscriptionEvent? Event { get; }

    public bool TryGetValue([MaybeNullWhen(false)] out T value)
    {
        value = _value;
        return IsValue;
    }

    public override string ToString() => IsValue ? $"value {_value}" : $"event {Event}";
}

/// <summary>One item of a subscription with declared errors in a test: a value, the error that ended it, or a status event.</summary>
public readonly struct SparSubscriptionItem<T, TError>
    where TError : class
{
    private readonly T _value;

    internal SparSubscriptionItem(T value)
    {
        _value = value;
        IsValue = true;
        Error = null;
        Event = null;
    }

    internal SparSubscriptionItem(TError error)
    {
        _value = default!;
        IsValue = false;
        Error = error;
        Event = null;
    }

    internal SparSubscriptionItem(SparSubscriptionEvent @event)
    {
        _value = default!;
        IsValue = false;
        Error = null;
        Event = @event;
    }

    public bool IsValue { get; }

    /// <summary>The value; throws when the item is an error or an event.</summary>
    public T Value => IsValue ? _value : throw new InvalidOperationException($"The item is not a value: {this}.");

    /// <summary>The declared error that ended the subscription, the last item; null otherwise.</summary>
    public TError? Error { get; }

    /// <summary>The status event, or null for a value or an error.</summary>
    public SparSubscriptionEvent? Event { get; }

    public bool TryGetValue([MaybeNullWhen(false)] out T value)
    {
        value = _value;
        return IsValue;
    }

    public override string ToString() => IsValue ? $"value {_value}" : Error is not null ? $"error {Error}" : $"event {Event}";
}
