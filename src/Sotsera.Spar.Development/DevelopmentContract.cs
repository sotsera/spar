using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Sotsera.Spar.Bridge;

namespace Sotsera.Spar.Development;

/// <summary>Development-only check that the compiled contract agrees with generated wire metadata.</summary>
public static class DevelopmentContract
{
    public static string GetFingerprint(Type contractType)
    {
        ArgumentNullException.ThrowIfNull(contractType);
        if (!contractType.IsInterface || contractType.GetCustomAttribute<SparContractAttribute>() is null)
        {
            throw new ArgumentException("Supply the compiled Spar export interface.", nameof(contractType));
        }

        var methods = contractType.GetMethods();
        var dtos = contractType.Assembly.GetExportedTypes()
            .Where(type => type.Namespace == contractType.Namespace && type.IsClass
                && !typeof(ISparBridgeDispatcher).IsAssignableFrom(type))
            .OrderBy(type => type.Name, StringComparer.Ordinal)
            .ToArray();
        if (methods.Length is < 1 or > 32 || dtos.Length is < 1 or > 32)
        {
            throw new InvalidDataException("A contract assembly must contain one small flat contract and its DTO records.");
        }

        var operations = methods.Select(method => (Method: method,
            Export: method.GetCustomAttribute<SparOperationAttribute>()
                ?? throw new InvalidDataException("Every contract method needs an explicit export.")))
            .OrderBy(operation => operation.Export.Name, StringComparer.Ordinal);
        using var bytes = new MemoryStream();
        using (var writer = new Utf8JsonWriter(bytes))
        {
            writer.WriteStartObject();
            writer.WriteNumber("protocol", 1);
            writer.WriteString("namespaceName", contractType.Namespace);
            writer.WriteString("contract", contractType.Name);
            writer.WriteStartArray("operations");
            foreach (var operation in operations)
            {
                var parameters = operation.Method.GetParameters();
                var result = operation.Method.ReturnType;
                if (parameters.Length != 2 || parameters[1].ParameterType != typeof(CancellationToken)
                    || !result.IsGenericType || result.GetGenericTypeDefinition() != typeof(Task<>))
                {
                    throw new InvalidDataException("The compiled export does not match the flat Task contract.");
                }

                writer.WriteStartObject();
                writer.WriteString("Name", operation.Export.Name);
                writer.WriteString("Permission", operation.Export.Permission);
                writer.WriteString("Method", operation.Method.Name);
                writer.WriteString("Request", parameters[0].ParameterType.Name);
                writer.WriteString("Response", result.GenericTypeArguments[0].Name);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteStartArray("dtos");
            var nullability = new NullabilityInfoContext();
            foreach (var dto in dtos)
            {
                var constructors = dto.GetConstructors();
                if (!dto.IsSealed || constructors.Length != 1)
                {
                    throw new InvalidDataException("Contract DTOs must be sealed positional records.");
                }

                var fields = constructors[0].GetParameters();
                if (fields.Length is < 1 or > 16)
                {
                    throw new InvalidDataException("Contract DTOs support one to sixteen scalar fields.");
                }

                writer.WriteStartObject();
                writer.WriteString("Name", dto.Name);
                writer.WriteStartArray("Fields");
                foreach (var field in fields)
                {
                    var type = field.ParameterType == typeof(int) ? "int"
                        : field.ParameterType == typeof(bool) ? "bool"
                        : field.ParameterType == typeof(string)
                            ? nullability.Create(field).ReadState == NullabilityState.Nullable ? "string?" : "string"
                            : throw new InvalidDataException("Unsupported compiled contract field type.");
                    writer.WriteStartObject();
                    writer.WriteString("Name", field.Name);
                    writer.WriteString("Type", type);
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return Convert.ToHexStringLower(SHA256.HashData(bytes.ToArray()));
    }
}
