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

    public static void Emit(CodeWriter code, IReadOnlyList<ContractModel> contracts, PageSecurityModel? security = null, string appId = "")
    {
        code.Open("internal static class SparContracts");
        code.Line($"public static {Runtime}.SparCatalog Catalog {{ get; }} = new(");
        code.Line($"    \"{Fingerprint(contracts)}\",");
        code.Line("    [");
        foreach (var contract in contracts)
        {
            foreach (var command in contract.Commands)
            {
                code.Line("        " + Entry(contract, command));
            }
        }

        code.Line("    ],");
        code.Line("    AddServices)");
        code.Line("{");
        code.Line("    AppAssembly = typeof(SparContracts).Assembly,");

        // By name only: the identity's values are in their own file, so they never change this one (ShapeEmitter).
        code.Line("    BuildIdentity = SparBuildIdentity.Block.ToArray(),");
        if (appId.Length > 0)
        {
            code.Line($"    AppId = {Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatLiteral(appId, quote: true)},");
        }

        EmitInterfaces(code, contracts);
        EmitPageSecurity(code, security);
        code.Line("};");
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

    /// <summary>The interface contracts, whose registration the app's start checks; nothing when there are none.</summary>
    private static void EmitInterfaces(CodeWriter code, IReadOnlyList<ContractModel> contracts)
    {
        var interfaces = contracts.Where(c => c.IsInterface).ToList();
        if (interfaces.Count == 0)
        {
            return;
        }

        code.Line("    Interfaces =");
        code.Line("    [");
        foreach (var contract in interfaces)
        {
            var name = contract.CSharp.StartsWith("global::", System.StringComparison.Ordinal) ? contract.CSharp.Substring("global::".Length) : contract.CSharp;
            code.Line($"        new(\"{contract.WireName}\", \"{name}\", typeof({contract.CSharp})),");
        }

        code.Line("    ],");
    }

    /// <summary>
    /// The page security Program.cs declares (plan section 3b), for the hosts: only what differs from the strict default,
    /// so an app that widens nothing gets <c>PageSecurity = new()</c>. Nothing when the app has no Create call.
    /// </summary>
    private static void EmitPageSecurity(CodeWriter code, PageSecurityModel? security)
    {
        if (security is null)
        {
            return;
        }

        var lines = new List<string>();
        Sources(lines, "FontSrc", security.FontSrc);
        Sources(lines, "ImgSrc", security.ImgSrc);
        Sources(lines, "ConnectSrc", security.ConnectSrc);
        Sources(lines, "MediaSrc", security.MediaSrc);
        Sources(lines, "ScriptSrc", security.ScriptSrc);
        Sources(lines, "StyleSrc", security.StyleSrc);
        Sources(lines, "FrameSrc", security.FrameSrc);
        Flag(lines, "AllowInlineStyles", security.AllowInlineStyles, true);
        Flag(lines, "AllowEval", security.AllowEval, true);
        Flag(lines, "AllowWebAssembly", security.AllowWebAssembly, true);
        Flag(lines, "StyleNonce", security.StyleNonce, true);
        Flag(lines, "TrustedTypes", security.TrustedTypes, false);
        Flag(lines, "Integrity", security.Integrity, false);
        if (security.Listener != "Always")
        {
            lines.Add($"Listener = global::Spar.CspReports.{security.Listener},");
        }

        if (security.Endpoint != "Always")
        {
            lines.Add($"Endpoint = global::Spar.CspReports.{security.Endpoint},");
        }

        if (security.Permissions.Length > 0)
        {
            lines.Add($"Permissions = [{string.Join(", ", security.Permissions.Select(p => "global::Spar.Permission." + p))}],");
        }

        if (lines.Count == 0)
        {
            code.Line($"    PageSecurity = new {Runtime}.SparPageSecurity(),");
            return;
        }

        code.Line($"    PageSecurity = new {Runtime}.SparPageSecurity");
        code.Line("    {");
        foreach (var line in lines)
        {
            code.Line("        " + line);
        }

        code.Line("    },");
    }

    private static void Sources(List<string> lines, string name, EquatableArray<string> sources)
    {
        if (sources.Length > 0)
        {
            var literals = sources.Select(s => Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatLiteral(s, quote: true));
            lines.Add($"{name} = [{string.Join(", ", literals)}],");
        }
    }

    /// <summary>A flag, written only when it is <paramref name="whenSet"/>, the opposite of its default.</summary>
    private static void Flag(List<string> lines, string name, bool value, bool whenSet)
    {
        if (value == whenSet)
        {
            lines.Add($"{name} = {(value ? "true" : "false")},");
        }
    }

    /// <summary>One catalog entry: a unary handler, or a stream handler with a subscription's buffer.</summary>
    private static string Entry(ContractModel contract, CommandModel command)
    {
        var name = $"\"{contract.WireName}\", \"{command.WireName}\", {command.TimeoutSeconds}";
        if (!command.IsStream)
        {
            return $"new({name}, {HandlerName(contract, command)}),";
        }

        var settings = $"StreamHandler = {HandlerName(contract, command)}";
        if (command.Subscription is { } subscription)
        {
            settings += $", Capacity = {subscription.Capacity}";
            settings += subscription.LatestOnly ? ", LatestOnly = true" : "";
        }

        return $"new({name}, null) {{ {settings} }},";
    }

    private static void EmitHandler(CodeWriter code, ContractModel contract, CommandModel command)
    {
        var parameters = command.IsStream ? $"{Runtime}.SparCallContext call, {Runtime}.SparItemSink sink" : $"{Runtime}.SparCallContext call";
        code.Open($"private static async global::System.Threading.Tasks.ValueTask<{Runtime}.SparOutcome> {HandlerName(contract, command)}({parameters})");
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
        if (command.IsStream)
        {
            EmitStreamLoop(code, command, $"contract.{command.MethodName}({string.Join(", ", arguments)})");
            return;
        }

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

    /// <summary>
    /// A stream's loop: each item to the sink, which stops a cancelled call at its next item; the first declared error
    /// ends the stream in-band. Leaving the loop, by its end, a return or an exception, disposes the iterator.
    /// </summary>
    private static void EmitStreamLoop(CodeWriter code, CommandModel command, string invocation)
    {
        var items = $"global::System.Threading.Tasks.TaskAsyncEnumerableExtensions.WithCancellation({invocation}, call.CancellationToken)";
        code.Open($"await foreach (var item in {items})");
        if (command.Return == ReturnShape.ResultItems)
        {
            code.Open($"if (!{Runtime}.SparItemSink.TryGetValue(item, out var value, out var error))");
            code.Line($"return sink.End(error, SparSerializers.Write{command.ErrorKey});");
            code.Close();
            code.Line();
            code.Line($"await sink.WriteAsync(value, SparSerializers.Write{command.Value!.Key}, call.CancellationToken);");
        }
        else
        {
            code.Line($"await sink.WriteAsync(item, SparSerializers.Write{command.Value!.Key}, call.CancellationToken);");
        }

        code.Close();
        code.Line();
        code.Line($"return {Runtime}.SparOutcome.FromValue(global::System.ReadOnlyMemory<byte>.Empty);");
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
                    .Append(command.Return).Append(' ').Append(command.Value?.Key ?? "-").Append(' ').Append(command.ErrorKey ?? "-");

                // What the page's code depends on: its events and its resume. The buffer's size is the backend's own.
                if (command.Subscription is { } subscription)
                {
                    shape.Append(" subscription").Append(subscription.LatestOnly ? " latest" : "").Append(subscription.Automatic ? " automatic" : " manual");
                }

                shape.Append('\n');
            }

            foreach (var record in contract.Records)
            {
                shape.Append("  record ").Append(record.Key).Append(Fields(record.Properties)).Append('\n');
            }

            foreach (var model in contract.Enums)
            {
                shape.Append("  enum ").Append(model.Key).Append(' ').Append(string.Join(",", model.Members)).Append('\n');
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
