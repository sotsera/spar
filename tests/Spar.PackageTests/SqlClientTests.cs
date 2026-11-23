using System.Net;
using System.Net.Sockets;

namespace Spar.PackageTests;

/// <summary>
/// The publishing-choices guide's worked example (plan, M1d step 7; decision "M1d step 7's shape: runtime choices"): a
/// new app that uses Microsoft.Data.SqlClient 7.1.2 and publishes with NativeAOT. Without the guide's block that accepts
/// its warnings the publish fails on them; with it (and the guide's culture and single-file blocks) the publish passes,
/// shows them as warnings, and is the executable alone, which reaches the network: an <c>Open</c> against a loopback port
/// where nothing listens throws <c>SqlException</c>, not <c>DllNotFoundException</c>, and in invariant mode
/// <c>NotSupportedException</c>. On Windows, without the single-file block, the executable alone needs SqlClient's native
/// SNI library. No server: the probe is the consumer's own code, a command-line mode of its <c>Program.cs</c>.
/// </summary>
public sealed class SqlClientTests(Consumer consumer) : IDisposable
{
    private const string App = "Ledger";

    /// <summary>The probe's call, before the template's <c>SparApplication.Create</c>: the probe opens no window.</summary>
    private const string ProbeCall = """
        // The package tests' probe (no window): what SqlClient throws for a port where nothing listens.
        if (args is ["--sql-probe", var port])
        {
            return SqlProbe.Run(port);
        }

        """;

    private const string Probe = """
        using Microsoft.Data.SqlClient;

        namespace Ledger;

        /// <summary>Opens a connection to a loopback port where nothing listens and prints what it threw: 0 for SqlException.</summary>
        internal static class SqlProbe
        {
            public static int Run(string port)
            {
                try
                {
                    using var connection = new SqlConnection($"Server=tcp:127.0.0.1,{port};Connect Timeout=3;Encrypt=false;Pooling=false");
                    connection.Open();
                    Console.WriteLine("probe: opened");
                    return 1;
                }
                catch (Exception problem)
                {
                    Console.WriteLine($"probe: {problem.GetType().FullName}: {problem.Message}");
                    for (var inner = problem.InnerException; inner is not null; inner = inner.InnerException)
                    {
                        Console.WriteLine($"probe: inner {inner.GetType().FullName}: {inner.Message}");
                    }

                    return problem is SqlException ? 0 : 2;
                }
            }
        }

        """;

    private static readonly string[] Recipe =
        [GuideSnippets.SqlClientPackage, GuideSnippets.AcceptAotWarnings, GuideSnippets.Globalization, GuideSnippets.SqlClientSingleFile];

    private string Executable => OperatingSystem.IsWindows() ? $"{App}.exe" : App;

    [Fact]
    public async Task Without_its_warnings_accepted_the_app_does_not_publish_with_NativeAOT()
    {
        var root = await AppAsync();

        var (exit, output) = await PublishAsync(root, "publish-sqlclient-strict",
            GuideSnippets.SqlClientPackage, GuideSnippets.Globalization, GuideSnippets.SqlClientSingleFile);

        Assert.NotEqual(0, exit);
        Assert.Contains("error IL2104: Assembly 'Microsoft.Data.SqlClient' produced trim warnings", output, StringComparison.Ordinal);
        Assert.Contains("error IL3053: Assembly 'Microsoft.Data.SqlClient' produced AOT analysis warnings", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task With_the_recipe_the_app_publishes_shows_the_accepted_warnings_and_runs_alone()
    {
        var root = await AppAsync();

        var (exit, output) = await PublishAsync(root, "publish-sqlclient", Recipe);

        Assert.True(exit == 0, output);
        Assert.Contains("warning IL2104: Assembly 'Microsoft.Data.SqlClient' produced trim warnings", output, StringComparison.Ordinal);
        Assert.Contains("warning IL2104: Assembly 'System.Configuration.ConfigurationManager' produced trim warnings", output, StringComparison.Ordinal);
        Assert.Contains("warning IL3053: Assembly 'Microsoft.Data.SqlClient' produced AOT analysis warnings", output, StringComparison.Ordinal);
        Assert.DoesNotContain(": error ", output, StringComparison.Ordinal);
        var publish = Path.Combine(consumer.RunRoot, "publish-sqlclient");
        Assert.Equal([Executable], CoreClrApp.FilesIn(publish));
        consumer.Measure(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"sqlclient recipe publication: 1 file, {new FileInfo(Path.Combine(publish, Executable)).Length:N0} B (debug symbols aside)"));

        var (reached, said) = await ProbeAsync(publish);
        Assert.True(reached == 0, said);
        Assert.Contains("probe: Microsoft.Data.SqlClient.SqlException:", said, StringComparison.Ordinal);
        Assert.DoesNotContain("DllNotFoundException", said, StringComparison.Ordinal);

        // SqlClient refuses invariant mode, which the recipe's InvariantGlobalization=false keeps off.
        var (invariant, refused) = await ProbeAsync(publish, ("DOTNET_SYSTEM_GLOBALIZATION_INVARIANT", "1"));
        Assert.True(invariant == 2, refused);
        Assert.Contains("probe: System.NotSupportedException: Globalization Invariant Mode is not supported.", refused, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Without_managed_networking_the_executable_needs_the_SNI_library_beside_it()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "SqlClient always uses managed networking on Linux.");
        var root = await AppAsync();

        var (exit, output) = await PublishAsync(root, "publish-sqlclient-native",
            GuideSnippets.SqlClientPackage, GuideSnippets.AcceptAotWarnings, GuideSnippets.Globalization);

        Assert.True(exit == 0, output);
        var publish = Path.Combine(consumer.RunRoot, "publish-sqlclient-native");
        Assert.Equal([Executable, "Microsoft.Data.SqlClient.SNI.dll"], CoreClrApp.FilesIn(publish));
        var (beside, besideOutput) = await ProbeAsync(publish);
        Assert.True(beside == 0, besideOutput);

        var alone = Path.Combine(consumer.RunRoot, "sqlclient-native-alone");
        Directory.CreateDirectory(alone);
        File.Copy(Path.Combine(publish, Executable), Path.Combine(alone, Executable), overwrite: true);
        var (missing, said) = await ProbeAsync(alone);
        Assert.True(missing == 2, said);
        Assert.Contains("System.DllNotFoundException: Unable to load DLL 'Microsoft.Data.SqlClient.SNI.dll'", said, StringComparison.Ordinal);
    }

    public void Dispose() => consumer.Report(TestContext.Current.TestState?.Result == TestResult.Passed);

    /// <summary>The consumer, once per run: a new app without end-to-end tests, with the probe in its <c>Program.cs</c>.</summary>
    private Task<string> AppAsync() => consumer.OnceAsync("sqlclient", async () =>
    {
        var root = Path.Combine(consumer.RunRoot, "Ledger Äpp");
        var (created, output) = await consumer.NewAppAsync(root, "-n", App, "--no-end-to-end-tests");
        Assert.True(created == 0, output);
        ConsumerFiles.WriteNugetConfig(root, consumer.Feed);
        var program = Path.Combine(root, App, "Program.cs");
        var text = File.ReadAllText(program);
        Assert.StartsWith("using Spar;\n", text, StringComparison.Ordinal);
        File.WriteAllText(program, $"using {App};\nusing Spar;\n\n{ProbeCall}{text["using Spar;\n".Length..]}");
        File.WriteAllText(Path.Combine(root, App, "SqlProbe.cs"), Probe);
        File.Copy(Path.Combine(root, App, $"{App}.csproj"), Path.Combine(root, App, $"{App}.csproj.template"));
        return root;
    });

    /// <summary>The app's project as the template wrote it with the guide's <paramref name="blocks"/>, published with NativeAOT.</summary>
    private Task<(int Exit, string Output)> PublishAsync(string root, string folder, params string[] blocks)
    {
        var project = Path.Combine(root, App, $"{App}.csproj");
        File.WriteAllText(project, GuideSnippets.AddTo(File.ReadAllText(project + ".template"), GuideSnippets.Read(consumer.Repository), blocks));
        return consumer.DotnetInAsync(root, "", "publish", App, "-c", "Release", "-o", Path.Combine(consumer.RunRoot, folder), "-nologo");
    }

    /// <summary>The published executable's probe against a loopback port that was free a moment ago.</summary>
    private Task<(int Exit, string Output)> ProbeAsync(string folder, params (string Name, string Value)[] variables)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        var environment = new Dictionary<string, string> { ["LOCALAPPDATA"] = Path.Combine(consumer.RunRoot, "local-app-data") };
        foreach (var (name, value) in variables)
        {
            environment[name] = value;
        }

        // It ends by itself in about 3 s; a limit, so an app that missed its probe and opened its window is ended at once.
        return Tool.RunAsync(folder, Path.Combine(folder, Executable), ["--sql-probe", port.ToString(System.Globalization.CultureInfo.InvariantCulture)], environment,
            TimeSpan.FromSeconds(30));
    }
}
