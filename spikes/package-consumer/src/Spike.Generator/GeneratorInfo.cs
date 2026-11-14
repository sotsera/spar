using System.Reflection;

namespace Spike.Generator;

/// <summary>The package version this generator was built as, stamped into generated C# and TypeScript.</summary>
public static class GeneratorInfo
{
    public static string Version { get; } =
        typeof(GeneratorInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? "unknown";
}
