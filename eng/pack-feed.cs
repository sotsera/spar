#:property PublishAot=false

// Packs Spar into a local NuGet feed, for an app outside this repository that uses Spar only as packages, as the SQL
// Server tool does until Spar is published (plan, M3; decision "M3's decide-first questions", item 3). From the
// repository's root, with Node.js on PATH:
//   dotnet run eng/pack-feed.cs -- [--feed <folder>] [--suffix <prerelease label>]
// The defaults: the feed artifacts/feed/ and a label local.<UTC time>, so every pack is a version no consumer has
// cached. The app's nuget.config then names the feed for Spar's packages, and its global.json the printed version.
using System.Diagnostics;
using System.Globalization;

string[] projects = ["Spar", "Spar.Windows", "Spar.Linux", "Spar.Loopback", "Spar.OpenTelemetry", "Spar.Testing", "Spar.Testing.Browser", "Spar.Sdk", "Spar.Templates", "Spar.Tool"];

var repository = FindRepository();
var feed = Path.GetFullPath(Option("--feed") ?? Path.Combine(repository, "artifacts", "feed"));
var suffix = Option("--suffix") ?? "local." + DateTime.UtcNow.ToString("yyyyMMddHHmm", CultureInfo.InvariantCulture);
var version = $"{Prefix()}-{suffix}";
Directory.CreateDirectory(feed);

// The SDK package carries the page SDK's compiled output, so the TypeScript SDK is built first.
var client = Path.Combine(repository, "src", "Spar.Client");
if (Run("npm", ["ci", "--ignore-scripts"], client) != 0 || Run("npm", ["run", "build"], client) != 0)
{
    Console.Error.WriteLine("The TypeScript SDK did not build (is Node.js on PATH?).");
    return 1;
}

foreach (var project in projects)
{
    var path = Path.Combine(repository, "src", project, project + ".csproj");
    var exit = Run("dotnet", ["pack", path, "-c", "Release", $"-p:VersionSuffix={suffix}", "-o", feed, "-nologo", "-nr:false", "-v:q"], repository);
    if (exit != 0)
    {
        Console.Error.WriteLine($"dotnet pack {project} failed.");
        return exit;
    }
}

Console.WriteLine($"""

    Packed Spar {version} into {feed}.

    In the app's global.json:
      "msbuild-sdks": {"{"} "Spar.Sdk": "{version}" {"}"}
    In its .config/dotnet-tools.json, Spar.Tool at the same version; in its nuget.config:
      <add key="spar" value="{feed}" />
    with package source mapping that sends Spar and Spar.* to it.
    """);
return 0;

string? Option(string name)
{
    var index = Array.IndexOf(args, name);
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
}

string Prefix()
{
    var props = File.ReadAllText(Path.Combine(repository, "Directory.Build.props"));
    var start = props.IndexOf("<VersionPrefix>", StringComparison.Ordinal) + "<VersionPrefix>".Length;
    return props[start..props.IndexOf("</VersionPrefix>", start, StringComparison.Ordinal)];
}

static int Run(string program, string[] arguments, string folder)
{
    // npm is npm.cmd on Windows, which only the command interpreter starts.
    var start = OperatingSystem.IsWindows() && program == "npm"
        ? new ProcessStartInfo("cmd.exe", ["/d", "/c", "npm", .. arguments])
        : new ProcessStartInfo(program, arguments);
    start.WorkingDirectory = folder;
    start.Environment["DOTNET_CLI_USE_MSBUILD_SERVER"] = "false";
    using var process = Process.Start(start)!;
    process.WaitForExit();
    return process.ExitCode;
}

static string FindRepository()
{
    for (var folder = new DirectoryInfo(Environment.CurrentDirectory); folder is not null; folder = folder.Parent)
    {
        if (File.Exists(Path.Combine(folder.FullName, "Spar.slnx")))
        {
            return folder.FullName;
        }
    }

    throw new InvalidOperationException("Run it from inside the Spar repository.");
}
