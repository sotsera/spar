using JetBrains.Annotations;

namespace Spar;

/// <summary>Marks a class or interface as a contract. Without a name, the wire name is derived from the type name.</summary>
/// <remarks>A contract class is created from dependency injection, through any of its constructors.</remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface, Inherited = false)]
[MeansImplicitUse(ImplicitUseKindFlags.InstantiatedNoFixedConstructorSignature)]
public sealed class SparContractAttribute(string? name = null) : Attribute
{
    public string? Name { get; } = name;
}

/// <summary>Marks a command. Required on class contracts; on interface contracts it only overrides the name.</summary>
/// <remarks>
/// ReSharper and Rider count a method with this attribute as used. On an interface contract, add it to each method
/// those tools would otherwise report as never used.
/// </remarks>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
[MeansImplicitUse(ImplicitUseKindFlags.Access)]
public sealed class SparCommandAttribute(string? name = null) : Attribute
{
    public string? Name { get; } = name;

    /// <summary>
    /// An optional backend time limit, in seconds, folded into the call's cancellation token (decision "M1a names").
    /// There is no default: some commands wait on a person or run long.
    /// </summary>
    public int TimeoutSeconds { get; set; }
}

/// <summary>Gives a wire type or an error case an explicit name.</summary>
[AttributeUsage(
    AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface | AttributeTargets.Enum,
    Inherited = false)]
public sealed class SparNameAttribute(string name) : Attribute
{
    public string Name { get; } = name;
}

/// <summary>
/// Makes a stream command (one that returns <c>IAsyncEnumerable</c>) a subscription: the producer never waits for the
/// page, a bounded buffer drops the oldest values when the page falls behind, and the page sees a <c>gap</c> event with
/// the count (plan section 2c, "Subscriptions").
/// </summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class SparSubscriptionAttribute : Attribute
{
    /// <summary>The values held for a page that falls behind, at least 1; 256 when left out. Not combined with <see cref="LatestOnly"/>.</summary>
    public int Capacity { get; set; }

    /// <summary>
    /// Whether the page may subscribe again by itself after a lost connection (<see cref="SubscriptionResume.Automatic"/>,
    /// the default). A subscription with side effects, such as draining a queue, is <see cref="SubscriptionResume.Manual"/>.
    /// </summary>
    public SubscriptionResume Resume { get; set; }

    /// <summary>
    /// Keeps only the newest value, replacing older ones silently, with no <c>gap</c> events: for state that matters only as
    /// its latest value, such as progress or resource usage. Not combined with <see cref="Capacity"/>.
    /// </summary>
    public bool LatestOnly { get; set; }
}

/// <summary>Whether a subscription subscribes again by itself after a lost connection (plan, "Subscription resumption").</summary>
public enum SubscriptionResume
{
    /// <summary>The page subscribes again, with backoff, and reports <c>reconnecting</c>, <c>reset</c> and <c>resumed</c>.</summary>
    Automatic,

    /// <summary>The subscription ends with the connection; the page decides whether to subscribe again.</summary>
    Manual,
}
