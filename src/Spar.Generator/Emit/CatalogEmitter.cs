using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Spar.Generator.Emit;

/// <summary>
/// Writes <c>SparContracts</c>: one small async handler per command, the immutable catalog with the contracts'
/// fingerprint, and the registration of class contracts (one instance per call scope).
/// </summary>
internal static class CatalogEmitter
{
    private const string Runtime = "global::Spar.Runtime";

    public static void Emit(CodeWriter code, IReadOnlyList<ContractModel> contracts)
    {
        code.Open("internal static class SparContracts");
        code.Line($"public static {Runtime}.SparCatalog Catalog {{ get; }} = new(");
        code.Line($"    \"{Fingerprint(contracts)}\",");
        code.Line("    [");
        foreach (var contract in contracts)
        {
            foreach (var command in contract.Commands)
            {
                code.Line($"        new(\"{contract.WireName}\", \"{command.WireName}\", {command.TimeoutSeconds}, {HandlerName(contract, command)}),");
            }
        }

        code.Line("    ],");
        code.Line("    AddServices);");
        code.Line();

        code.Open("private static void AddServices(global::Microsoft.Extensions.DependencyInjection.IServiceCollection services)");
        foreach (var contract in contracts.Where(c => !c.IsInterface))
        {
            // Interface contracts get their implementation from the app's own registration.
            code.Line($"global::Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddScoped<{contract.CSharp}>(services);");
        }

        code.Close();
        foreach (var contract in contracts)
        {
            foreach (var command in contract.Commands)
            {
                code.Line();
                EmitHandler(code, contract, command);
            }
        }

        code.Close();
    }

    private static void EmitHandler(CodeWriter code, ContractModel contract, CommandModel command)
    {
        code.Open($"private static async global::System.Threading.Tasks.ValueTask<{Runtime}.SparOutcome> {HandlerName(contract, command)}({Runtime}.SparCallContext call)");
        var arguments = new List<string>();
        if (command.Request is { } request)
        {
            code.Line($"{request.CSharp} request;");
            code.Open("try");
            code.Line($"request = {Runtime}.SparJson.Read(call.Request, SparSerializers.Read{request.Key});");
            code.Close();
            code.Open("catch (global::System.Text.Json.JsonException exception)");
            code.Line($"return {Runtime}.SparOutcome.FromFailure({Runtime}.SparFailure.InvalidRequest(exception.Message));");
            code.Close();
            code.Line();
            arguments.Add("request");
        }

        if (command.TakesToken)
        {
            arguments.Add("call.CancellationToken");
        }

        code.Line($"var contract = global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<{contract.CSharp}>(call.Services);");
        var invocation = $"await contract.{command.MethodName}({string.Join(", ", arguments)})";
        if (command.Return == ReturnShape.None)
        {
            code.Line($"{invocation};");
            code.Line($"return {Runtime}.SparOutcome.FromValue(global::System.ReadOnlyMemory<byte>.Empty);");
            code.Close();
            return;
        }

        code.Line($"var result = {invocation};");
        code.Line();

        // A value that cannot be written (default(Result), a non-finite double) is a response failure: the command ran.
        code.Open("try");
        switch (command.Return)
        {
            case ReturnShape.Value:
                code.Line($"return {Runtime}.SparOutcome.FromValue({Runtime}.SparJson.Write(result, SparSerializers.Write{command.Value!.Key}));");
                break;
            case ReturnShape.ResultWithValue:
                code.Open("if (result.TryGetValue(out var value))");
                code.Line($"return {Runtime}.SparOutcome.FromValue({Runtime}.SparJson.Write(value, SparSerializers.Write{command.Value!.Key}));");
                code.Close();
                code.Line();
                code.Line($"return {Runtime}.SparOutcome.FromError({Runtime}.SparJson.Write(result.Error, SparSerializers.Write{command.ErrorKey}));");
                break;
            case ReturnShape.ResultWithoutValue:
                code.Open("if (result.IsSuccess)");
                code.Line($"return {Runtime}.SparOutcome.FromValue(global::System.ReadOnlyMemory<byte>.Empty);");
                code.Close();
                code.Line();
                code.Line($"return {Runtime}.SparOutcome.FromError({Runtime}.SparJson.Write(result.Error, SparSerializers.Write{command.ErrorKey}));");
                break;
        }

        code.Close();
        code.Open("catch (global::System.Exception exception)");
        code.Line($"return {Runtime}.SparOutcome.FromFailure({Runtime}.SparFailure.ResponseSerialization(exception));");
        code.Close();
        code.Close();
    }

    public static string HandlerName(ContractModel contract, CommandModel command) => $"Handle{contract.PascalName}{Naming.Pascal(command.WireName)}";

    /// <summary>
    /// 16 hex characters of the SHA-256 of every contract's full wire shape (plan section 2). The client step computes the
    /// page's copy with this same function, so the two can only differ when the contracts do.
    /// </summary>
    public static string Fingerprint(IEnumerable<ContractModel> contracts)
    {
        var shape = new StringBuilder();
        foreach (var contract in contracts.OrderBy(c => c.WireName, System.StringComparer.Ordinal))
        {
            shape.Append("contract ").Append(contract.WireName).Append('\n');
            foreach (var command in contract.Commands.OrderBy(c => c.WireName, System.StringComparer.Ordinal))
            {
                shape.Append("  command ").Append(command.WireName).Append(' ').Append(command.Request?.Key ?? "-").Append(' ')
                    .Append(command.Return).Append(' ').Append(command.Value?.Key ?? "-").Append(' ').Append(command.ErrorKey ?? "-").Append('\n');
            }

            foreach (var record in contract.Records)
            {
                shape.Append("  record ").Append(record.Key).Append(Fields(record.Properties)).Append('\n');
            }

            foreach (var error in contract.Errors)
            {
                shape.Append("  error ").Append(error.Key).Append('\n');
                foreach (var @case in error.Cases)
                {
                    shape.Append("    case ").Append(@case.Code).Append(Fields(@case.Properties)).Append('\n');
                }
            }
        }

        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(shape.ToString()));
        return string.Concat(hash.Take(8).Select(b => b.ToString("x2")));
    }

    private static string Fields(EquatableArray<PropertyModel> properties) =>
        string.Concat(properties.Select(p => " " + p.JsonName + ":" + p.Type.Key));
}
