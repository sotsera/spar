using System.Text.Json;
using System.Xml.Linq;

namespace Spar.PackageTests;

/// <summary>
/// What <c>dotnet new spar-app</c> writes from the packed Spar.Templates (plan, M1d step 6): the main consumer's files as
/// the template made them, with its global.json at the package's own version, its launch profiles and its solution;
/// a new app without the end-to-end project, whose solution builds and tests; and a new app for one OS. Each new app
/// is a folder of the run's own, made with the run's template hive.
/// </summary>
[Collection(Consumer.MainCollection)]
public sealed class NewAppTests(Consumer consumer) : IDisposable
{
    private const string App = Consumer.App;

    [Fact]
    public void The_new_app_has_the_templates_files_renamed_and_nothing_else()
    {
        var template = ConsumerFiles.Template(consumer.Repository);
        var expected = Directory.EnumerateFiles(template, "*", SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(template, file).Replace('\\', '/'))
            .Where(file => !file.StartsWith(".template.config/", StringComparison.Ordinal))
            .Select(file => ConsumerFiles.Rename(file, App))
            .Order(StringComparer.Ordinal);

        Assert.Equal(expected, consumer.NewAppFiles);

        // The tool manifest comes with the spar tool (M1d step 6b); Spar's packages come from the user's own NuGet sources.
        Assert.DoesNotContain(".config/dotnet-tools.json", consumer.NewAppFiles);
        Assert.DoesNotContain("nuget.config", consumer.NewAppFiles);
    }

    [Fact]
    public void The_new_apps_global_json_pins_the_dotnet_SDK_and_Spar_Sdk_at_the_packages_own_version()
    {
        var json = JsonDocument.Parse(consumer.NewAppGlobalJson).RootElement;

        Assert.Equal(consumer.RepositorySdkVersion(), json.GetProperty("sdk").GetProperty("version").GetString());
        Assert.Equal("latestFeature", json.GetProperty("sdk").GetProperty("rollForward").GetString());
        Assert.Equal(consumer.First, json.GetProperty("msbuild-sdks").GetProperty("Spar.Sdk").GetString());
        Assert.Equal("Microsoft.Testing.Platform", json.GetProperty("test").GetProperty("runner").GetString());
        Assert.EndsWith("}\n", consumer.NewAppGlobalJson, StringComparison.Ordinal);
    }

    [Fact]
    public void The_new_app_has_both_platforms_its_app_id_its_launch_profiles_and_a_solution_of_its_three_projects()
    {
        var project = XDocument.Load(Path.Combine(consumer.Root, App, $"{App}.csproj"));
        Assert.Equal("windows;linux", Property(project, "SparPlatforms"));
        Assert.Equal($"com.example.{App}", Property(project, "SparAppId"));

        var profiles = JsonDocument.Parse(File.ReadAllText(Path.Combine(consumer.Root, App, "Properties", "launchSettings.json")))
            .RootElement.GetProperty("profiles");
        Assert.Equal(["Desktop", "Loopback"], profiles.EnumerateObject().Select(profile => profile.Name));
        Assert.Equal("Loopback", profiles.GetProperty("Loopback").GetProperty("environmentVariables").GetProperty("SPAR_HOST").GetString());

        Assert.Equal([$"{App}/{App}.csproj", $"{App}.Tests/{App}.Tests.csproj", $"{App}.EndToEndTests/{App}.EndToEndTests.csproj"],
            SolutionProjects(consumer.Root));
    }

    [Fact]
    public async Task Without_the_end_to_end_project_the_solution_holds_the_app_and_its_fast_tests_and_they_pass()
    {
        Assert.Equal(0, (await consumer.FirstAsync()).Exit);
        var folder = Path.Combine(consumer.RunRoot, "Fast Äpp");

        var (created, createOutput) = await consumer.NewAppAsync(folder, "-n", App, "--no-end-to-end-tests");

        Assert.True(created == 0, createOutput);
        ConsumerFiles.WriteNugetConfig(folder, consumer.Feed);
        Assert.False(Directory.Exists(Path.Combine(folder, $"{App}.EndToEndTests")), "The end-to-end project was created.");
        Assert.Equal([$"{App}/{App}.csproj", $"{App}.Tests/{App}.Tests.csproj"], SolutionProjects(folder));

        // dotnet test in the folder: the solution, through global.json's test runner.
        var (exit, output) = await consumer.DotnetInAsync(folder, "", "test");

        Assert.True(exit == 0, output);
        Assert.Contains("succeeded: 2", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task With_one_platform_the_app_project_names_it_and_the_app_builds()
    {
        Assert.Equal(0, (await consumer.FirstAsync()).Exit);
        var (thisOs, otherOs) = OperatingSystem.IsWindows() ? ("windows", "linux") : ("linux", "windows");
        var folder = Path.Combine(consumer.RunRoot, "One Äpp");
        var other = Path.Combine(consumer.RunRoot, "Other Äpp");

        var (created, createOutput) = await consumer.NewAppAsync(folder, "-n", App, "--platforms", thisOs, "--app-id", "org.example.notes.Single");
        var (createdOther, otherOutput) = await consumer.NewAppAsync(other, "-n", App, "--platforms", otherOs, "--no-end-to-end-tests");

        Assert.True(created == 0, createOutput);
        Assert.True(createdOther == 0, otherOutput);
        var project = XDocument.Load(Path.Combine(folder, App, $"{App}.csproj"));
        Assert.Equal(thisOs, Property(project, "SparPlatforms"));
        Assert.Equal("org.example.notes.Single", Property(project, "SparAppId"));
        Assert.Equal(otherOs, Property(XDocument.Load(Path.Combine(other, App, $"{App}.csproj")), "SparPlatforms"));

        ConsumerFiles.WriteNugetConfig(folder, consumer.Feed);
        var (exit, output) = await consumer.DotnetInAsync(folder, App, "build", "-nologo", "-v:m");

        Assert.True(exit == 0, output);
    }

    public void Dispose() => consumer.Report(TestContext.Current.TestState?.Result == TestResult.Passed);

    private static string? Property(XDocument project, string name) => project.Descendants(name).SingleOrDefault()?.Value;

    private static IEnumerable<string> SolutionProjects(string folder)
    {
        var solution = XDocument.Load(Assert.Single(Directory.GetFiles(folder, "*.slnx")));
        return solution.Descendants("Project").Select(project => (string)project.Attribute("Path")!);
    }
}
