using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Spar.ContractGenerator;

public sealed record GeneratedContract(string CSharp, string JavaScript, string TypeScript, string Fingerprint);

public static class ContractCompiler
{
    public static GeneratedContract Generate(string source, string path)
    {
        var tree = CSharpSyntaxTree.ParseText(source, path: path);
        var root = tree.GetCompilationUnitRoot();
        Require(!root.DescendantTrivia(descendIntoTrivia: true).Any(trivia => trivia.GetStructure() is
            IfDirectiveTriviaSyntax or ElifDirectiveTriviaSyntax or ElseDirectiveTriviaSyntax
            or DefineDirectiveTriviaSyntax or UndefDirectiveTriviaSyntax), root,
            "Conditional contract declarations are not supported; generate one matching contract for every configuration.");
        var error = tree.GetDiagnostics().FirstOrDefault(item => item.Severity == DiagnosticSeverity.Error);
        if (error is not null)
        {
            throw new InvalidOperationException(error.ToString());
        }

        var scopes = root.Members.OfType<FileScopedNamespaceDeclarationSyntax>().ToArray();
        Require(scopes.Length == 1 && root.Members.Count == 1, root, "Use one file-scoped contract namespace.");
        var scope = scopes[0];
        var namespaceName = scope.Name.ToString();
        Require(namespaceName.Split('.').All(IsIdentifier), scope, "Use simple namespace identifiers.");
        var interfaces = scope.Members.OfType<InterfaceDeclarationSyntax>().ToArray();
        Require(interfaces.Length == 1, scope, "Use exactly one exported interface per contract file.");
        var contract = interfaces[0];
        var contractAttributes = contract.AttributeLists.SelectMany(list => list.Attributes).ToArray();
        Require(contractAttributes.Length == 1 && contractAttributes[0].Name.ToString() == "SparContract"
            && contractAttributes[0].ArgumentList is null && IsIdentifier(contract.Identifier.ValueText)
            && contract.Modifiers.Count == 1 && contract.Modifiers.Any(SyntaxKind.PublicKeyword)
            && contract.TypeParameterList is null && contract.BaseList is null, contract,
            "Mark a public, non-generic interface with [SparContract]. Inheritance is not supported yet.");

        var records = scope.Members.OfType<RecordDeclarationSyntax>().ToArray();
        Require(scope.Members.Count == records.Length + 1 && records.Length is > 0 and <= 32, scope,
            "The contract file may contain only its interface and up to 32 positional DTO records.");
        var dtos = records.Select(ReadDto).ToArray();
        Require(dtos.All(dto => dto.Name is not ("CallOptions" or "BridgeIdentity")), scope,
            "CallOptions and BridgeIdentity are reserved generated TypeScript types.");
        Require(dtos.Select(dto => dto.Name).Distinct(StringComparer.Ordinal).Count() == dtos.Length, scope,
            "DTO names must be unique.");
        var methods = contract.Members.OfType<MethodDeclarationSyntax>().ToArray();
        Require(methods.Length == contract.Members.Count && methods.Length is > 0 and <= 32, contract,
            "Export between one and 32 methods; properties and events are not supported.");
        var operations = methods.Select(method => ReadOperation(method, dtos)).ToArray();
        Require(operations.All(op => ClientName(op.Method) != "__proto__"), contract,
            "The client method name __proto__ is reserved by JavaScript object literals.");
        Require(operations.Select(op => op.Name).Distinct(StringComparer.Ordinal).Count() == operations.Length
            && operations.Select(op => op.Method).Distinct(StringComparer.Ordinal).Count() == operations.Length
            && operations.Select(op => ClientName(op.Method)).Distinct(StringComparer.Ordinal).Count() == operations.Length,
            contract, "Operation, C# method, and generated client method names must be unique; overloads are not supported.");
        var canonical = JsonSerializer.Serialize(new
        {
            protocol = 1,
            namespaceName,
            contract = contract.Identifier.ValueText,
            operations = operations.OrderBy(op => op.Name, StringComparer.Ordinal),
            dtos = dtos.OrderBy(dto => dto.Name, StringComparer.Ordinal)
        });
        var fingerprint = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
        var contractName = contract.Identifier.ValueText;
        var adapter = (contractName.Length > 1 && contractName[0] == 'I' && char.IsUpper(contractName[1])
            ? contractName[1..] : contractName) + "Bridge";
        Require(IsIdentifier(adapter), contract, "The interface name must produce a valid adapter name.");
        Require(dtos.All(dto => dto.Name != adapter && dto.Name != adapter + "Json" && dto.Name != contractName),
            contract, "DTO names must not collide with the interface or generated adapter/context names.");
        var csharp = GenerateCSharp(namespaceName, contract.Identifier.ValueText, adapter, operations, dtos, fingerprint);
        using var stream = typeof(ContractCompiler).Assembly.GetManifestResourceStream("Spar.ContractGenerator.ClientRuntime.js")!;
        using var reader = new StreamReader(stream);
        var javascript = reader.ReadToEnd().Replace("__FINGERPRINT__", fingerprint, StringComparison.Ordinal);
        javascript += "\nexport const backend = Object.freeze({\n";
        foreach (var operation in operations)
        {
            javascript += $"  {ClientName(operation.Method)}: (request, options) => call({Quote(operation.Name)}, request, options),\n";
        }

        javascript += "});\n";
        return new GeneratedContract(csharp, javascript, GenerateTypeScript(operations, dtos), fingerprint);
    }

    private static Dto ReadDto(RecordDeclarationSyntax record)
    {
        Require(record.ClassOrStructKeyword.Kind() != SyntaxKind.StructKeyword && record.Modifiers.Any(SyntaxKind.PublicKeyword)
            && record.Modifiers.Any(SyntaxKind.SealedKeyword) && record.ParameterList is not null
            && record.Modifiers.Count == 2 && IsIdentifier(record.Identifier.ValueText)
            && record.Members.Count == 0 && record.BaseList is null && record.TypeParameterList is null
            && record.AttributeLists.Count == 0, record, "DTOs must be public sealed positional records without custom members or attributes.");
        Require(record.ParameterList!.Parameters.Count is > 0 and <= 16, record, "DTOs support one to 16 scalar fields.");
        var fields = record.ParameterList.Parameters.Select(parameter =>
        {
            var type = parameter.Type?.ToString();
            Require(type is "string" or "string?" or "int" or "bool" && parameter.Default is null
                && parameter.AttributeLists.Count == 0 && parameter.Modifiers.Count == 0, parameter,
                "DTO fields support string, nullable string, int, and bool; no defaults or custom attributes.");
            Require(IsIdentifier(parameter.Identifier.ValueText), parameter, "Use simple DTO field identifiers.");
            return new Field(parameter.Identifier.ValueText, type!);
        }).ToArray();
        Require(fields.Select(field => JsonNamingPolicy.CamelCase.ConvertName(field.Name)).Distinct(StringComparer.Ordinal).Count()
            == fields.Length, record, "JSON field names must be unique.");
        return new Dto(record.Identifier.ValueText, fields);
    }

    private static Operation ReadOperation(MethodDeclarationSyntax method, Dto[] dtos)
    {
        var attributes = method.AttributeLists.SelectMany(list => list.Attributes).ToArray();
        Require(attributes.Length == 1 && attributes[0].Name.ToString() == "SparOperation", method,
            "Every method needs exactly one [SparOperation(\"operation\", \"permission\")] attribute.");
        var arguments = attributes[0].ArgumentList?.Arguments;
        Require(arguments?.Count == 2 && arguments.Value.All(arg => arg.NameColon is null && arg.NameEquals is null
            && arg.Expression is LiteralExpressionSyntax { RawKind: (int)SyntaxKind.StringLiteralExpression }), method,
            "Operation and permission must be explicit string literals.");
        var name = ((LiteralExpressionSyntax)arguments!.Value[0].Expression).Token.ValueText;
        var permission = ((LiteralExpressionSyntax)arguments.Value[1].Expression).Token.ValueText;
        Require(IsWireName(name) && IsWireName(permission), method, "Operation and permission names need one to 64 ASCII letters, digits, dots, or hyphens.");
        var parameters = method.ParameterList.Parameters;
        Require(method.ReturnType is GenericNameSyntax { Identifier.ValueText: "Task", TypeArgumentList.Arguments.Count: 1 }
            && parameters.Count == 2 && parameters[1].Type?.ToString() == "CancellationToken"
            && parameters.All(item => item.Default is null && item.Modifiers.Count == 0 && item.AttributeLists.Count == 0)
            && method.Modifiers.Count == 0 && IsIdentifier(method.Identifier.ValueText)
            && method.TypeParameterList is null && method.Body is null && method.ExpressionBody is null,
            method, "Export Task<TReply> Method(TRequest request, CancellationToken cancellationToken).");
        var request = parameters[0].Type!.ToString();
        var response = ((GenericNameSyntax)method.ReturnType).TypeArgumentList.Arguments[0].ToString();
        Require(dtos.Any(dto => dto.Name == request) && dtos.Any(dto => dto.Name == response), method,
            "Request and reply must be DTO records declared in this contract file.");
        return new Operation(name, permission, method.Identifier.ValueText, request, response);
    }

    private static string GenerateCSharp(string space, string contract, string adapter, Operation[] operations, Dto[] dtos, string hash)
    {
        var code = new StringBuilder($$"""
            // <auto-generated />
            #nullable enable
            using System.Text.Json;
            using System.Text.Json.Serialization;
            using Sotsera.Spar.Bridge;
            namespace {{space}};
            public sealed class {{adapter}}({{contract}} backend) : ISparBridgeDispatcher
            {
                public string Fingerprint => "{{hash}}";
                public bool TryGetPermission(string operation, out string permission)
                {
                    permission = operation switch
                    {
            """);
        code.AppendLine();
        foreach (var op in operations)
        {
            code.AppendLine(CultureInfo.InvariantCulture, $"            {Quote(op.Name)} => {Quote(op.Permission)},");
        }

        code.AppendLine("            _ => string.Empty\n        };\n        return permission.Length != 0;\n    }");
        code.AppendLine("    public async ValueTask<JsonElement> InvokeAsync(string operation, JsonElement payload, CancellationToken cancellationToken)\n    {\n        switch (operation)\n        {");
        foreach (var op in operations)
        {
            code.AppendLine(CultureInfo.InvariantCulture, $"            case {Quote(op.Name)}:\n            {{\n                {op.Request} request;\n                try\n                {{");
            code.AppendLine(CultureInfo.InvariantCulture, $"                    request = JsonSerializer.Deserialize(payload, {adapter}Json.Default.{op.Request}) ?? throw new JsonException(\"Null request.\");\n                }}\n                catch (JsonException error)\n                {{\n                    throw new SparPayloadException(\"Invalid request payload.\", error);\n                }}");
            AppendValidation(code, dtos.Single(dto => dto.Name == op.Request), "request", "RequestString");
            code.AppendLine(CultureInfo.InvariantCulture, $"                var response = await backend.{op.Method}(request, cancellationToken).ConfigureAwait(false);\n                ArgumentNullException.ThrowIfNull(response);");
            AppendValidation(code, dtos.Single(dto => dto.Name == op.Response), "response", "ResponseString");
            code.AppendLine(CultureInfo.InvariantCulture, $"                return JsonSerializer.SerializeToElement(response, {adapter}Json.Default.{op.Response});\n            }}");
        }

        code.AppendLine("            default: throw new InvalidOperationException(\"Unknown generated operation.\");\n        }\n    }\n}");
        code.AppendLine("[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, MaxDepth = 16, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, RespectNullableAnnotations = true, RespectRequiredConstructorParameters = true)]");
        foreach (var dto in dtos)
        {
            code.AppendLine(CultureInfo.InvariantCulture, $"[JsonSerializable(typeof({dto.Name}))]");
        }

        code.AppendLine(CultureInfo.InvariantCulture, $"internal sealed partial class {adapter}Json : JsonSerializerContext;");
        return code.ToString();
    }

    private static void AppendValidation(StringBuilder code, Dto dto, string value, string method)
    {
        foreach (var field in dto.Fields.Where(field => field.Type.StartsWith("string", StringComparison.Ordinal)))
        {
            code.AppendLine(CultureInfo.InvariantCulture, $"                SparContractValidation.{method}({value}.{field.Name}, {(field.Type.EndsWith('?') ? "true" : "false")});");
        }
    }

    private static string GenerateTypeScript(Operation[] operations, Dto[] dtos)
    {
        var code = new StringBuilder("// Generated from the C# contract. Do not edit.\n");
        code.AppendLine("export interface CallOptions { signal?: AbortSignal; timeoutMs?: number }\nexport interface BridgeIdentity { version: 1; contract: string; client: string; document: string }\nexport declare function connect(): Promise<BridgeIdentity>;");
        foreach (var dto in dtos)
        {
            code.AppendLine(CultureInfo.InvariantCulture, $"export interface {dto.Name} {{");
            foreach (var field in dto.Fields)
            {
                var type = field.Type switch
                {
                    "string" => "string",
                    "string?" => "string | null",
                    "int" => "number",
                    "bool" => "boolean",
                    _ => throw new InvalidOperationException()
                };
                code.AppendLine(CultureInfo.InvariantCulture, $"  {JsonNamingPolicy.CamelCase.ConvertName(field.Name)}: {type};");
            }

            code.AppendLine("}");
        }

        code.AppendLine("export declare const backend: {");
        foreach (var op in operations)
        {
            code.AppendLine(CultureInfo.InvariantCulture, $"  {ClientName(op.Method)}(request: {op.Request}, options?: CallOptions): Promise<{op.Response}>;");
        }

        code.AppendLine("};");
        return code.ToString();
    }

    private static string ClientName(string method)
    {
        return JsonNamingPolicy.CamelCase.ConvertName(method.EndsWith("Async", StringComparison.Ordinal) ? method[..^5] : method);
    }

    private static bool IsIdentifier(string name)
    {
        return SyntaxFacts.IsValidIdentifier(name) && !name.StartsWith('@') && SyntaxFacts.GetKeywordKind(name) == SyntaxKind.None;
    }

    private static bool IsWireName(string value)
    {
        return value.Length is > 0 and <= 64 && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '-');
    }

    private static string Quote(string value)
    {
        return JsonSerializer.Serialize(value);
    }

    private static void Require(bool condition, SyntaxNode node, string message)
    {
        if (!condition)
        {
            var location = node.GetLocation().GetLineSpan();
            throw new InvalidOperationException($"{location.Path}({location.StartLinePosition.Line + 1},{location.StartLinePosition.Character + 1}): error SPAR001: {message}");
        }
    }

    private sealed record Field(string Name, string Type);
    private sealed record Dto(string Name, Field[] Fields);
    private sealed record Operation(string Name, string Permission, string Method, string Request, string Response);
}
