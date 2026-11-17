namespace Spar.PackageTests;

/// <summary>
/// A contract library shipped as a NuGet package, as a team shares contracts between apps: a plain class library that
/// references the <c>Spar</c> package and declares an interface contract, packed into a feed of its own; and an app,
/// the same one as the first consumer's, that references the package, lists it in <c>SparContracts</c> and implements
/// the interface. The library writes its documentation file, which the package carries beside the DLL.
/// </summary>
internal static class LibraryConsumerFiles
{
    public const string PackageId = "Notes.Tags";

    /// <summary>The library's own folder, outside the app's, as another solution would be.</summary>
    public static void WriteLibrary(string root, string sparFeed, string sparVersion, string dotnetSdk)
    {
        Directory.CreateDirectory(root);
        ConsumerFiles.WriteGlobalJson(root, sparVersion, dotnetSdk);
        File.WriteAllText(Path.Combine(root, "nuget.config"), NugetConfig(sparFeed, libraryFeed: null));
        File.WriteAllText(Path.Combine(root, $"{PackageId}.csproj"), $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net11.0</TargetFramework>
                <Nullable>enable</Nullable>
                <ImplicitUsings>enable</ImplicitUsings>
                <IsAotCompatible>true</IsAotCompatible>
                <GenerateDocumentationFile>true</GenerateDocumentationFile>
                <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
                <MSBuildTreatWarningsAsErrors>true</MSBuildTreatWarningsAsErrors>
              </PropertyGroup>

              <ItemGroup>
                <PackageReference Include="Spar" Version="{{sparVersion}}" />
              </ItemGroup>
            </Project>

            """);
        File.WriteAllText(Path.Combine(root, "TagsService.cs"), """
            using Spar;

            namespace Notes.Tags;

            /// <summary>A tag and how many times it was added.</summary>
            /// <param name="Name">The tag as it was typed.</param>
            /// <param name="Count">How many times it was added, in any letter case.</param>
            public sealed record Tag(string Name, int Count);

            /// <summary>Why a tag was refused.</summary>
            public closed record TagError
            {
                /// <summary>The name is empty or only spaces.</summary>
                public sealed record Blank : TagError;
            }

            /// <summary>Tags for notes, shared between apps. Each app implements it.</summary>
            [SparContract]
            public interface ITagsService
            {
                /// <summary>Adds <paramref name="name"/> and returns it with its count, or why it was refused.</summary>
                Task<Result<Tag, TagError>> AddAsync(string name, CancellationToken cancellationToken);
            }

            """);
    }

    /// <summary>The first consumer's app and tests, plus the package, its listing, the implementation and a test of it.</summary>
    public static void WriteApp(string root, string sparFeed, string libraryFeed, string sparVersion, string libraryVersion, string dotnetSdk, string page)
    {
        Directory.CreateDirectory(root);
        ConsumerFiles.WriteGlobalJson(root, sparVersion, dotnetSdk);
        File.WriteAllText(Path.Combine(root, "nuget.config"), NugetConfig(sparFeed, libraryFeed));

        var app = Path.Combine(root, "App");
        ConsumerFiles.WriteApp(app);
        ConsumerFiles.CopyFolder(page, Path.Combine(app, "ui"));
        var project = Path.Combine(app, "Notes.App.csproj");
        File.WriteAllText(project, File.ReadAllText(project).Replace("</Project>", $$"""

              <ItemGroup>
                <PackageReference Include="{{PackageId}}" Version="{{libraryVersion}}" />
                <SparContracts Include="{{PackageId}}" />
              </ItemGroup>
            </Project>
            """, StringComparison.Ordinal));
        File.WriteAllText(Path.Combine(app, "Program.cs"), """
            using Microsoft.Extensions.DependencyInjection;
            using Notes.Tags;
            using NotesTool;
            using Spar;

            var app = SparApplication.Create(args, spar => spar.Services.AddSingleton<ITagsService, Tags>());
            return app.Run();

            """);
        File.WriteAllText(Path.Combine(app, "Tags.cs"), """
            using Notes.Tags;
            using Spar;

            namespace NotesTool;

            /// <summary>The app's implementation of the shared tags contract: counts kept in memory.</summary>
            public sealed class Tags : ITagsService
            {
                private readonly Dictionary<string, int> _counts = new(StringComparer.OrdinalIgnoreCase);
                private readonly Lock _lock = new();

                public Task<Result<Tag, TagError>> AddAsync(string name, CancellationToken cancellationToken)
                {
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        return Task.FromResult<Result<Tag, TagError>>(new TagError.Blank());
                    }

                    lock (_lock)
                    {
                        var count = _counts.GetValueOrDefault(name) + 1;
                        _counts[name] = count;
                        return Task.FromResult<Result<Tag, TagError>>(new Tag(name, count));
                    }
                }
            }

            """);

        var tests = Path.Combine(root, "App.Tests");
        ConsumerFiles.WriteTests(tests);
        File.WriteAllText(Path.Combine(tests, "TagsTests.cs"), """
            using Notes.Tags;
            using Spar;
            using Spar.Testing;

            namespace NotesTool.Tests;

            public sealed class TagsTests
            {
                [Fact]
                public async Task The_packaged_interface_contract_runs_the_apps_implementation()
                {
                    var token = TestContext.Current.CancellationToken;
                    await using var app = await SparTestApp.StartAsync<Program>(cancellationToken: token);
                    var tags = new TagsClient(app);

                    var first = await tags.AddAsync("work", token);
                    var second = await tags.AddAsync("Work", token);
                    var blank = await tags.AddAsync(" ", token);

                    Assert.Equal(new Tag("work", 1), Value(first));
                    Assert.Equal(new Tag("Work", 2), Value(second));
                    Assert.IsType<TagError.Blank>(Error(blank));
                }

                private static Tag Value(Result<Tag, TagError> result) => result switch
                {
                    Tag tag => tag,
                    TagError error => throw new InvalidOperationException($"Expected a tag, got {error}."),
                };

                private static TagError Error(Result<Tag, TagError> result) => result switch
                {
                    Tag tag => throw new InvalidOperationException($"Expected an error, got {tag}."),
                    TagError error => error,
                };
            }

            """);
    }

    /// <summary>Spar's packages from the run's feed, the library from its own feed when given, the rest from nuget.org.</summary>
    private static string NugetConfig(string sparFeed, string? libraryFeed)
    {
        var librarySource = libraryFeed is null ? "" : $"\n    <add key=\"library\" value=\"{libraryFeed}\" />";
        var libraryMapping = libraryFeed is null ? "" : $"""

                <packageSource key="library">
                  <package pattern="{PackageId}" />
                </packageSource>
            """;
        return $"""
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <packageSources>
                <clear />
                <add key="spar" value="{sparFeed}" />{librarySource}
                <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
              </packageSources>
              <packageSourceMapping>
                <packageSource key="spar">
                  <package pattern="Spar" />
                  <package pattern="Spar.*" />
                </packageSource>{libraryMapping}
                <packageSource key="nuget.org">
                  <package pattern="*" />
                </packageSource>
              </packageSourceMapping>
            </configuration>

            """;
    }
}
