namespace Spar.PackageTests;

/// <summary>
/// The consumer an app author would write with Spar's packages alone (no template yet: decision "Packages for the
/// package-only consumer"): an app whose project and assembly names differ, with one contract, and a test project that
/// calls the contract through the generated typed client. Warnings fail its builds, as an app from the template has it.
/// </summary>
internal static class ConsumerFiles
{
    public static void Write(string root, string feed, string sparVersion, string dotnetSdk)
    {
        WriteGlobalJson(root, sparVersion, dotnetSdk);
        File.WriteAllText(Path.Combine(root, "nuget.config"), NugetConfig(feed));
        WriteApp(Path.Combine(root, "App"));
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

    private static void WriteApp(string folder)
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

    private static void WriteTests(string folder)
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
                <ProjectReference Include="..\App\Notes.App.csproj" />
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
}
