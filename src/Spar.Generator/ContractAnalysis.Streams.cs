using System.Linq;
using Microsoft.CodeAnalysis;

namespace Spar.Generator;

/// <summary>
/// Streams and subscriptions (plan section 2c): <c>IAsyncEnumerable&lt;T&gt;</c> and
/// <c>IAsyncEnumerable&lt;Result&lt;T, TError&gt;&gt;</c>, and the <c>[SparSubscription]</c> that makes one a subscription.
/// </summary>
internal sealed partial class ContractAnalysis
{
    /// <summary>The buffer of a subscription that sets no <c>Capacity</c> (decision "M3a's decide-first questions").</summary>
    private const int DefaultCapacity = 256;

    private static bool IsAsyncEnumerable(INamedTypeSymbol type) =>
        type.OriginalDefinition.ToDisplayString() == "System.Collections.Generic.IAsyncEnumerable<T>";

    /// <summary>A stream's item: a wire value, or a <c>Result&lt;T, TError&gt;</c> whose error ends the stream.</summary>
    private (ReturnShape Shape, TypeRef? Value, string? ErrorKey, string? ErrorCSharp)? StreamReturn(IMethodSymbol method, ITypeSymbol item)
    {
        if (item is not INamedTypeSymbol result || !IsSparResult(result))
        {
            var value = Wire(item, method);
            return value is null ? null : (ReturnShape.Items, value, null, null);
        }

        // A stream of Result<TError> would carry nothing but its end: a unary Task<Result<TError>> says that.
        if (result.TypeArguments.Length == 1)
        {
            Report(Descriptors.UnsupportedReturn, method, method.Name, method.ReturnType.ToDisplayString());
            return null;
        }

        var error = Error((INamedTypeSymbol)result.TypeArguments[1], method);
        if (error is null)
        {
            return null;
        }

        var itemValue = Wire(result.TypeArguments[0], method);
        return itemValue is null ? null : (ReturnShape.ResultItems, itemValue, error.Key, error.CSharp);
    }

    /// <summary>
    /// The command's <c>[SparSubscription]</c>, or null without one. False when the attribute is on a command that is not a
    /// stream (SPAR017) or its settings contradict each other (SPAR018); the command is then left out.
    /// </summary>
    private bool Subscription(IMethodSymbol method, ReturnShape shape, out SubscriptionModel? subscription)
    {
        subscription = null;
        var data = method.GetAttributes().FirstOrDefault(a =>
            a.AttributeClass?.Name == "SparSubscriptionAttribute" && a.AttributeClass.ContainingNamespace.ToDisplayString() == "Spar");
        if (data is null)
        {
            return true;
        }

        var location = data.ApplicationSyntaxReference?.GetSyntax().GetLocation();
        if (shape is not (ReturnShape.Items or ReturnShape.ResultItems))
        {
            ReportAt(Descriptors.SubscriptionNotStream, method, location, method.Name, method.ReturnType.ToDisplayString());
            return false;
        }

        var capacity = Named<int>(data, "Capacity");
        var latestOnly = Named<bool>(data, "LatestOnly") ?? false;

        // SubscriptionResume.Manual is 1; Automatic, the default, is 0.
        var automatic = (Named<int>(data, "Resume") ?? 0) == 0;
        if (latestOnly && capacity is not null)
        {
            ReportAt(Descriptors.InvalidSubscription, method, location, method.Name, Descriptors.CapacityWithLatestOnly);
            return false;
        }

        if (capacity is <= 0)
        {
            ReportAt(Descriptors.InvalidSubscription, method, location, method.Name, string.Format(Descriptors.CapacityNotPositive, capacity));
            return false;
        }

        subscription = new SubscriptionModel(latestOnly ? 1 : capacity ?? DefaultCapacity, latestOnly, automatic);
        return true;
    }

    private static T? Named<T>(AttributeData data, string name)
        where T : struct
    {
        foreach (var argument in data.NamedArguments)
        {
            if (argument.Key == name && argument.Value.Value is T value)
            {
                return value;
            }
        }

        return null;
    }

    private void ReportAt(DiagnosticDescriptor descriptor, ISymbol symbol, Location? location, params object[] arguments)
    {
        if (_report is null)
        {
            return;
        }

        var place = location ?? symbol.Locations.FirstOrDefault(l => l.IsInSource) ?? Location.None;
        _report(symbol, Diagnostic.Create(descriptor, place, arguments));
    }
}
