using System.Text.Json;
using Spar.Sdk.Tasks;
using Spar.Tool.Frontend;
using Spar.Tool.Project;

namespace Spar.Tool.Doctor;

/// <summary>
/// The app's project, read from its files: the frontend folder (<c>SparFrontendPath</c>), the <c>@spar/app</c>
/// dependency, Spar's Vite plugin, the <c>dev</c> and <c>build</c> scripts, the lockfile and <c>node_modules</c> (the
/// build's own rule, <see cref="FrontendInstallState"/>), what the type-check sees, the contract snapshot against the
/// one the last build expected, and the launch profiles.
/// </summary>
internal static class ProjectChecks
{
    public static IEnumerable<Finding> Check(AppProject app)
    {
        var frontend = app.FrontendDirectory;
        var packageFile = Path.Combine(frontend, "package.json");
        var shown = Path.GetRelativePath(app.Directory, frontend).Replace('\\', '/');
        if (!File.Exists(packageFile))
        {
            yield return Finding.Error(FindingIds.FrontendFolder, Area.Project,
                $"The frontend folder {shown}/ has no package.json{(app.FrontendPathProperty is null ? " (frontend/, the default; SparFrontendPath names another)" : " (SparFrontendPath)")}, so the app has no page.",
                "Make or wire one: dotnet spar init frontend --framework react (or vue) scaffolds one with Vite, and dotnet spar init frontend wires an existing Vite frontend.");
            foreach (var finding in new[] { Snapshot(app), LaunchProfiles(app) })
            {
                yield return finding;
            }

            yield break;
        }

        yield return Finding.Info(FindingIds.FrontendFolder, Area.Project, $"The frontend is {shown}/.");
        var (contents, problem) = PackageJson.Read(File.ReadAllText(packageFile));
        if (contents is null)
        {
            yield return Finding.Error(FindingIds.SparDependency, Area.Project, $"{shown}/{problem}", "Fix package.json: npm and the build read it.");
        }
        else
        {
            yield return Dependency(app, contents, shown);
            foreach (var finding in Scripts(contents))
            {
                yield return finding;
            }
        }

        yield return VitePlugin(frontend, shown);
        foreach (var finding in Install(frontend, shown))
        {
            yield return finding;
        }

        if (TypeCheckScope(frontend) is { } typeCheck)
        {
            yield return typeCheck;
        }

        foreach (var finding in new[] { Snapshot(app), LaunchProfiles(app) })
        {
            yield return finding;
        }
    }

    internal static Finding Dependency(AppProject app, PackageJson.Contents contents, string shown)
    {
        var expected = PackageJson.ClientSpecifier(app.FrontendDirectory, app.ClientDirectory);
        var fix = $"Set \"{PackageJson.SparPackage}\": \"{expected}\" in the dependencies of {shown}/package.json (dotnet spar init frontend does), then build: the build writes that package.";
        if (contents.SparDependency is null)
        {
            var where = contents.SparDevDependency is null ? "" : " (it is in devDependencies)";
            return Finding.Error(FindingIds.SparDependency, Area.Project, $"{shown}/package.json has no {PackageJson.SparPackage} dependency{where}: the page cannot import its generated client or Spar's Vite plugin.", fix);
        }

        if (!PackageJson.NamesClient(contents.SparDependency, app.FrontendDirectory, app.ClientDirectory))
        {
            return Finding.Error(FindingIds.SparDependency, Area.Project, $"{shown}/package.json's {PackageJson.SparPackage} is \"{contents.SparDependency}\", not the app's generated client ({expected}).", fix);
        }

        return Finding.Info(FindingIds.SparDependency, Area.Project, $"{PackageJson.SparPackage} is \"{contents.SparDependency}\", the app's generated client.");
    }

    internal static IEnumerable<Finding> Scripts(PackageJson.Contents contents)
    {
        if (contents.DevScript is null)
        {
            yield return Finding.Error(FindingIds.Scripts, Area.Project, "package.json has no dev script: a Debug run starts the page's server with npm run dev.",
                "Add \"dev\": \"vite\" to its scripts (dotnet spar init frontend does).");
        }

        if (contents.BuildScript is null)
        {
            yield return Finding.Warning(FindingIds.Scripts, Area.Project, "package.json has no build script. Spar's build runs Vite itself, but npm run build is how the page is built on its own.",
                "Add \"build\": \"vite build\" to its scripts.");
        }

        if (contents.DevScript is not null && contents.BuildScript is not null)
        {
            yield return Finding.Info(FindingIds.Scripts, Area.Project, $"Scripts: dev is \"{contents.DevScript}\", build is \"{contents.BuildScript}\".");
        }
    }

    internal static Finding VitePlugin(string frontend, string shown)
    {
        var fix = $"Add Spar's plugin to Vite's configuration (dotnet spar init frontend does, or prints the snippet):\n{ViteConfig.Snippet}";
        if (ViteConfig.Find(frontend) is not { } config)
        {
            return Finding.Error(FindingIds.VitePlugin, Area.Project, $"{shown}/ has no vite.config file: the page needs Spar's Vite plugin.", fix);
        }

        var name = Path.GetFileName(config);
        var (imported, called) = ViteConfig.Inspect(File.ReadAllText(config));
        if (!imported || !called)
        {
            return Finding.Error(FindingIds.VitePlugin, Area.Project,
                $"{shown}/{name} {(imported ? "imports Spar's plugin but does not call it in plugins" : $"does not import Spar's plugin ({ViteConfig.PluginModule})")}: without it a Debug run cannot serve the page, and the build's bundle has no Spar settings.",
                fix);
        }

        return Finding.Info(FindingIds.VitePlugin, Area.Project, $"{shown}/{name} uses Spar's Vite plugin.");
    }

    internal static IEnumerable<Finding> Install(string frontend, string shown)
    {
        var lockfile = new FileInfo(Path.Combine(frontend, FrontendInstallState.Lockfile));
        if (!lockfile.Exists)
        {
            yield return Finding.Error(FindingIds.Lockfile, Area.Project, $"{shown}/ has no package-lock.json: the build installs exactly the lockfile's packages, with npm ci, and cannot without one.",
                $"Run npm install --ignore-scripts in {shown}/ once, after a build has written the generated client (dotnet spar init frontend does both), and commit package-lock.json.");
            yield break;
        }

        yield return Finding.Info(FindingIds.Lockfile, Area.Project, $"{shown}/package-lock.json.");
        var record = new FileInfo(Path.Combine(frontend, FrontendInstallState.InstallRecord));
        var current = FrontendInstallState.IsCurrent(lockfile.LastWriteTimeUtc, record.Exists ? record.LastWriteTimeUtc : null);
        if (current)
        {
            yield return Finding.Info(FindingIds.NodeModules, Area.Project, $"{shown}/node_modules is current with the lockfile.");
            yield break;
        }

        var state = Directory.Exists(Path.Combine(frontend, "node_modules")) ? "older than package-lock.json" : "missing";
        yield return Finding.Warning(FindingIds.NodeModules, Area.Project,
            $"{shown}/node_modules is {state}: the next build installs it again (npm ci --ignore-scripts), but npm test or npm run dev before that use what is there.",
            $"Build the app (dotnet build), or run npm ci --ignore-scripts in {shown}/.");
    }

    /// <summary>
    /// The build type-checks with <c>tsc --noEmit -p .</c> on the frontend's <c>tsconfig.json</c>. A config that only
    /// references others (<c>"files": []</c> with <c>"references"</c>, Vite's scaffold) gives that command no file to check.
    /// </summary>
    internal static Finding? TypeCheckScope(string frontend)
    {
        var path = Path.Combine(frontend, "tsconfig.json");
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            var root = document.RootElement;
            var emptyFiles = root.TryGetProperty("files", out var files) && files.ValueKind == JsonValueKind.Array && files.GetArrayLength() == 0;
            var hasInclude = root.TryGetProperty("include", out _);
            var references = root.TryGetProperty("references", out var list) && list.ValueKind == JsonValueKind.Array && list.GetArrayLength() > 0;
            if (emptyFiles && !hasInclude && references)
            {
                return Finding.Warning(FindingIds.TypeCheck, Area.Project,
                    "tsconfig.json only references other configs (\"files\": [] and \"references\"), so the build's type-check (tsc --noEmit -p .) checks no file of the page.",
                    "Put the page's compilerOptions and \"include\": [\"src\"] in tsconfig.json itself, as the page of dotnet new spar-app has them; or set SparTypeCheck=false in the project and check with npm run build (tsc -b) instead.");
            }
        }
        catch (JsonException)
        {
            // An unreadable tsconfig.json is the type-check's own error at build time, with its line.
        }

        return null;
    }

    internal static Finding Snapshot(AppProject app)
    {
        var name = Path.GetFileName(app.SnapshotFile);
        if (!File.Exists(app.SnapshotFile))
        {
            return Finding.Warning(FindingIds.ContractSnapshot, Area.Project, $"{name} is missing: a CI build (ContinuousIntegrationBuild=true) fails without it (SPAR100).",
                "Build the app once, which writes it, and commit it.");
        }

        // What the last build expected (the client step's copy under obj/), compared as the build compares it: CRLF read as LF.
        var obj = Path.Combine(app.Directory, "obj");
        var expected = Directory.Exists(obj)
            ? Directory.EnumerateFiles(obj, "contracts.snapshot", SearchOption.AllDirectories)
                .Select(path => new FileInfo(path)).OrderByDescending(file => file.LastWriteTimeUtc).FirstOrDefault()
            : null;
        if (expected is null)
        {
            return Finding.Info(FindingIds.ContractSnapshot, Area.Project, $"{name} (no build yet to compare it with).");
        }

        var committed = File.ReadAllText(app.SnapshotFile).Replace("\r\n", "\n", StringComparison.Ordinal);
        if (File.ReadAllText(expected.FullName) != committed)
        {
            return Finding.Warning(FindingIds.ContractSnapshot, Area.Project, $"{name} differs from the contracts the last build saw: a CI build fails with SPAR100.",
                "Build the app locally, which updates it, and commit it with the contract change.");
        }

        return Finding.Info(FindingIds.ContractSnapshot, Area.Project, $"{name} matches the last build.");
    }

    internal static Finding LaunchProfiles(AppProject app)
    {
        var path = Path.Combine(app.Directory, "Properties", "launchSettings.json");
        const string fix = "Keep Desktop as the first profile (\"commandName\": \"Project\") and Loopback with \"environmentVariables\": { \"SPAR_HOST\": \"Loopback\" }, as dotnet new spar-app writes them.";
        if (!File.Exists(path))
        {
            return Finding.Warning(FindingIds.LaunchProfiles, Area.Project, "Properties/launchSettings.json is missing: dotnet run and the IDEs have no Desktop or Loopback profile.", fix);
        }

        var names = new List<string>();
        string? loopbackHost = null;
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            if (document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty("profiles", out var profiles) && profiles.ValueKind == JsonValueKind.Object)
            {
                names.AddRange(profiles.EnumerateObject().Select(p => p.Name));
                if (profiles.TryGetProperty("Loopback", out var loopback) && loopback.ValueKind == JsonValueKind.Object
                    && loopback.TryGetProperty("environmentVariables", out var variables) && variables.ValueKind == JsonValueKind.Object
                    && variables.TryGetProperty("SPAR_HOST", out var host) && host.ValueKind == JsonValueKind.String)
                {
                    loopbackHost = host.GetString();
                }
            }
        }
        catch (JsonException exception)
        {
            return Finding.Warning(FindingIds.LaunchProfiles, Area.Project, $"Properties/launchSettings.json is not valid JSON: {exception.Message}", fix);
        }

        if (names.FirstOrDefault() != "Desktop")
        {
            return Finding.Warning(FindingIds.LaunchProfiles, Area.Project, $"The first launch profile is {(names.Count == 0 ? "missing" : names[0])}, not Desktop: dotnet run uses the first one.", fix);
        }

        if (loopbackHost != "Loopback")
        {
            return Finding.Warning(FindingIds.LaunchProfiles, Area.Project, "There is no Loopback profile that sets SPAR_HOST=Loopback: dotnet run -lp Loopback cannot serve the page to a browser.", fix);
        }

        return Finding.Info(FindingIds.LaunchProfiles, Area.Project, "Launch profiles Desktop (first) and Loopback.");
    }
}
