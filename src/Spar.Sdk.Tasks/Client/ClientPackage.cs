using System.Text;
using System.Text.Json;
using Microsoft.Build.Framework;
using Spar.Generator;
using Spar.Generator.Emit;

namespace Spar.Sdk.Tasks.Client;

/// <summary>
/// The <c>@spar/app</c> package under <c>obj/spar/client</c> (plan section 2, "Generated client"): a module per
/// contract, <c>shared</c> when contracts share types, <c>spar</c> with the fingerprint, <c>testing</c> with the fake
/// backend for page tests, <c>windows</c> with the page's close guard, and the compiled SDK under
/// <c>sdk/</c> with its TypeScript sources under <c>sdk/src/</c>. The whole folder belongs to the client step.
/// </summary>
internal static class ClientPackage
{
    /// <summary>
    /// The page security Program.cs declares, for the Vite plugin, which reads it relative to itself
    /// (<c>sdk/vite/index.js</c> → <c>../../csp.json</c>) on every build and page load (plan section 3b).
    /// </summary>
    public const string CspFile = "csp.json";

    /// <summary>
    /// Every file of the package, by its path relative to the package folder, with <c>/</c> separators.
    /// <paramref name="sourcePrefix"/> is the way from the package folder to the project
    /// folder (<c>../../../</c> for <c>obj/spar/client</c>), so the declaration maps reach the C# files.
    /// </summary>
    public static SortedDictionary<string, byte[]> Files(
        ClientLayout layout,
        AbsolutePath sdkDirectory,
        AbsolutePath sdkSourceDirectory,
        string sdkVersion,
        string sourcePrefix,
        PageSecurityModel? security = null)
    {
        var files = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
        void Text(string path, string text) => files[path] = Encoding.UTF8.GetBytes(text);

        var fingerprint = CatalogEmitter.Fingerprint(layout.Contracts.Select(c => c.Model));
        Text("spar.js", ModuleEmitter.Spar(fingerprint));
        Text("spar.d.ts", DeclarationEmitter.Spar());
        Text("testing.js", ModuleEmitter.Testing(layout));
        Text("testing.d.ts", DeclarationEmitter.Testing());
        Text("windows.js", ModuleEmitter.Windows());
        Text("windows.d.ts", DeclarationEmitter.Windows());

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

        // The compiled SDK, its subfolders included (vite/), and its sources under sdk/src/ at the same relative paths.
        foreach (var file in Directory.EnumerateFiles(sdkDirectory, "*", SearchOption.AllDirectories).Where(IsCompiledSdkFile))
        {
            files[$"sdk/{Relative(sdkDirectory, file)}"] = File.ReadAllBytes(new AbsolutePath(file));
        }

        var sources = Directory.EnumerateFiles(sdkSourceDirectory, "*.ts", SearchOption.AllDirectories)
            .Where(f => !f.EndsWith(".d.ts", StringComparison.Ordinal));
        foreach (var file in sources)
        {
            files[$"sdk/src/{Relative(sdkSourceDirectory, file)}"] = File.ReadAllBytes(new AbsolutePath(file));
        }

        Text("package.json", PackageJson(layout, sdkVersion));
        Text(CspFile, CspJson(security));
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

    private static string Relative(AbsolutePath directory, string file) => Path.GetRelativePath(directory, file).Replace('\\', '/');

    private static bool IsCompiledSdkFile(string path) =>
        path.EndsWith(".js", StringComparison.Ordinal) || path.EndsWith(".d.ts", StringComparison.Ordinal) || path.EndsWith(".map", StringComparison.Ordinal);

    private static string PackageJson(ClientLayout layout, string sdkVersion)
    {
        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true, NewLine = "\n" }))
        {
            json.WriteStartObject();
            json.WriteString("name", "@spar/app");
            // Always 0.0.0: npm copies a linked package's version into the app's committed lockfile, so a real version
            // would change that file on every Spar update. The SDK's version goes in a field npm does not copy.
            json.WriteString("version", "0.0.0");
            json.WriteBoolean("private", true);
            json.WriteString("description", "Generated by Spar from the contracts of this app. Do not edit: every build rewrites it.");
            json.WriteString("type", "module");
            json.WriteBoolean("sideEffects", false);
            json.WriteString("sparSdk", sdkVersion);
            json.WriteStartObject("exports");
            Export(json, "sdk", "sdk/sdk");
            Export(json, "vite", "sdk/vite/index");
            Export(json, "spar", "spar");
            Export(json, "testing", "testing");
            Export(json, "windows", "windows");
            foreach (var contract in layout.Contracts)
            {
                Export(json, contract.Model.WireName, contract.Model.WireName);
            }

            json.WriteEndObject();
            json.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray()) + "\n";
    }

    /// <summary>
    /// <c>csp.json</c>: the declaration as the build read it, the defaults when the app has no Create call. Its names
    /// are the C# methods' in camelCase; the values are already normalized.
    /// </summary>
    internal static string CspJson(PageSecurityModel? security)
    {
        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true, NewLine = "\n" }))
        {
            json.WriteStartObject();
            Sources(json, "fontSrc", security?.FontSrc);
            Sources(json, "imgSrc", security?.ImgSrc);
            Sources(json, "connectSrc", security?.ConnectSrc);
            Sources(json, "mediaSrc", security?.MediaSrc);
            Sources(json, "scriptSrc", security?.ScriptSrc);
            Sources(json, "styleSrc", security?.StyleSrc);
            Sources(json, "frameSrc", security?.FrameSrc);
            json.WriteBoolean("allowInlineStyles", security?.AllowInlineStyles ?? false);
            json.WriteBoolean("allowEval", security?.AllowEval ?? false);
            json.WriteBoolean("allowWebAssembly", security?.AllowWebAssembly ?? false);
            json.WriteBoolean("styleNonce", security?.StyleNonce ?? false);
            json.WriteBoolean("trustedTypes", security?.TrustedTypes ?? true);
            json.WriteBoolean("integrity", security?.Integrity ?? true);
            json.WriteStartObject("reporting");
            json.WriteString("listener", CamelCase(security?.Listener ?? "Always"));
            json.WriteString("endpoint", CamelCase(security?.Endpoint ?? "Always"));
            json.WriteEndObject();
            json.WriteStartArray("permissions");
            foreach (var permission in security?.Permissions ?? default)
            {
                json.WriteStringValue(CamelCase(permission));
            }

            json.WriteEndArray();
            json.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray()) + "\n";
    }

    private static void Sources(Utf8JsonWriter json, string name, EquatableArray<string>? sources)
    {
        json.WriteStartArray(name);
        foreach (var source in sources ?? default)
        {
            json.WriteStringValue(source);
        }

        json.WriteEndArray();
    }

    private static string CamelCase(string name) => name.Length == 0 ? name : char.ToLowerInvariant(name[0]) + name[1..];

    private static void Export(Utf8JsonWriter json, string subpath, string file)
    {
        json.WriteStartObject($"./{subpath}");
        json.WriteString("types", $"./{file}.d.ts");
        json.WriteString("default", $"./{file}.js");
        json.WriteEndObject();
    }
}
