using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace WireShapes.Tests;

/// <summary>
/// Records the M1 budget measurements (plan, "Provisional budgets"): each as its median, range and count, written to
/// <c>artifacts/test-results/&lt;project&gt;/budgets.txt</c> with the build configuration and the runtime. Times vary with
/// the machine, so they are recorded and compared at each slice exit, not asserted. Compiled into this project and
/// linked into <c>WireShapes.EndToEndTests</c>.
/// </summary>
internal static class Budgets
{
    private static readonly Lock Gate = new();
    private static bool s_started;

    /// <summary>Median, range and count of <paramref name="milliseconds"/>, for example "0.41 ms (0.30–0.88, n = 19)".</summary>
    public static string Summary(IReadOnlyList<double> milliseconds)
    {
        var sorted = milliseconds.Order().ToArray();
        var middle = sorted.Length / 2;
        var median = sorted.Length % 2 == 1 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2;
        return string.Create(CultureInfo.InvariantCulture, $"{median:0.00} ms ({sorted[0]:0.00}–{sorted[^1]:0.00}, n = {sorted.Length})");
    }

    /// <summary>Adds one line to this run's file; the first line of a test process starts the file again.</summary>
    public static void Record(string measure, string result)
    {
        var line = $"{measure}: {result} [{Configuration()}, {Runtime()}]";
        TestContext.Current.SendDiagnosticMessage(line);

        var folder = Path.Combine(ResultsRoot(), Assembly.GetExecutingAssembly().GetName().Name!);
        var file = Path.Combine(folder, "budgets.txt");
        lock (Gate)
        {
            Directory.CreateDirectory(folder);
            if (!s_started)
            {
                File.WriteAllText(file, $"Recorded {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}\n");
                s_started = true;
            }

            File.AppendAllText(file, line + "\n");
        }
    }

    private static string Configuration() =>
        Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyConfigurationAttribute>()?.Configuration ?? "unknown";

    // PublishAot makes IsDynamicCodeSupported false even under the JIT (docs/pitfalls.md); this one tells them apart.
    private static string Runtime() => RuntimeFeature.IsDynamicCodeCompiled ? "CoreCLR" : "NativeAOT";

    /// <summary>
    /// The results root: SPAR_TEST_RESULTS when the runner names it (Windows Sandbox's runner in its guest, CI), otherwise
    /// the repository's <c>artifacts/test-results/</c> above the tests' output (the folder beside <c>global.json</c>).
    /// </summary>
    private static string ResultsRoot()
    {
        if (Environment.GetEnvironmentVariable("SPAR_TEST_RESULTS") is { Length: > 0 } named && Path.IsPathFullyQualified(named))
        {
            return named;
        }

        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "global.json")) && Directory.Exists(Path.Combine(directory.FullName, "artifacts")))
            {
                return Path.Combine(directory.FullName, "artifacts", "test-results");
            }
        }

        throw new InvalidOperationException("These tests write their budgets under the repository's artifacts/, or under SPAR_TEST_RESULTS.");
    }
}
