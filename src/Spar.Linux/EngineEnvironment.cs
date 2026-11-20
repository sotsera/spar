using System.Collections;
using System.Globalization;
using Spar.Hosting;
using Spar.Linux.Interop;

namespace Spar.Linux;

/// <summary>
/// What the host sets in the environment for the engine, on the process's main thread before GTK and WebKit start,
/// through libc <c>setenv</c>, which JavaScriptCore's <c>getenv</c> and WebKit's child processes see (decision
/// "WebAssembly off on Linux"): <c>JSC_useWasm</c>, <c>false</c> unless the app's page security allows WebAssembly.
/// It is always written, replacing an inherited value, so the app's C# decides it and the user's environment never
/// does, <c>AllowBrowserEnvironmentOverrides()</c> or not. Without WebAssembly the global is absent in the page, its
/// frames and every worker; the policy, without <c>'wasm-unsafe-eval'</c>, would refuse modules anyway. JavaScriptCore
/// reads every other <c>JSC_</c> variable too, in this process and in the web processes: the ignored-environment rule
/// names exact variables (plan section 3c), so these pass, and each is named in the log.
/// </summary>
internal static class EngineEnvironment
{
    public const string WebAssemblyVariable = "JSC_useWasm";

    private const string JscPrefix = "JSC_";

    /// <summary>The longest part of an inherited value the log repeats.</summary>
    private const int MaxLoggedValue = 100;

    /// <summary>Sets <c>JSC_useWasm</c> and logs what was done; a problem when it could not be set, and the app does not start.</summary>
    public static string? Apply(bool allowWebAssembly, HostLog log)
    {
        var inherited = Libc.Variable(WebAssemblyVariable);
        var value = Value(allowWebAssembly);
        if (Libc.SetEnv(WebAssemblyVariable, value, overwrite: 1) != 0 || Libc.Variable(WebAssemblyVariable) != value)
        {
            return $"{WebAssemblyVariable} could not be set to {value} in the environment, so WebAssembly in the page " +
                "would not be what the app declared.";
        }

        log.Info(Describe(allowWebAssembly, inherited));
        foreach (var (name, other) in OtherJscVariables())
        {
            log.Warn($"{name}={Cut(other)} from the environment reaches JavaScriptCore in this app and its web processes; Spar lets it through.");
        }

        return null;
    }

    /// <summary>The value of <c>JSC_useWasm</c> for the app's declaration.</summary>
    internal static string Value(bool allowWebAssembly) => allowWebAssembly ? "true" : "false";

    /// <summary>The start-up line: the setting, why, and an inherited value that was replaced.</summary>
    internal static string Describe(bool allowWebAssembly, string? inherited)
    {
        var value = Value(allowWebAssembly);
        var setting = allowWebAssembly
            ? $"WebAssembly on (AllowWebAssembly()): {WebAssemblyVariable}={value}"
            : $"WebAssembly off: {WebAssemblyVariable}={value}";
        return inherited is null || inherited == value
            ? $"{setting}."
            : $"{setting}, replacing the inherited {WebAssemblyVariable}={Cut(inherited)}.";
    }

    /// <summary>The <c>JSC_</c> variables the process inherited, other than <c>JSC_useWasm</c>, by name.</summary>
    private static List<(string Name, string Value)> OtherJscVariables()
    {
        var found = new List<(string Name, string Value)>();
        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            if (entry.Key is string name
                && name.StartsWith(JscPrefix, StringComparison.Ordinal)
                && name != WebAssemblyVariable)
            {
                found.Add((name, entry.Value as string ?? ""));
            }
        }

        found.Sort((left, right) => string.CompareOrdinal(left.Name, right.Name));
        return found;
    }

    private static string Cut(string value) =>
        value.Length <= MaxLoggedValue
            ? value
            : string.Concat(value.AsSpan(0, MaxLoggedValue), "… (", value.Length.ToString(CultureInfo.InvariantCulture), " characters)");
}
