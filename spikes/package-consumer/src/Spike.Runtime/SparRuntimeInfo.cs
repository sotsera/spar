using System.Reflection;

namespace Spike;

/// <summary>The package version of the runtime library, so a consumer can show which package it runs.</summary>
public static class SparRuntimeInfo
{
    public static string Version { get; } =
        typeof(SparRuntimeInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? "unknown";
}
