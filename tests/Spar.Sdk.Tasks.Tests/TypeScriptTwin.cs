using System.Text;
using Spar.Generator;
using Spar.Generator.Emit;
using Spar.Sdk.Tasks.Client;

namespace Spar.Sdk.Tasks.Tests;

/// <summary>
/// The typed TypeScript twin of the generated package (decision "Generated JavaScript is validated against tsc in
/// Spar's own tests"): from the same model, one <c>.ts</c> per module that the client step writes as <c>.js</c> and
/// <c>.d.ts</c>. Written apart from the client step's emitters, sharing only the naming rules, so <c>tsc</c> checks
/// every reader, writer and decoder against the declared types, and its output can be compared with Spar's.
/// </summary>
internal static class TypeScriptTwin
{
    /// <summary>The twin's files by name: each contract, <c>shared</c> when contracts share types, and <c>spar</c>.</summary>
    public static SortedDictionary<string, string> Files(ClientLayout layout)
    {
        var files = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var contract in layout.Contracts)
        {
            files[$"{contract.Model.WireName}.ts"] = Contract(layout, contract);
        }

        if (layout.Shared.Any())
        {
            files["shared.ts"] = Shared(layout);
        }

        var fingerprint = CatalogEmitter.Fingerprint(layout.Contracts.Select(c => c.Model));
        files["spar.ts"] = $"export const fingerprint: string = {TypeScriptNames.Literal(fingerprint)};\n";
        return files;
    }

    private static string Contract(ClientLayout layout, ClientContract contract)
    {
        var model = contract.Model;
        var ts = new StringBuilder()
            .Append($"import {{ {string.Join(", ", Functions(model))} }} from \"./sdk/sdk.js\";\n")
            .Append("import * as wire from \"./sdk/wire.js\";\n")
            .Append("import type { Decoder, StreamDecoder } from \"./sdk/sdk.js\";\n")
            .Append("import type { Call, CallOptions, LatestOnlyEvents, Result, SubscribeOptions, Subscription, SubscriptionEvents } from \"@spar/app/sdk\";\n");

        var imported = layout.Imported(contract).ToList();
        if (imported.Count > 0)
        {
            var functions = imported.SelectMany(t => new[] { ClientNames.Reader(t.Name), ClientNames.Writer(t.Name) });
            var names = string.Join(", ", imported.Select(t => t.Name));
            ts.Append($"import {{ {string.Join(", ", functions)} }} from \"./shared.js\";\n")
                .Append($"import type {{ {names} }} from \"./shared.js\";\n")
                .Append($"export type {{ {names} }} from \"./shared.js\";\n");
        }

        foreach (var type in layout.Own(contract))
        {
            Type(ts, layout, type, export: "");
        }

        ts.Append("const noValue = () => undefined;\n");
        ts.Append("const noErrors = () => unknownErrorCase;\n");
        foreach (var command in model.Commands)
        {
            var value = command.Value is null ? "void" : TypeOf(layout, command.Value);
            var error = command.ErrorKey is null ? "noErrors" : ClientNames.Reader(layout.Type(command.ErrorKey).Name);
            if (IsStream(command))
            {
                // A stream's decoder reads each item's parsed JSON.
                ts.Append($"const {ClientNames.Decoder(command.WireName)}: StreamDecoder<{value}> = {{ item: item => {Read(layout, command.Value!, "item", 1)}, error: {error} }};\n");
                continue;
            }

            var read = command.Value is null ? "noValue" : $"json => {Read(layout, command.Value, "JSON.parse(json)", 0)}";
            ts.Append($"const {ClientNames.Decoder(command.WireName)}: Decoder<{value}> = {{ value: {read}, error: {error} }};\n");
        }

        ContractObject(ts, layout, contract);
        TestingTable(ts, layout, contract);
        return ts.ToString();
    }

    /// <summary>What @spar/app/testing reads: typed here, so tsc checks each reader and writer against its command.</summary>
    private static void TestingTable(StringBuilder ts, ClientLayout layout, ClientContract contract)
    {
        ts.Append("export const __sparTesting = {\n");
        foreach (var command in contract.Model.Commands)
        {
            ts.Append($"  {LiteralKey(command.WireName)}: {{ contract: {TypeScriptNames.Literal(contract.Model.WireName)}, command: {TypeScriptNames.Literal(command.WireName)}, ");
            if (IsStream(command))
            {
                ts.Append(command.Subscription is null ? "stream: \"stream\", " : "stream: \"subscription\", ");
            }

            if (command.Request is not null)
            {
                ts.Append($"request: (json: string): {TypeOf(layout, command.Request)} => {Read(layout, command.Request, "JSON.parse(json)", 0)}, ");
            }

            if (command.Value is not null)
            {
                ts.Append($"value: (value: {TypeOf(layout, command.Value)}): string => {Write(layout, command.Value, "value", 0)}, ");
            }

            if (command.ErrorKey is not null)
            {
                ts.Append($"error: {ClientNames.Writer(layout.Type(command.ErrorKey).Name)}, ");
            }

            ts.Append("},\n");
        }

        ts.Append("};\n");
    }

    private static string Shared(ClientLayout layout)
    {
        var ts = new StringBuilder()
            .Append("import { unknownErrorCase } from \"./sdk/sdk.js\";\n")
            .Append("import * as wire from \"./sdk/wire.js\";\n");
        foreach (var type in layout.Shared)
        {
            Type(ts, layout, type, export: "export ");
        }

        return ts.ToString();
    }

    private static void Type(StringBuilder ts, ClientLayout layout, ClientType type, string export)
    {
        if (type.Enum is { } model)
        {
            var name = TypeScriptNames.Literal(type.Name);
            var cases = string.Concat(model.Members.Select(m => $"    case {TypeScriptNames.Literal(m)}:\n"));
            ts.Append($"export type {type.Name} = {string.Join(" | ", model.Members.Select(TypeScriptNames.Literal))};\n");
            ts.Append($"{export}function {ClientNames.Reader(type.Name)}(value: unknown): {type.Name} {{\n  switch (value) {{\n{cases}");
            ts.Append($"      return value;\n    default:\n      return wire.unknownMember(value, {name});\n  }}\n}}\n");
            ts.Append($"{export}function {ClientNames.Writer(type.Name)}(value: {type.Name}): string {{\n  switch (value) {{\n{cases}");
            ts.Append($"      return wire.writeString(value);\n    default:\n      return wire.refusedMember(value, {name});\n  }}\n}}\n");
            return;
        }

        if (type.Record is { } record)
        {
            ts.Append($"export interface {type.Name} {{\n");
            foreach (var property in record.Properties)
            {
                ts.Append($"  readonly {Key(property.JsonName)}: {TypeOf(layout, property.Type)};\n");
            }

            ts.Append("}\n");
            ts.Append($"{export}function {ClientNames.Reader(type.Name)}(value: unknown): {type.Name} {{\n");
            ts.Append("  const object = wire.readObject(value);\n");
            ts.Append($"  return {ObjectLiteral(layout, record.Properties)};\n}}\n");
            ts.Append($"{export}function {ClientNames.Writer(type.Name)}(value: {type.Name}): string {{\n");
            ts.Append(record.Properties.Length == 0 ? "  return \"{}\";\n" : $"  return wire.writeObject([{Members(layout, record.Properties)}]);\n");
            ts.Append("}\n");
            return;
        }

        var error = type.Error!;
        ts.Append($"export interface {type.Name} {{\n");
        foreach (var @case in error.Cases)
        {
            var payload = @case.Properties.Length == 0
                ? "undefined"
                : "{ " + string.Concat(@case.Properties.Select(p => $"readonly {Key(p.JsonName)}: {TypeOf(layout, p.Type)}; ")) + "}";
            ts.Append($"  readonly {Key(@case.Code)}: {payload};\n");
        }

        ts.Append("}\n");
        ts.Append($"{export}function {ClientNames.Reader(type.Name)}(code: string, payload: Readonly<Record<string, unknown>>): {type.Name}[keyof {type.Name}] | typeof unknownErrorCase {{\n");
        ts.Append("  switch (code) {\n");
        foreach (var @case in error.Cases)
        {
            ts.Append(@case.Properties.Length == 0
                ? $"    case {TypeScriptNames.Literal(@case.Code)}: return undefined;\n"
                : $"    case {TypeScriptNames.Literal(@case.Code)}: {{ const object = payload; return {ObjectLiteral(layout, @case.Properties)}; }}\n");
        }

        ts.Append("    default: return unknownErrorCase;\n  }\n}\n");

        // The writer the fake backend of page tests uses: each case's payload typed as that case.
        ts.Append($"{export}function {ClientNames.Writer(type.Name)}(code: string, value: unknown): string {{\n  switch (code) {{\n");
        foreach (var @case in error.Cases)
        {
            var code = TypeScriptNames.Literal(@case.Code);
            if (@case.Properties.Length == 0)
            {
                ts.Append($"    case {code}: return wire.writeObject([wire.member(\"code\", wire.writeString(code))]);\n");
                continue;
            }

            var members = string.Concat(@case.Properties.Select(p =>
                $"wire.member({TypeScriptNames.Literal(p.JsonName)}, {Write(layout, p.Type, Access("payload", p.JsonName), 0)}), "));
            ts.Append($"    case {code}: {{ const payload = value as {type.Name}[{code}]; ")
                .Append($"return wire.writeObject([wire.member(\"code\", wire.writeString(code)), {members}]); }}\n");
        }

        ts.Append($"    default: return wire.refusedMember(code, {TypeScriptNames.Literal(type.Name)});\n  }}\n}}\n");
    }

    private static void ContractObject(StringBuilder ts, ClientLayout layout, ClientContract contract)
    {
        var model = contract.Model;
        var signatures = new StringBuilder();
        var functions = new StringBuilder();
        foreach (var command in model.Commands)
        {
            var value = command.Value is null ? "void" : TypeOf(layout, command.Value);
            var error = command.ErrorKey is null ? null : layout.Type(command.ErrorKey).Name;
            var (call, options, sdk, typeArguments, extra) = Shape(command, value, error);
            var contractName = TypeScriptNames.Literal(model.WireName);
            var commandName = TypeScriptNames.Literal(command.WireName);
            var decoder = ClientNames.Decoder(command.WireName);

            if (command.Request is null)
            {
                signatures.Append($"  readonly {Key(command.WireName)}: (options?: {options}) => {call};\n");
                functions.Append($"  {LiteralKey(command.WireName)}: (options?: {options}): {call} => {sdk}{typeArguments}({contractName}, {commandName}, () => \"\", {decoder}, {extra}options),\n");
                continue;
            }

            var request = command.Request.Kind == WireKind.Record ? "request" : TypeScriptNames.Parameter(command.RequestName ?? "request");
            var requestType = TypeOf(layout, command.Request);
            var encode = Write(layout, command.Request, request, 0);
            signatures.Append($"  readonly {Key(command.WireName)}: ({request}: {requestType}, options?: {options}) => {call};\n");
            functions.Append($"  {LiteralKey(command.WireName)}: ({request}: {requestType}, options?: {options}): {call} => {sdk}{typeArguments}({contractName}, {commandName}, () => {encode}, {decoder}, {extra}options),\n");
        }

        ts.Append($"export const {model.WireName}: {{\n{signatures}}} = Object.freeze({{\n{functions}}});\n");
    }

    private static bool IsStream(CommandModel command) => command.Return is ReturnShape.Items or ReturnShape.ResultItems;

    /// <summary>The SDK functions a module calls, in the order the client step imports them.</summary>
    private static IEnumerable<string> Functions(ContractModel model)
    {
        var all = model.Commands.ToList();
        return new[]
        {
            all.Any(c => !IsStream(c)) ? "invoke" : null,
            all.Any(c => IsStream(c) && c.Subscription is null) ? "stream" : null,
            all.Any(c => c.Subscription is not null) ? "subscribe" : null,
            "unknownErrorCase",
        }.OfType<string>();
    }

    /// <summary>
    /// A command's type, its options' type, the SDK function it calls with its type arguments, and the arguments that
    /// function takes before the options: a subscription's latest-only and automatic flags.
    /// </summary>
    private static (string Call, string Options, string Sdk, string TypeArguments, string Extra) Shape(CommandModel command, string value, string? error)
    {
        // Spelled as the app model writes them (docs/app-model.md section 5): type arguments left at their defaults are left out.
        var errors = error ?? "{}";
        var withError = error is null ? value : $"{value}, {error}";
        if (command.Subscription is { } subscription)
        {
            var events = subscription.LatestOnly ? "LatestOnlyEvents" : "SubscriptionEvents";
            var type = subscription.LatestOnly ? $"Subscription<{value}, {errors}, LatestOnlyEvents>" : $"Subscription<{withError}>";
            var resume = subscription.Automatic ? "automatic" : "manual";
            var flags = $"{(subscription.LatestOnly ? "true" : "false")}, {(subscription.Automatic ? "true" : "false")}, ";
            return (type, $"SubscribeOptions<\"{resume}\">", "subscribe", $"<{value}, {errors}, {events}>", flags);
        }

        if (IsStream(command))
        {
            return ($"AsyncIterable<Result<{withError}>>", "CallOptions", "stream", $"<{value}, {errors}>", "");
        }

        return (error is null ? $"Call<{value}>" : $"Call<{value}, {error}>", "CallOptions", "invoke", $"<{value}, {errors}>", "");
    }

    /// <summary>The TypeScript type of a wire type: the wire-rules table of plan section 2.</summary>
    private static string TypeOf(ClientLayout layout, TypeRef type) => type.Kind switch
    {
        WireKind.String or WireKind.Decimal or WireKind.Char or WireKind.Guid or WireKind.DateTime or WireKind.DateTimeOffset
            or WireKind.DateOnly or WireKind.TimeOnly or WireKind.TimeSpan => "string",
        WireKind.Boolean => "boolean",
        WireKind.Byte or WireKind.SByte or WireKind.Int16 or WireKind.UInt16 or WireKind.Int32 or WireKind.UInt32
            or WireKind.Single or WireKind.Double => "number",
        WireKind.Int64 or WireKind.UInt64 => "bigint",
        WireKind.Bytes => "Uint8Array",
        WireKind.Record or WireKind.Enum => layout.Type(type.Key).Name,
        WireKind.Nullable => $"{TypeOf(layout, type.Element!)} | null",
        WireKind.List when type.Element!.Kind is WireKind.Nullable or WireKind.List => $"readonly ({TypeOf(layout, type.Element)})[]",
        WireKind.List => $"readonly {TypeOf(layout, type.Element)}[]",
        _ => throw new InvalidOperationException($"No TypeScript type for {type.Kind}."),
    };

    private static string ObjectLiteral(ClientLayout layout, EquatableArray<PropertyModel> properties) =>
        "{ " + string.Concat(properties.Select(p =>
            $"{LiteralKey(p.JsonName)}: {Read(layout, p.Type, $"wire.field(object, {TypeScriptNames.Literal(p.JsonName)})", 0)}, ")) + "}";

    private static string Members(ClientLayout layout, EquatableArray<PropertyModel> properties) =>
        string.Join(", ", properties.Select(p =>
            $"wire.member({TypeScriptNames.Literal(p.JsonName)}, {Write(layout, p.Type, Access("value", p.JsonName), 0)})"));

    private static string Read(ClientLayout layout, TypeRef type, string value, int depth) => type.Kind switch
    {
        WireKind.List => $"wire.readList({value}, {Reader(layout, type.Element!, depth + 1)})",
        WireKind.Nullable => $"wire.readNullable({value}, {Reader(layout, type.Element!, depth + 1)})",
        _ => $"{Reader(layout, type, depth)}({value})",
    };

    private static string Reader(ClientLayout layout, TypeRef type, int depth) => type.Kind switch
    {
        // Dates, times and GUIDs are checked by the backend; the page passes their text as it is.
        WireKind.String or WireKind.Guid or WireKind.DateTime or WireKind.DateTimeOffset or WireKind.DateOnly
            or WireKind.TimeOnly or WireKind.TimeSpan => "wire.readString",
        WireKind.Boolean or WireKind.Byte or WireKind.SByte or WireKind.Int16 or WireKind.UInt16 or WireKind.Int32
            or WireKind.UInt32 or WireKind.Int64 or WireKind.UInt64 or WireKind.Single or WireKind.Double
            or WireKind.Decimal or WireKind.Char or WireKind.Bytes => $"wire.read{type.Kind}",
        WireKind.Record or WireKind.Enum => ClientNames.Reader(layout.Type(type.Key).Name),
        _ => $"{Item(depth)} => {Read(layout, type, Item(depth), depth)}",
    };

    private static string Write(ClientLayout layout, TypeRef type, string value, int depth) => type.Kind switch
    {
        WireKind.List => $"wire.writeList({value}, {Writer(layout, type.Element!, depth + 1)})",
        WireKind.Nullable => $"wire.writeNullable({value}, {Writer(layout, type.Element!, depth + 1)})",
        _ => $"{Writer(layout, type, depth)}({value})",
    };

    private static string Writer(ClientLayout layout, TypeRef type, int depth) => type.Kind switch
    {
        // Dates, times and GUIDs are checked by the backend; the page passes their text as it is.
        WireKind.String or WireKind.Guid or WireKind.DateTime or WireKind.DateTimeOffset or WireKind.DateOnly
            or WireKind.TimeOnly or WireKind.TimeSpan => "wire.writeString",
        WireKind.Boolean or WireKind.Byte or WireKind.SByte or WireKind.Int16 or WireKind.UInt16 or WireKind.Int32
            or WireKind.UInt32 or WireKind.Int64 or WireKind.UInt64 or WireKind.Single or WireKind.Double
            or WireKind.Decimal or WireKind.Char or WireKind.Bytes => $"wire.write{type.Kind}",
        WireKind.Record or WireKind.Enum => ClientNames.Writer(layout.Type(type.Key).Name),
        _ => $"{Item(depth)} => {Write(layout, type, Item(depth), depth)}",
    };

    private static string Item(int depth) => depth <= 1 ? "item" : $"item{depth}";

    /// <summary>A plain identifier that is not a reserved word can be written bare and read with a dot.</summary>
    private static bool IsPlain(string name) =>
        name.Length > 0 && (char.IsAsciiLetter(name[0]) || name[0] is '_' or '$')
        && name.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '$')
        && !TypeScriptNames.IsReserved(name);

    private static string Key(string name) => IsPlain(name) ? name : TypeScriptNames.Literal(name);

    /// <summary>An object literal's key; <c>__proto__</c> written plainly would set the prototype.</summary>
    private static string LiteralKey(string name) => name == "__proto__" ? $"[{TypeScriptNames.Literal(name)}]" : Key(name);

    private static string Access(string value, string name) => IsPlain(name) ? $"{value}.{name}" : $"{value}[{TypeScriptNames.Literal(name)}]";
}
