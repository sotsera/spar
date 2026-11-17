namespace Spar.PackageTests;

/// <summary>
/// The consumer an app author would write with Spar's packages alone (no template yet: decision "Packages for the
/// package-only consumer"): an app whose project and assembly names differ, with one contract, and a test project that
/// calls the contract through the generated typed client. The app's page sits in <c>ui/</c>, not the default folder, with
/// its committed lockfile; an end-to-end project opens it in Chromium. Warnings fail every build, as an app from the
/// template has it.
/// </summary>
internal static class ConsumerFiles
{
    public static void Write(string root, string feed, string sparVersion, string dotnetSdk, string page, string playwrightVersion)
    {
        WriteGlobalJson(root, sparVersion, dotnetSdk);
        File.WriteAllText(Path.Combine(root, "nuget.config"), NugetConfig(feed));
        WriteApp(Path.Combine(root, "App"));
        CopyFolder(page, Path.Combine(root, "App", "ui"));
        WriteTests(Path.Combine(root, "App.Tests"));
        WriteEndToEndTests(Path.Combine(root, "App.EndToEndTests"), playwrightVersion);
    }

    /// <summary>
    /// The same app and test project in a folder that manages package versions centrally, as many solutions do: a
    /// <c>Directory.Packages.props</c> with transitive pinning, which pins a package Spar itself uses.
    /// </summary>
    public static void WriteCentral(string root, string feed, string sparVersion, string dotnetSdk, string page, string pinnedVersion)
    {
        WriteGlobalJson(root, sparVersion, dotnetSdk);
        File.WriteAllText(Path.Combine(root, "nuget.config"), NugetConfig(feed));
        File.WriteAllText(Path.Combine(root, "Directory.Packages.props"), $$"""
            <Project>
              <PropertyGroup>
                <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
                <CentralPackageTransitivePinningEnabled>true</CentralPackageTransitivePinningEnabled>
              </PropertyGroup>

              <ItemGroup>
                <PackageVersion Include="Microsoft.Extensions.DependencyInjection" Version="{{pinnedVersion}}" />
              </ItemGroup>
            </Project>

            """);
        WriteApp(Path.Combine(root, "App"));
        CopyFolder(page, Path.Combine(root, "App", "ui"));
        WriteTests(Path.Combine(root, "App.Tests"));
    }

    /// <summary>The .NET SDK the repository uses, and the Spar.Sdk version the project files resolve.</summary>
    public static void WriteGlobalJson(string root, string sparVersion, string dotnetSdk) =>
        File.WriteAllText(Path.Combine(root, "global.json"), $$"""
            {
              "sdk": { "version": "{{dotnetSdk}}", "rollForward": "latestPatch" },
              "msbuild-sdks": { "Spar.Sdk": "{{sparVersion}}" },
              "test": { "runner": "Microsoft.Testing.Platform" }
            }

            """);

    /// <summary>Spar's packages from this run's feed only; everything else from nuget.org.</summary>
    private static string NugetConfig(string feed) => $"""
        <?xml version="1.0" encoding="utf-8"?>
        <configuration>
          <packageSources>
            <clear />
            <add key="spar" value="{feed}" />
            <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
          </packageSources>
          <packageSourceMapping>
            <packageSource key="spar">
              <package pattern="Spar" />
              <package pattern="Spar.*" />
            </packageSource>
            <packageSource key="nuget.org">
              <package pattern="*" />
            </packageSource>
          </packageSourceMapping>
        </configuration>

        """;

    internal static void WriteApp(string folder)
    {
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "Notes.App.csproj"), """
            <Project Sdk="Spar.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net11.0</TargetFramework>
                <Nullable>enable</Nullable>
                <ImplicitUsings>enable</ImplicitUsings>
                <AssemblyName>NotesTool</AssemblyName>
                <RootNamespace>NotesTool</RootNamespace>
                <SparFrontendPath>ui</SparFrontendPath>
                <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
                <MSBuildTreatWarningsAsErrors>true</MSBuildTreatWarningsAsErrors>
              </PropertyGroup>
            </Project>

            """);
        File.WriteAllText(Path.Combine(folder, "Program.cs"), """
            using Spar;

            var app = SparApplication.Create(args, _ => { });
            return app.Run();

            """);
        File.WriteAllText(Path.Combine(folder, "NotesService.cs"), """
            using Spar;

            namespace NotesTool;

            /// <summary>A note's title and how many words it has.</summary>
            public sealed record Note(string Title, int Words);

            /// <summary>Counts words. Wire name: notes.</summary>
            [SparContract]
            public sealed class NotesService
            {
                [SparCommand]
                public Task<Note> CountAsync(string text) =>
                    Task.FromResult(new Note(text, text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length));
            }

            """);
    }

    internal static void WriteTests(string folder)
    {
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "Notes.App.Tests.csproj"), """
            <Project Sdk="Spar.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net11.0</TargetFramework>
                <Nullable>enable</Nullable>
                <ImplicitUsings>enable</ImplicitUsings>
                <IsTestProject>true</IsTestProject>
                <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
                <MSBuildTreatWarningsAsErrors>true</MSBuildTreatWarningsAsErrors>
              </PropertyGroup>

              <ItemGroup>
                <SparTestedApplication Include="..\App\Notes.App.csproj" />
              </ItemGroup>
            </Project>

            """);
        File.WriteAllText(Path.Combine(folder, "NotesTests.cs"), """
            using NotesTool;
            using Spar.Testing;

            namespace NotesTool.Tests;

            public sealed class NotesTests
            {
                [Fact]
                public async Task Counting_words_goes_through_the_apps_own_dispatch()
                {
                    var token = TestContext.Current.CancellationToken;
                    await using var app = await SparTestApp.StartAsync<Program>(cancellationToken: token);

                    var note = await new NotesClient(app).CountAsync("hello package world", token);

                    Assert.Equal(3, note.Words);
                }
            }

            """);
    }

    private static void WriteEndToEndTests(string folder, string playwrightVersion)
    {
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "Notes.App.EndToEndTests.csproj"), $$"""
            <Project Sdk="Spar.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net11.0</TargetFramework>
                <Nullable>enable</Nullable>
                <ImplicitUsings>enable</ImplicitUsings>
                <IsTestProject>true</IsTestProject>
                <SparBrowserTests>true</SparBrowserTests>
                <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
                <MSBuildTreatWarningsAsErrors>true</MSBuildTreatWarningsAsErrors>
              </PropertyGroup>

              <ItemGroup>
                <PackageReference Include="Microsoft.Playwright" Version="{{playwrightVersion}}" />
              </ItemGroup>

              <ItemGroup>
                <SparTestedApplication Include="..\App\Notes.App.csproj" />
              </ItemGroup>
            </Project>

            """);
        File.WriteAllText(Path.Combine(folder, "PageTests.cs"), """
            using Microsoft.Playwright;
            using NotesTool;
            using Spar.Testing;
            using static Microsoft.Playwright.Assertions;

            namespace NotesTool.EndToEndTests;

            public sealed class PageTests
            {
                [Fact]
                public async Task The_page_counts_words_through_the_backend()
                {
                    var token = TestContext.Current.CancellationToken;
                    await using var app = await SparTestApp.StartAsync<Program>(o =>
                    {
                        o.Surface = SparTestSurface.Browser;
                        o.Page();
                    }, token);
                    using var playwright = await Playwright.CreateAsync();
                    await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
                    var page = await browser.NewPageAsync();

                    await page.GotoAsync(app.PageUrl().ToString());
                    await page.GetByLabel("Text").FillAsync("one two three four");
                    await page.GetByRole(AriaRole.Button, new() { Name = "Count" }).ClickAsync();

                    await Expect(page.GetByRole(AriaRole.Status)).ToHaveTextAsync("4 words");
                    Assert.Empty(app.Problems);
                }
            }

            """);
    }

    /// <summary>Copies the page's authored files, as an author's checkout has them: no node_modules.</summary>
    internal static void CopyFolder(string source, string target)
    {
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            var destination = Path.Combine(target, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination);
        }
    }
}
