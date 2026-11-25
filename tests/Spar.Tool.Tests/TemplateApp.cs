namespace Spar.Tool.Tests;

/// <summary>
/// The app template's app (<c>src/Spar.Templates/content/spar-app/MyApp</c>, its name left as <c>MyApp</c>) copied into
/// a test folder's <c>MyApp/</c>, as <c>dotnet new spar-app</c> writes it, optionally with what a first build adds.
/// </summary>
internal static class TemplateApp
{
    public static string Source => Path.Combine(Repository.Root, "src", "Spar.Templates", "content", "spar-app", "MyApp");

    /// <summary>The app as the template writes it: no <c>node_modules</c>, no snapshot, no <c>obj/</c>.</summary>
    public static TestFolder New()
    {
        var root = Repository.NewFolder("app");
        var app = Path.Combine(root.Path, "MyApp");
        foreach (var file in Directory.EnumerateFiles(Source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(Source, file);
            if (relative.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj" or "node_modules"))
            {
                continue;
            }

            var target = Path.Combine(app, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }

        return new TestFolder(app, root.Path);
    }

    /// <summary>The app after a build: npm's install record, the client's fingerprint, the snapshot and the build's copy of it.</summary>
    public static TestFolder Built()
    {
        var folder = New();
        folder.Write("frontend/node_modules/.package-lock.json", "{}\n");
        File.SetLastWriteTimeUtc(Path.Combine(folder.Path, "frontend", "package-lock.json"), DateTime.UtcNow.AddMinutes(-5));
        folder.Write("obj/spar/client/package.json", "{ \"name\": \"@spar/app\", \"version\": \"0.0.0\" }\n");
        folder.Write("obj/spar/client/spar.js", "export const fingerprint = \"0123456789abcdef\";\n");
        folder.Write("obj/Development/Debug/net11.0/win-x64/contracts.snapshot", "fingerprint 0123456789abcdef\n");
        folder.Write("MyApp.contracts.snapshot", "fingerprint 0123456789abcdef\n");
        return folder;
    }
}
