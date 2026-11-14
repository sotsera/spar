// Prints the public surface of selected types in an assembly, for the RESULTS evidence.
// Usage: Inspect <assembly.dll> <typeNameFilter>...   (a filter matches a type whose full name contains it)
using System.Reflection;
using System.Runtime.Loader;
using System.Text;

if (args.Length < 1)
{
    Console.Error.WriteLine("Usage: Inspect <assembly.dll> [typeNameFilter...]");
    return 2;
}

var path = Path.GetFullPath(args[0]);
var directory = Path.GetDirectoryName(path)!;
var context = new AssemblyLoadContext("inspect", isCollectible: true);
context.Resolving += (ctx, name) =>
{
    var candidate = Path.Combine(directory, name.Name + ".dll");
    return File.Exists(candidate) ? ctx.LoadFromAssemblyPath(candidate) : null;
};

var assembly = context.LoadFromAssemblyPath(path);
var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
Console.WriteLine($"# {assembly.GetName().Name} {assembly.GetName().Version} ({informational})");
Console.WriteLine($"# {path}");

Type[] types;
try
{
    types = assembly.GetExportedTypes();
}
catch (ReflectionTypeLoadException ex)
{
    types = [.. ex.Types.Where(t => t is not null).Cast<Type>()];
}

var filters = args.Skip(1).ToArray();
foreach (var type in types.OrderBy(t => t.FullName, StringComparer.Ordinal))
{
    if (filters.Length > 0 && !filters.Any(f => type.FullName!.Contains(f, StringComparison.Ordinal)))
    {
        continue;
    }

    Console.WriteLine();
    Console.WriteLine(Describe(type));
    const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
    foreach (var member in type.GetMembers(flags).OrderBy(m => m.MemberType).ThenBy(m => m.Name, StringComparer.Ordinal))
    {
        var line = member switch
        {
            ConstructorInfo c => $"  ctor({Parameters(c.GetParameters())})",
            MethodInfo m when !m.IsSpecialName => $"  {(m.IsStatic ? "static " : "")}{Name(m.ReturnType)} {m.Name}({Parameters(m.GetParameters())})",
            MethodInfo m when m.Name.StartsWith("op_", StringComparison.Ordinal) => $"  static {Name(m.ReturnType)} {m.Name}({Parameters(m.GetParameters())})",
            PropertyInfo p => $"  {(p.GetMethod?.IsStatic == true ? "static " : "")}{Name(p.PropertyType)} {p.Name} {{ {(p.GetMethod?.IsPublic == true ? "get; " : "")}{(p.SetMethod?.IsPublic == true ? "set; " : "")}}}",
            FieldInfo f => $"  {(f.IsStatic ? "static " : "")}{Name(f.FieldType)} {f.Name}",
            EventInfo e => $"  event {Name(e.EventHandlerType!)} {e.Name}",
            _ => null,
        };
        if (line is not null)
        {
            Console.WriteLine(line);
        }
    }
}

context.Unload();
return 0;

static string Describe(Type type)
{
    var kind = type.IsInterface ? "interface" : type.IsEnum ? "enum" : type.IsValueType ? "struct" : type.IsAbstract && type.IsSealed ? "static class" : type.IsAbstract ? "abstract class" : type.IsSealed ? "sealed class" : "class";
    var sb = new StringBuilder($"{kind} {Name(type)}");
    var bases = new List<string>();
    if (type.BaseType is { } b && b != typeof(object) && b.FullName != "System.ValueType" && b.FullName != "System.Enum")
    {
        bases.Add(Name(b));
    }

    bases.AddRange(type.GetInterfaces().Select(Name));
    if (bases.Count > 0)
    {
        sb.Append(" : ").Append(string.Join(", ", bases));
    }

    foreach (var attribute in type.CustomAttributes)
    {
        sb.Append($"  [{attribute.AttributeType.Name}({string.Join(", ", attribute.ConstructorArguments.Select(a => a.Value?.ToString()))}{string.Concat(attribute.NamedArguments.Select(a => $", {a.MemberName}={a.TypedValue.Value}"))})]");
    }

    return sb.ToString();
}

static string Parameters(ParameterInfo[] parameters) =>
    string.Join(", ", parameters.Select(p => $"{Name(p.ParameterType)} {p.Name}"));

static string Name(Type type)
{
    if (type.IsGenericType)
    {
        var name = type.Name[..type.Name.IndexOf('`', StringComparison.Ordinal)];
        return $"{name}<{string.Join(", ", type.GetGenericArguments().Select(Name))}>";
    }

    return type.Namespace is "System" or "System.Collections" or "System.Collections.Generic" or "System.Diagnostics"
        ? type.Name
        : type.FullName ?? type.Name;
}
