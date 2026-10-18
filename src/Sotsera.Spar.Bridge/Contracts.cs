using System.Text.Json;

namespace Sotsera.Spar.Bridge;

[AttributeUsage(AttributeTargets.Interface)]
public sealed class SparContractAttribute : Attribute;

[AttributeUsage(AttributeTargets.Method)]
public sealed class SparOperationAttribute(string name, string permission) : Attribute
{
    public string Name { get; } = name;

    public string Permission { get; } = permission;
}

/// <summary>Implemented by generated glue. Application handlers remain ordinary typed C# methods.</summary>
public interface ISparBridgeDispatcher
{
    string Fingerprint
    {
        get;
    }

    bool TryGetPermission(string operation, out string permission);

    ValueTask<JsonElement> InvokeAsync(string operation, JsonElement payload, CancellationToken cancellationToken);
}

public sealed class SparPayloadException(string message, Exception? innerException = null) : Exception(message, innerException);

/// <summary>Lifecycle metadata only; no request arguments, results, or exception details.</summary>
public sealed record SparBridgeEvent(string Stage, string Document, int RequestId, string? Operation, string? Code);

public static class SparContractValidation
{
    public const int MaximumStringLength = 4096;

    public static void RequestString(string? value, bool nullable)
    {
        if ((!nullable && value is null) || value?.Length > MaximumStringLength)
        {
            throw new SparPayloadException("A request string is null or exceeds the contract limit.");
        }
    }

    public static void ResponseString(string? value, bool nullable)
    {
        if ((!nullable && value is null) || value?.Length > MaximumStringLength)
        {
            throw new InvalidOperationException("A response string is null or exceeds the contract limit.");
        }
    }
}
