using System.Globalization;
using Spar.Hosting;
using Spar.Linux.Interop;

namespace Spar.Linux;

/// <summary>
/// What the host changes in the environment for the engine, on the process's main thread before GTK, WebKit or any
/// other native thread starts, through libc (<c>unsetenv</c>, <c>setenv</c>), which WebKit's and JavaScriptCore's
/// <c>getenv</c> and WebKit's child processes see. Two steps:
/// <list type="number">
/// <item><see cref="Filter"/>, before WebKit's libraries are loaded (plan section 3c, "Browser environment overrides
/// are ignored in Production"): the variables that turn WebKit's safety off (<see cref="Overrides"/>: the inspector
/// servers and the sandbox switch) are removed in every build unless the app calls
/// <c>AllowBrowserEnvironmentOverrides()</c>; and every inherited JavaScriptCore option (<c>JSC_*</c>, which
/// JavaScriptCore reads in this process and in each web process) is removed from a Production build unless the app calls
/// it, while a Development build keeps them for debugging (user decision 2026-11-20), and so is <c>GTK_DEBUG</c>, whose
/// <c>interactive</c> opens GTK's inspector at start (user decision 2026-11-20). Everything else passes, the GPU
/// workarounds in particular. Each variable removed or let through is named in the log, never its value;</item>
/// <item><see cref="Apply"/>, once the page security is resolved: <c>JSC_useWasm</c>, <c>false</c> unless the app's
/// page security allows WebAssembly (decision "WebAssembly off on Linux"), always written, so the app's C# decides it
/// in every build and with every option. Without WebAssembly the global is absent in the page, its frames and every
/// worker.</item>
/// </list>
/// .NET keeps its own copy of the environment, taken when the process first read it, so a removed variable is also
/// removed from that copy: <c>Environment.GetEnvironmentVariable</c> and processes .NET starts then agree with native
/// code.
/// </summary>
internal static class EngineEnvironment
{
    public const string WebAssemblyVariable = "JSC_useWasm";

    /// <summary>The prefix of JavaScriptCore's options in the environment (<c>JSC_&lt;option&gt;</c>).</summary>
    public const string JscPrefix = "JSC_";

    /// <summary>
    /// GTK's debugging flags, read by GTK itself at its start: <c>interactive</c> opens GTK's inspector, which can change
    /// any object of the process, so it is treated as JavaScriptCore's options are.
    /// </summary>
    public const string GtkDebugVariable = "GTK_DEBUG";

    /// <summary>The longest part of an inherited <c>JSC_useWasm</c> value the log repeats.</summary>
    private const int MaxLoggedValue = 100;

    /// <summary>
    /// The variables WebKitGTK reads to turn its safety off (plan section 3c): a remote inspector server any local
    /// program can connect to (<c>WEBKIT_INSPECTOR_SERVER</c>, and its HTTP form for a browser), and the web processes
    /// run without their sandbox. Every other <c>WEBKIT_*</c> variable passes.
    /// </summary>
    public static string[] Overrides() =>
    [
        "WEBKIT_INSPECTOR_SERVER",
        "WEBKIT_INSPECTOR_HTTP_SERVER",
        "WEBKIT_DISABLE_SANDBOX_THIS_IS_DANGEROUS",
    ];

    /// <summary>
    /// Removes the variables <see cref="Decide"/> removes and logs each decision; a problem when one could not be removed,
    /// and the app does not start. Runs once, before WebKit's libraries are loaded.
    /// </summary>
    public static string? Filter(bool allowed, bool development, HostLog log)
    {
        foreach (var (name, remove, line) in Decide(Libc.VariableNames(), allowed, development))
        {
            if (remove)
            {
                if (Libc.UnsetEnv(name) != 0 || Libc.Variable(name) is not null)
                {
                    return $"{name} could not be removed from the environment, so the browser would start with it.";
                }

                // .NET's copy too, which its Process.Start hands to children and Environment.GetEnvironmentVariable reads.
                Environment.SetEnvironmentVariable(name, null);
            }

            log.Warn(line);
        }

        return null;
    }

    /// <summary>
    /// For each name in <paramref name="names"/> the filter acts on: whether it is removed, and its log line. A listed
    /// override goes unless <paramref name="allowed"/>; a <c>JSC_*</c> option, and <c>GTK_DEBUG</c>, go unless
    /// <paramref name="allowed"/> or <paramref name="development"/>; <c>JSC_useWasm</c> is not here, since
    /// <see cref="Apply"/> always writes it; any other name is not listed. In the order of <paramref name="names"/>, each
    /// name once.
    /// </summary>
    internal static List<(string Name, bool Remove, string Line)> Decide(IEnumerable<string> names, bool allowed, bool development)
    {
        var overrides = Overrides();
        var decided = new List<(string Name, bool Remove, string Line)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in names)
        {
            if (!seen.Add(name))
            {
                continue;
            }

            if (Array.IndexOf(overrides, name) >= 0)
            {
                decided.Add(allowed
                    ? (name, false, $"{name} in force (AllowBrowserEnvironmentOverrides).")
                    : (name, true, $"{name} ignored (AllowBrowserEnvironmentOverrides allows it)."));
            }
            else if (name.StartsWith(JscPrefix, StringComparison.Ordinal) && name != WebAssemblyVariable)
            {
                decided.Add((allowed, development) switch
                {
                    (true, _) => (name, false, $"{name} in force (AllowBrowserEnvironmentOverrides): a JavaScriptCore option, in this app and its web processes."),
                    (false, true) => (name, false, $"{name} in force (a Development build keeps JavaScriptCore options): in this app and its web processes."),
                    _ => (name, true, $"{name} ignored: a JavaScriptCore option (AllowBrowserEnvironmentOverrides allows it)."),
                });
            }
            else if (name == GtkDebugVariable)
            {
                decided.Add((allowed, development) switch
                {
                    (true, _) => (name, false, $"{name} in force (AllowBrowserEnvironmentOverrides): GTK's debugging flags, its inspector included."),
                    (false, true) => (name, false, $"{name} in force (a Development build keeps GTK's debugging flags): its inspector included."),
                    _ => (name, true, $"{name} ignored: GTK's debugging flags, its inspector included (AllowBrowserEnvironmentOverrides allows it)."),
                });
            }
        }

        return decided;
    }

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

        // .NET's copy agrees with native code, as for the variables the filter removed.
        Environment.SetEnvironmentVariable(WebAssemblyVariable, value);
        log.Info(Describe(allowWebAssembly, inherited));
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

    private static string Cut(string value) =>
        value.Length <= MaxLoggedValue
            ? value
            : string.Concat(value.AsSpan(0, MaxLoggedValue), "… (", value.Length.ToString(CultureInfo.InvariantCulture), " characters)");
}
