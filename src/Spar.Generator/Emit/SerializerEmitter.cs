using System.Collections.Generic;
using System.Linq;

namespace Spar.Generator.Emit;

/// <summary>
/// Writes <c>SparSerializers</c>: one reader and one writer per wire type, against <c>Utf8JsonReader</c> and
/// <c>Utf8JsonWriter</c> through <c>Spar.Runtime.SparJson</c>. Every type gets a named method, so lists and nullable
/// values compose without lambdas.
/// </summary>
internal static class SerializerEmitter
{
    private const string Json = "global::Spar.Runtime.SparJson";
    private const string Reader = "ref global::System.Text.Json.Utf8JsonReader reader";
    private const string Writer = "global::System.Text.Json.Utf8JsonWriter writer";
    private const string TokenType = "global::System.Text.Json.JsonTokenType";

    public static void Emit(CodeWriter code, IReadOnlyList<TypeRef> types, IReadOnlyList<RecordModel> records, IReadOnlyList<ErrorModel> errors, IReadOnlyList<EnumModel> enums)
    {
        code.Open("internal static class SparSerializers");
        foreach (var type in types)
        {
            EmitType(code, type, records, enums);
        }

        foreach (var error in errors)
        {
            EmitError(code, error);
        }

        code.Close();
    }

    private static void EmitType(CodeWriter code, TypeRef type, IReadOnlyList<RecordModel> records, IReadOnlyList<EnumModel> enums)
    {
        if (type.Kind == WireKind.Record)
        {
            EmitRecord(code, records.First(r => r.Key == type.Key));
            return;
        }

        if (type.Kind == WireKind.Enum)
        {
            EmitEnum(code, enums.First(e => e.Key == type.Key));
            return;
        }

        code.Line($"public static {type.CSharp} Read{type.Key}({Reader}) =>");
        code.Line($"    {ReadExpression(type)};");
        code.Line();
        code.Open($"public static void Write{type.Key}({Writer}, {type.CSharp} value)");
        foreach (var line in WriteStatements(type))
        {
            code.Line(line);
        }

        code.Close();
        code.Line();
    }

    private static string ReadExpression(TypeRef type) => type.Kind switch
    {
        WireKind.String => $"{Json}.ReadString(ref reader)",
        WireKind.Boolean => $"{Json}.ReadBoolean(ref reader)",
        WireKind.Byte or WireKind.SByte or WireKind.Int16 or WireKind.UInt16 or WireKind.Int32 or WireKind.UInt32
            or WireKind.Int64 or WireKind.UInt64 or WireKind.Single or WireKind.Double or WireKind.Decimal or WireKind.Char
            or WireKind.Guid or WireKind.DateTime or WireKind.DateTimeOffset or WireKind.DateOnly or WireKind.TimeOnly
            or WireKind.TimeSpan or WireKind.Bytes => $"{Json}.Read{type.Kind}(ref reader)",
        WireKind.List when type.Key.StartsWith("ArrayOf", System.StringComparison.Ordinal) => $"[.. {Json}.ReadList(ref reader, Read{type.Element!.Key})]",
        WireKind.List => $"{Json}.ReadList(ref reader, Read{type.Element!.Key})",
        WireKind.Nullable => $"reader.TokenType == {TokenType}.Null ? null : Read{type.Element!.Key}(ref reader)",
        _ => $"Read{type.Key}(ref reader)",
    };

    private static IEnumerable<string> WriteStatements(TypeRef type)
    {
        switch (type.Kind)
        {
            case WireKind.String:
            case WireKind.DateTime:
            case WireKind.DateTimeOffset:
                yield return "writer.WriteStringValue(value);";
                break;
            case WireKind.Boolean:
                yield return "writer.WriteBooleanValue(value);";
                break;
            case WireKind.Byte:
            case WireKind.SByte:
            case WireKind.Int16:
            case WireKind.UInt16:
                yield return "writer.WriteNumberValue((int)value);";
                break;
            case WireKind.Int32:
            case WireKind.UInt32:
                yield return "writer.WriteNumberValue(value);";
                break;
            case WireKind.Single:
                // NaN and infinities are a writer failure (wire rules), reported as response / serialization.
                yield return "if (!float.IsFinite(value)) throw new global::System.InvalidOperationException(\"A float must be finite.\");";
                yield return "writer.WriteNumberValue(value);";
                break;
            case WireKind.Double:
                yield return "if (!double.IsFinite(value)) throw new global::System.InvalidOperationException(\"A double must be finite.\");";
                yield return "writer.WriteNumberValue(value);";
                break;
            case WireKind.Bytes:
                yield return "writer.WriteBase64StringValue(value);";
                break;
            case WireKind.Int64:
            case WireKind.UInt64:
            case WireKind.Decimal:
            case WireKind.Char:
            case WireKind.Guid:
            case WireKind.DateOnly:
            case WireKind.TimeOnly:
            case WireKind.TimeSpan:
                yield return $"{Json}.Write{type.Kind}(writer, value);";
                break;
            case WireKind.List:
                yield return $"{Json}.WriteList(writer, value, Write{type.Element!.Key});";
                break;
            case WireKind.Nullable:
                var inner = type.IsValueType ? "value.Value" : "value";
                yield return "if (value is null)";
                yield return "{";
                yield return "    writer.WriteNullValue();";
                yield return "    return;";
                yield return "}";
                yield return "";
                yield return $"Write{type.Element!.Key}(writer, {inner});";
                break;
        }
    }

    private static void EmitRecord(CodeWriter code, RecordModel record)
    {
        code.Open($"public static {record.CSharp} Read{record.Key}({Reader})");
        EmitReadFields(code, record.Properties.Items, skipCode: false);
        code.Line($"return new {record.CSharp}({string.Join(", ", record.Properties.Select(Local))});");
        code.Close();
        code.Line();

        code.Open($"public static void Write{record.Key}({Writer}, {record.CSharp} value)");
        code.Line("writer.WriteStartObject();");
        EmitWriteFields(code, record.Properties.Items, "value");
        code.Line("writer.WriteEndObject();");
        code.Close();
        code.Line();
    }

    /// <summary>Reads an object's declared fields into locals: duplicates rejected, unknown ones skipped, required ones checked.</summary>
    private static void EmitReadFields(CodeWriter code, IReadOnlyList<PropertyModel> properties, bool skipCode)
    {
        code.Line($"{Json}.Expect(ref reader, {TokenType}.StartObject);");
        foreach (var property in properties)
        {
            code.Line($"{property.Type.CSharp} {Local(property)} = default!;");
            code.Line($"var has{property.Name} = false;");
        }

        code.Open($"while ({Json}.NextProperty(ref reader))");
        if (skipCode)
        {
            code.Open("if (reader.ValueTextEquals(\"code\"u8))");
            code.Line($"{Json}.SkipValue(ref reader);");
            code.Line("continue;");
            code.Close();
            code.Line();
        }

        foreach (var property in properties)
        {
            code.Open($"if (reader.ValueTextEquals(\"{property.JsonName}\"u8))");
            code.Open($"if (has{property.Name})");
            code.Line($"{Json}.Duplicate(ref reader);");
            code.Close();
            code.Line();
            code.Line($"has{property.Name} = true;");
            code.Line("reader.Read();");
            code.Line($"{Local(property)} = Read{property.Type.Key}(ref reader);");
            code.Line("continue;");
            code.Close();
            code.Line();
        }

        code.Line($"{Json}.SkipValue(ref reader);");
        code.Close();
        code.Line();

        // Requests accept a missing or null optional value; a non-nullable value is required (wire rules).
        foreach (var property in properties.Where(p => p.Type.Kind != WireKind.Nullable))
        {
            code.Open($"if (!has{property.Name})");
            code.Line($"throw {Json}.Missing(ref reader, \"{property.JsonName}\");");
            code.Close();
            code.Line();
        }
    }

    private static void EmitWriteFields(CodeWriter code, IReadOnlyList<PropertyModel> properties, string owner)
    {
        foreach (var property in properties)
        {
            code.Line($"writer.WritePropertyName(\"{property.JsonName}\"u8);");
            code.Line($"Write{property.Type.Key}(writer, {owner}.{property.Name});");
        }
    }

    /// <summary>
    /// One member name, exactly (wire rules). A value that is no member, such as a cast integer, is a writer failure;
    /// <c>@</c> lets a member named like a keyword be written.
    /// </summary>
    private static void EmitEnum(CodeWriter code, EnumModel model)
    {
        code.Open($"public static {model.CSharp} Read{model.Key}({Reader})");
        code.Line($"var name = {Json}.ReadString(ref reader);");
        code.Line("return name switch");
        code.Line("{");
        foreach (var member in model.Members)
        {
            code.Line($"    \"{member}\" => {model.CSharp}.@{member},");
        }

        code.Line($"    _ => throw {Json}.Error(ref reader, \"Not a member of {model.Name}\"),");
        code.Line("};");
        code.Close();
        code.Line();
        code.Open($"public static void Write{model.Key}({Writer}, {model.CSharp} value)");
        code.Line("writer.WriteStringValue(value switch");
        code.Line("{");
        foreach (var member in model.Members)
        {
            code.Line($"    {model.CSharp}.@{member} => \"{member}\",");
        }

        code.Line($"    _ => throw new global::System.InvalidOperationException(\"The value is not a member of {model.Name}.\"),");
        code.Line("});");
        code.Close();
        code.Line();
    }

    /// <summary>Errors are <c>{"code":"…", …payload}</c>. The reader finds the code first, then reads that case.</summary>
    private static void EmitError(CodeWriter code, ErrorModel error)
    {
        code.Open($"public static {error.CSharp} Read{error.Key}({Reader})");
        code.Line($"{Json}.Expect(ref reader, {TokenType}.StartObject);");
        code.Line("var start = reader;");
        code.Line("string? code = null;");
        code.Open($"while ({Json}.NextProperty(ref reader))");
        code.Open("if (reader.ValueTextEquals(\"code\"u8))");
        code.Line("reader.Read();");
        code.Line($"code = {Json}.ReadString(ref reader);");
        code.Line("continue;");
        code.Close();
        code.Line();
        code.Line($"{Json}.SkipValue(ref reader);");
        code.Close();
        code.Line();
        code.Line("var end = reader;");
        code.Line("reader = start;");
        code.Line($"{error.CSharp} value = code switch");
        code.Line("{");
        for (var i = 0; i < error.Cases.Length; i++)
        {
            code.Line($"    \"{error.Cases.Items[i].Code}\" => Read{error.Key}Case{i}(ref reader),");
        }

        code.Line($"    _ => throw {Json}.Error(ref reader, \"Unknown error code '\" + code + \"'\"),");
        code.Line("};");
        code.Line("reader = end;");
        code.Line("return value;");
        code.Close();
        code.Line();

        for (var i = 0; i < error.Cases.Length; i++)
        {
            var @case = error.Cases.Items[i];
            code.Open($"private static {error.CSharp} Read{error.Key}Case{i}({Reader})");
            EmitReadFields(code, @case.Properties.Items, skipCode: true);
            code.Line($"return new {@case.CSharp}({string.Join(", ", @case.Properties.Select(Local))});");
            code.Close();
            code.Line();
        }

        code.Open($"public static void Write{error.Key}({Writer}, {error.CSharp} value)");
        code.Line("writer.WriteStartObject();");
        code.Open("switch (value)");
        for (var i = 0; i < error.Cases.Length; i++)
        {
            var @case = error.Cases.Items[i];
            code.Line($"case {@case.CSharp} case{i}:");
            code.Line($"    writer.WriteString(\"code\"u8, \"{@case.Code}\"u8);");
            foreach (var property in @case.Properties)
            {
                code.Line($"    writer.WritePropertyName(\"{property.JsonName}\"u8);");
                code.Line($"    Write{property.Type.Key}(writer, case{i}.{property.Name});");
            }

            code.Line("    break;");
        }

        code.Line("default:");
        code.Line("    throw new global::System.InvalidOperationException(\"Unknown error case \" + value.GetType().Name + \".\");");
        code.Close();
        code.Line();
        code.Line("writer.WriteEndObject();");
        code.Close();
        code.Line();
    }

    private static string Local(PropertyModel property) => "@" + Naming.CamelCase(property.Name);
}
