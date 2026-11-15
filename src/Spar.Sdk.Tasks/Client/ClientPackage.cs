using System.Text;
using System.Text.Json;
using Microsoft.Build.Framework;
using Spar.Generator.Emit;

namespace Spar.Sdk.Tasks.Client;

/// <summary>
/// The <c>@spar/app</c> package under <c>obj/spar/client</c> (plan section 2, "Generated client"): a module per
/// contract, <c>shared</c> when contracts share types, <c>spar</c> with the fingerprint, and the compiled SDK under
/// <c>sdk/</c> with its TypeScript sources under <c>sdk/src/</c>. The whole folder belongs to the client step.
/// </summary>
internal static class ClientPackage
{
    /// <summary>Every file of the package, by its path relative to the package folder, with <c>/</c> separators.</summary>
    /// <summary>
    /// Every file of the package. <paramref name="sourcePrefix"/> is the way from the package folder to the project
    /// folder (<c>../../../</c> for <c>obj/spar/client</c>), so the declaration maps reach the C# files.
    /// </summary>
    public static SortedDictionary<string, byte[]> Files(
        ClientLayout layout, AbsolutePath sdkDirectory, AbsolutePath sdkSourceDirectory, string version, string sourcePrefix)
    {
        var files = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
        void Text(string path, string text) => files[path] = Encoding.UTF8.GetBytes(text);

        var fingerprint = CatalogEmitter.Fingerprint(layout.Contracts.Select(c => c.Model));
        Text("spar.js", ModuleEmitter.Spar(fingerprint));
        Text("spar.d.ts", DeclarationEmitter.Spar());

        if (layout.Shared.Any())
        {
            Text("shared.js", ModuleEmitter.Shared(layout));
            var shared = DeclarationEmitter.Shared(layout);
            Text("shared.d.ts", shared.ToString());
            Text("shared.d.ts.map", shared.SourceMap("shared.d.ts", sourcePrefix));
        }

        foreach (var contract in layout.Contracts)
        {
            Text($"{contract.Model.WireName}.js", ModuleEmitter.Contract(layout, contract));
            var declarations = DeclarationEmitter.Contract(layout, contract);
            Text($"{contract.Model.WireName}.d.ts", declarations.ToString());
            Text($"{contract.Model.WireName}.d.ts.map", declarations.SourceMap($"{contract.Model.WireName}.d.ts", sourcePrefix));
        }

        foreach (var file in Directory.EnumerateFiles(sdkDirectory).Where(IsCompiledSdkFile))
        {
            files[$"sdk/{Path.GetFileName(file)}"] = File.ReadAllBytes(new AbsolutePath(file));
        }

        foreach (var file in Directory.EnumerateFiles(sdkSourceDirectory, "*.ts").Where(f => !f.EndsWith(".d.ts", StringComparison.Ordinal)))
        {
            files[$"sdk/src/{Path.GetFileName(file)}"] = File.ReadAllBytes(new AbsolutePath(file));
        }

        Text("package.json", PackageJson(layout, version));
        return files;
    }

    /// <summary>
    /// Brings the folder to exactly these files: writes the ones whose bytes differ, and deletes the rest. Returns the
    /// paths it wrote. Two variants of one app may run this at once with the same files, so each write replaces the
    /// file in one step and either writer's copy is correct.
    /// </summary>
    public static List<string> Sync(AbsolutePath directory, SortedDictionary<string, byte[]> files)
    {
        var written = new List<string>();
        foreach (var (relative, bytes) in files)
        {
            var path = new AbsolutePath(relative, directory);
            if (File.Exists(path) && File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes))
            {
                continue;
            }

            Directory.CreateDirectory(new AbsolutePath(Path.GetDirectoryName(path.Value)!));
            Write(path, bytes);
            written.Add(relative);
        }

        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var wanted = new HashSet<string>(files.Keys.Select(k => new AbsolutePath(k, directory).GetCanonicalForm().Value), comparer);
        var present = Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).Select(f => new AbsolutePath(f));
        foreach (var stale in present.Where(f => !wanted.Contains(f.GetCanonicalForm().Value)))
        {
            // A fresh temporary file belongs to another build that is still writing; an old one to a build that died.
            var abandoned = File.GetLastWriteTimeUtc(stale) < DateTime.UtcNow.AddMinutes(-1);
            if (!stale.Value.EndsWith(".tmp", StringComparison.Ordinal) || abandoned)
            {
                File.Delete(stale);
            }
        }

        return written;
    }

    /// <summary>Writes a file by replacing it in one step, so a build running beside this one never sees half of it.</summary>
    public static void Write(AbsolutePath path, byte[] bytes)
    {
        var temporary = new AbsolutePath($"{path.Value}.{Guid.NewGuid():N}.tmp");
        File.WriteAllBytes(temporary, bytes);
        Replace(temporary, path);
    }

    private static void Replace(AbsolutePath temporary, AbsolutePath path)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                File.Move(temporary, path, overwrite: true);
                return;
            }
            catch (IOException) when (attempt < 5)
            {
                // Another build's replacement or a reader holding the file; the content is the same either way.
                Thread.Sleep(50 * attempt);
            }
            catch (UnauthorizedAccessException) when (attempt < 5)
            {
                Thread.Sleep(50 * attempt);
            }
        }
    }

    private static bool IsCompiledSdkFile(string path) =>
        path.EndsWith(".js", StringComparison.Ordinal) || path.EndsWith(".d.ts", StringComparison.Ordinal) || path.EndsWith(".map", StringComparison.Ordinal);

    private static string PackageJson(ClientLayout layout, string version)
    {
        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true, NewLine = "\n" }))
        {
            json.WriteStartObject();
            json.WriteString("name", "@spar/app");
            json.WriteString("version", version);
            json.WriteBoolean("private", true);
            json.WriteString("description", "Generated by Spar from the contracts of this app. Do not edit: every build rewrites it.");
            json.WriteString("type", "module");
            json.WriteBoolean("sideEffects", false);
            json.WriteStartObject("exports");
            Export(json, "sdk", "sdk/sdk");
            Export(json, "spar", "spar");
            foreach (var contract in layout.Contracts)
            {
                Export(json, contract.Model.WireName, contract.Model.WireName);
            }

            json.WriteEndObject();
            json.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray()) + "\n";
    }

    private static void Export(Utf8JsonWriter json, string subpath, string file)
    {
        json.WriteStartObject($"./{subpath}");
        json.WriteString("types", $"./{file}.d.ts");
        json.WriteString("default", $"./{file}.js");
        json.WriteEndObject();
    }
}
