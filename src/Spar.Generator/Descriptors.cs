using Microsoft.CodeAnalysis;

namespace Spar.Generator;

/// <summary>
/// The contract diagnostics, with the IDs of the decided map (plan section 2, "Diagnostics"; docs/evidence/contract-syntax).
/// Each message says how to fix the problem.
/// </summary>
internal static class Descriptors
{
    private const string Contracts = "Spar.Contracts";
    private const string ProgramShape = "Spar.Program";
    private const string PageSecurity = "Spar.PageSecurity";
    private const string Scenes = "Spar.Scenes";

    /// <summary>The restated descriptors of <see cref="InReferencedAssembly"/>, by ID and message.</summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, DiagnosticDescriptor> Referenced = new();

    public static readonly DiagnosticDescriptor NotSealedClassOrInterface = Error(
        "SPAR001", "Contract must be a sealed class or an interface", "Contract '{0}' must be a sealed class or an interface", Contracts);

    public static readonly DiagnosticDescriptor GenericContract = Error(
        "SPAR002", "Contract must not be generic", "Contract '{0}' must not be generic", Contracts);

    /// <summary>{0}: what is named ("Contract 'OrdersService'"); {1}: the name; {2}: the rule; {3}: the fix.</summary>
    public static readonly DiagnosticDescriptor InvalidName = Error(
        "SPAR003", "Invalid name", "{0} resolves to the name '{1}', which is not {2}. {3}", Contracts);

    /// <summary>{0}: what is named; {1}: the name; {2}: why it is reserved; {3}: the fix.</summary>
    public static readonly DiagnosticDescriptor ReservedName = Error(
        "SPAR004", "Reserved name", "{0} resolves to the name '{1}', {2}. {3}", Contracts);

    /// <summary>{0}: what is wrong with a library the app lists in <c>SparContracts</c>, and the fix.</summary>
    public static readonly DiagnosticDescriptor ListedLibrary = Error(
        "SPAR006", "Listed contract library", "{0}", Contracts);

    public static readonly DiagnosticDescriptor DuplicateContract = Error(
        "SPAR005", "Two contracts with one name",
        "Contracts '{0}' and '{1}' both resolve to the name '{2}' (contract names are compared ignoring case). Give one of them another name with [SparContract(\"…\")]",
        Contracts);

    public static readonly DiagnosticDescriptor UnsupportedReturn = Error(
        "SPAR010", "Unsupported command return type",
        "Command '{0}' returns '{1}'. A command returns Task, Task<T>, Task<Result<T, TError>>, Task<Result<TError>>, their ValueTask forms, IAsyncEnumerable<T> or IAsyncEnumerable<Result<T, TError>>",
        Contracts);

    public static readonly DiagnosticDescriptor SubscriptionNotStream = Error(
        "SPAR017", "A subscription is a stream",
        "Command '{0}' has [SparSubscription] but returns '{1}'. A subscription returns IAsyncEnumerable<T> or IAsyncEnumerable<Result<T, TError>>",
        Contracts);

    public static readonly DiagnosticDescriptor InvalidSubscription = Error(
        "SPAR018", "Invalid subscription settings", "The [SparSubscription] of command '{0}' {1}", Contracts);

    public const string CapacityWithLatestOnly =
        "sets Capacity with LatestOnly. A subscription that keeps only the newest value holds one; remove Capacity";

    public static readonly DiagnosticDescriptor InvalidSerialGroup = Error(
        "SPAR019", "Invalid serial group", "Command '{0}' {1}", Contracts);

    public const string SerialGroupEmpty = "is in a serial group without a name. Name the group, as in [SparSerial(\"recipes\")]";

    public const string SerialGroupStream =
        "is a stream or a subscription in a serial group. A stream would hold its group for as long as it runs; take [SparSerial] off it, or off its contract and onto its other commands";

    public const string CapacityNotPositive = "sets Capacity to {0}. Capacity is a positive number of values, or left out for 256";

    public static readonly DiagnosticDescriptor TooManyRequests = Error(
        "SPAR011", "Too many request parameters",
        "Command '{0}' takes at most one request parameter, then a CancellationToken. Put the values in one record", Contracts);

    public static readonly DiagnosticDescriptor TokenNotLast = Error(
        "SPAR012", "CancellationToken must be last", "The CancellationToken of command '{0}' must be its last parameter", Contracts);

    public static readonly DiagnosticDescriptor RefParameter = Error(
        "SPAR013", "Parameter cannot be ref, out, in or params", "Parameter '{0}' of command '{1}' cannot be ref, out, in or params", Contracts);

    public static readonly DiagnosticDescriptor GenericCommand = Error(
        "SPAR014", "Command must not be generic", "Command '{0}' must not be generic", Contracts);

    public static readonly DiagnosticDescriptor UnsupportedRequest = Error(
        "SPAR015", "Unsupported request type",
        "The request of command '{0}' is '{1}'. A request is one record or one scalar; put a list inside a record", Contracts);

    public static readonly DiagnosticDescriptor DuplicateCommand = Error(
        "SPAR016", "Two commands with one name",
        "Commands {0} of contract '{1}' all resolve to the name '{2}'. Give them distinct names with [SparCommand(\"…\")]", Contracts);

    public static readonly DiagnosticDescriptor UnsupportedWireType = Error(
        "SPAR020", "Unsupported wire type", "Type '{0}' is not a supported wire type", Contracts);

    /// <summary>{0}: the record; {1}: <see cref="NotPositional"/> or <see cref="Stray"/>.</summary>
    public static readonly DiagnosticDescriptor NotPositionalRecord = Error(
        "SPAR021", "Record must be positional", "Record '{0}' {1}", Contracts);

    public const string NotPositional =
        "must be a non-abstract, non-generic positional record whose values are its constructor parameters";

    public const string Stray =
        "has a settable property '{0}' that is not a constructor parameter, so it would never reach the wire. Make it a parameter of the record, or make it read-only";

    public static readonly DiagnosticDescriptor MissingProperty = Error(
        "SPAR022", "Constructor parameter without a property",
        "Record '{0}' has no property for constructor parameter '{1}'. Name the parameter like a property, or make the record positional", Contracts);

    public static readonly DiagnosticDescriptor DuplicateJsonName = Error(
        "SPAR023", "Two properties with one JSON name",
        "Record '{0}' has two properties with the JSON name '{1}'. Rename one of them", Contracts);

    /// <summary>A warning, not an error (decision "Wire format choices"): the value works, but its meaning depends on its Kind.</summary>
    public static readonly DiagnosticDescriptor DateTimeOnWire = new(
        "SPAR024", "DateTime on the wire",
        "'{0}' carries a DateTime, whose time zone depends on its Kind: written with 'Z', an offset or nothing, and read back as the reader's local time when it has an offset. Use DateTimeOffset instead.",
        Contracts, DiagnosticSeverity.Warning, isEnabledByDefault: true);

    /// <summary>{0}: the enum; {1}: why one member name cannot stand for each value.</summary>
    public static readonly DiagnosticDescriptor UnsupportedEnum = Error(
        "SPAR025", "Unsupported enum",
        "Enum '{0}' cannot travel on the wire as one member name: {1}", Contracts);

    public static readonly DiagnosticDescriptor BadErrorType = Error(
        "SPAR030", "Unsupported error type",
        "Error type '{0}' must be a non-generic closed record with nested sealed record cases", Contracts);

    public static readonly DiagnosticDescriptor CaseNotNested = Error(
        "SPAR031", "Error case not nested",
        "Error case '{0}' derives from '{1}' but is not nested in it. Declare it inside '{1}'", Contracts);

    public static readonly DiagnosticDescriptor BadErrorCase = Error(
        "SPAR032", "Unsupported error case", "Error case '{0}' must be a non-generic sealed record", Contracts);

    public static readonly DiagnosticDescriptor ReservedPayloadName = Error(
        "SPAR033", "Reserved payload name",
        "Error case '{0}' cannot have a payload named '{1}': 'kind', 'code', 'message' and 'execution' are reserved", Contracts);

    public static readonly DiagnosticDescriptor DuplicateCode = Error(
        "SPAR034", "Two error cases with one code",
        "Error cases {0} of '{1}' both resolve to the code '{2}'. Give one a distinct code with [SparName(\"…\")]", Contracts);

    public static readonly DiagnosticDescriptor NoErrorCases = Error(
        "SPAR035", "Error type has no cases", "Error type '{0}' has no cases", Contracts);

    /// <summary>{0}: where ("Contract 'orders' uses", "The shared module declares"); {1}: the name; {2}: the C# types.</summary>
    public static readonly DiagnosticDescriptor DuplicateTypeName = Error(
        "SPAR040", "Two types with one TypeScript name",
        "{0} two types named '{1}' in TypeScript: {2}. Give one of them another name with [SparName(\"…\")]", Contracts);

    public static readonly DiagnosticDescriptor NotTopLevel = Error(
        "SPAR060", "Program.cs must use top-level statements",
        "Program.cs must be top-level statements with one SparApplication.Create and one Run; the entry point is '{0}'", ProgramShape);

    public static readonly DiagnosticDescriptor NotReturningRun = Error(
        "SPAR061", "Program.cs must return app.Run()",
        "The top-level statements of Program.cs must end with 'return app.Run();' (an int); they return '{0}'", ProgramShape);

    /// <summary>{0}: the method, <c>ContentSecurity</c> or <c>Permissions</c>.</summary>
    public static readonly DiagnosticDescriptor NotDirectlyInCreate = Error(
        "SPAR101", "Page security must be declared directly in SparApplication.Create",
        "spar.{0}(…) must be a statement of its own directly in the SparApplication.Create configuration of Program.cs, on its builder parameter, so the build can read it without running the app; not inside a condition, a loop, a helper method or another lambda",
        PageSecurity);

    /// <summary>{0}: the argument as written; {1}: the fix.</summary>
    public static readonly DiagnosticDescriptor NotConstant = Error(
        "SPAR102", "Page security values must be constants",
        "'{0}' is not a constant. The build reads the page security from Program.cs without running the app. {1}.", PageSecurity);

    /// <summary>{0}: what to write instead.</summary>
    public static readonly DiagnosticDescriptor UnsupportedShape = Error(
        "SPAR103", "Unsupported page security configuration",
        "The build cannot read this page security configuration: {0}", PageSecurity);

    /// <summary>{0}: why the value is refused, naming the method to use instead where there is one.</summary>
    public static readonly DiagnosticDescriptor InvalidValue = Error(
        "SPAR104", "Invalid page security value", "{0}", PageSecurity);

    public static readonly DiagnosticDescriptor NonceWithInlineStyles = new(
        "SPAR105", "StyleNonce() has no effect with AllowInlineStyles()",
        "Browsers ignore 'unsafe-inline' next to a nonce, so with AllowInlineStyles() Spar puts no nonce in style-src and StyleNonce() has no effect; keep one of the two",
        PageSecurity, DiagnosticSeverity.Warning, isEnabledByDefault: true);

    /// <summary>{0}: what the build cannot read, and what to write instead.</summary>
    public static readonly DiagnosticDescriptor WindowColours = Error(
        "SPAR106", "The window colours must be constants the build can read",
        "{0}. The build reads the colours of spar.Window(w => w.Background(light, dark)) from Program.cs without running the app, for the page's theme style and its hash in the Content Security Policy.",
        PageSecurity);

    /// <summary>{0}: the method; {1}: what is wrong with it.</summary>
    public static readonly DiagnosticDescriptor SceneShape = Error(
        "SPAR110", "A scene is a static method that takes a SparScene and returns Task",
        "Scene method '{0}' cannot be a scene: {1}. A scene is public static Task Name(SparScene scene), so spar snap and its test can run it.",
        Scenes);

    /// <summary>{0}: the name.</summary>
    public static readonly DiagnosticDescriptor SceneName = Error(
        "SPAR111", "A scene's name is lower case letters, digits and hyphens",
        "Scene name '{0}' is not lower case letters, digits and single hyphens (such as new-page-dialog): spar snap takes it on the command line and names its folder after it",
        Scenes);

    /// <summary>{0}: the name.</summary>
    public static readonly DiagnosticDescriptor SceneDuplicate = Error(
        "SPAR112", "Two scenes have one name",
        "Two scenes are named '{0}': spar snap names a scene, so each name is used once in the project",
        Scenes);

    /// <summary>
    /// The same diagnostic about a symbol in a referenced assembly, which has no place in the project: its message
    /// starts "In referenced assembly 'X': " (plan section 2, "Diagnostics"). One copy per descriptor.
    /// </summary>
    public static DiagnosticDescriptor InReferencedAssembly(DiagnosticDescriptor descriptor) =>
        Referenced.GetOrAdd(descriptor.Id + "\0" + descriptor.MessageFormat, _ => new DiagnosticDescriptor(
            descriptor.Id, descriptor.Title.ToString(), "In referenced assembly '{0}': {1}", descriptor.Category,
            descriptor.DefaultSeverity, isEnabledByDefault: true));

    private static DiagnosticDescriptor Error(string id, string title, string message, string category) =>
        new(id, title, message, category, DiagnosticSeverity.Error, isEnabledByDefault: true);
}
