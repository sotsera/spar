using System.Collections.Concurrent;
using System.IO.Compression;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Spike.Generator;

namespace Spike.Tool;

/// <summary>
/// Client generation and frontend packing, shared by the command-line tool and the MSBuild tasks.
/// All paths must be absolute; callers resolve them (the tasks through TaskEnvironment).
/// </summary>
public static class ClientBuilder
{
    // Metadata references are the expensive part of analysis. A long-lived host (MSBuild node or task host)
    // reuses them across builds while the file's timestamp and size are unchanged.
    private static readonly ConcurrentDictionary<string, (DateTime Stamp, long Length, MetadataReference Reference)> References = new(StringComparer.OrdinalIgnoreCase);

    public static (int Contracts, int Sources, int References, int ReusedReferences) GenerateClient(string inventoryPath, string outDir)
    {
        var lines = File.ReadAllLines(inventoryPath);
        string Value(string key) => lines.FirstOrDefault(l => l.StartsWith(key + "=", StringComparison.Ordinal))?[(key.Length + 1)..] ?? string.Empty;
        IEnumerable<string> Values(string key) => lines.Where(l => l.StartsWith(key + "=", StringComparison.Ordinal)).Select(l => l[(key.Length + 1)..]);

        // Parse with the newest language version this Roslyn knows; the SDK compiler does the real compile.
        var parseOptions = new CSharpParseOptions(LanguageVersion.Preview)
            .WithPreprocessorSymbols(Value("define").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        var trees = Values("source").Select(path => CSharpSyntaxTree.ParseText(File.ReadAllText(path), parseOptions, path)).ToList();

        var reused = 0;
        var references = new List<MetadataReference>();
        foreach (var path in Values("reference"))
        {
            var info = new FileInfo(path);
            if (References.TryGetValue(path, out var cached) && cached.Stamp == info.LastWriteTimeUtc && cached.Length == info.Length)
            {
                references.Add(cached.Reference);
                reused++;
                continue;
            }

            var reference = MetadataReference.CreateFromFile(path);
            References[path] = (info.LastWriteTimeUtc, info.Length, reference);
            references.Add(reference);
        }

        var nullable = Value("nullable") is "enable" or "Enable" ? NullableContextOptions.Enable : NullableContextOptions.Disable;
        var compilation = CSharpCompilation.Create("analysis", trees, references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: nullable));

        var contracts = new List<ContractModel>();
        foreach (var tree in trees)
        {
            var model = compilation.GetSemanticModel(tree);
            foreach (var declaration in tree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>().Where(c => c.AttributeLists.Count > 0))
            {
                if (model.GetDeclaredSymbol(declaration) is not { } type)
                {
                    continue;
                }

                var attribute = type.GetAttributes().FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == ContractReader.ContractAttribute);
                if (attribute is not null)
                {
                    contracts.Add(ContractReader.Read(type, attribute, CancellationToken.None));
                }
            }
        }

        // The generator reports contract diagnostics with source locations during compilation; the client
        // step only skips invalid contracts so the C# compiler, not this step, owns the error report.
        var valid = contracts.Where(c => c.Diagnostics.Count == 0).OrderBy(c => c.WireName, StringComparer.Ordinal).ToList();
        Directory.CreateDirectory(outDir);
        var written = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Write(string name, string content)
        {
            var path = Path.Combine(outDir, name);
            written.Add(path);
            if (!File.Exists(path) || File.ReadAllText(path) != content)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, content, new UTF8Encoding(false));
            }
        }

        var manifest = ClientEmitter.EmitManifest(valid);
        var fingerprint = Fingerprint(manifest);
        Write("contracts.json", manifest);
        Write("transport.ts", "export declare function invoke<T>(contract: string, command: string, request: unknown): Promise<T>;\n");
        foreach (var contract in valid)
        {
            Write(contract.WireName + ".ts", ClientEmitter.EmitTypeScript(contract));
            Write(contract.WireName + ".js", ClientEmitter.EmitJavaScript(contract, fingerprint));
        }

        // npm-shaped packages for bundler frontends: packages/@spar/sdk and packages/@spar/<contract>.
        // TypeScript source only: the application's own toolchain type-checks and bundles it. Packages import each
        // other relatively ("../sdk/index.js" resolves to index.ts), so linking them needs no bundler settings.
        var packages = Path.Combine(outDir, "packages", "@spar");
        Write(Path.Combine("packages", "@spar", "sdk", "package.json"), PackageJson("sdk"));
        Write(Path.Combine("packages", "@spar", "sdk", "index.ts"), SdkTypeScript);
        foreach (var contract in valid)
        {
            var folder = Path.Combine("packages", "@spar", contract.WireName);
            Write(Path.Combine(folder, "package.json"), PackageJson(contract.WireName));
            Write(Path.Combine(folder, "index.ts"), ClientEmitter.EmitPackageTypeScript(contract, fingerprint, "../sdk/index.js"));
        }

        // Reviewable snapshot of the generated public surface (spike). The build copies it next to the project, where it
        // is committed: a contract change, or a Spar update that changes generated code, shows up as a diff to review.
        var snapshot = new StringBuilder("# Spar contract snapshot. Written by the build: commit it and review its changes.\n");
        snapshot.Append("# fingerprint ").Append(fingerprint).Append('\n');
        snapshot.Append("\n## @spar/sdk/index.ts\n\n").Append(SdkTypeScript);
        foreach (var contract in valid)
        {
            snapshot.Append("\n## @spar/").Append(contract.WireName).Append("/index.ts\n\n")
                .Append(ClientEmitter.EmitPackageTypeScript(contract, fingerprint, "../sdk/index.js"));
        }

        Write("contracts.snapshot", snapshot.ToString());

        foreach (var stale in Directory.EnumerateFiles(outDir, "*", SearchOption.AllDirectories).Where(f => !written.Contains(f)))
        {
            File.Delete(stale);
        }

        foreach (var emptyPackage in Directory.EnumerateDirectories(packages).Where(d => !Directory.EnumerateFileSystemEntries(d).Any()))
        {
            Directory.Delete(emptyPackage);
        }

        return (valid.Count, trees.Count, references.Count, reused);
    }

    private static string PackageJson(string name) =>
        $"{{\n  \"name\": \"@spar/{name}\",\n  \"private\": true,\n  \"type\": \"module\",\n  \"exports\": \"./index.ts\",\n  \"types\": \"./index.ts\",\n  \"sideEffects\": false\n}}\n";

    // Spike stand-in for Spar's maintained SDK, as TypeScript source: one transport per host, chosen at runtime.
    private const string SdkTypeScript = """
        // Spar SDK (spike). Do not edit.
        const hash = new URLSearchParams(location.hash.slice(1));
        const token = hash.get("token") ?? sessionStorage.getItem("spar-token");
        if (token) {
          sessionStorage.setItem("spar-token", token);
          if (hash.has("token")) history.replaceState(null, "", location.pathname + location.search);
        }

        export interface CallOptions {
          readonly signal?: AbortSignal;
        }

        /** The command was refused before it ran: retrying after fixing the cause is safe. */
        export class NotExecutedError extends Error {
          readonly code: string;

          constructor(code: string, message: string) {
            super(message);
            this.code = code;
          }
        }

        /** The page was built from different contracts than the running backend. */
        export class ContractMismatchError extends NotExecutedError {}

        /** The connection failed or the call was abandoned: the command may or may not have run. */
        export class OutcomeUnknownError extends Error {}

        interface ErrorBody {
          readonly error?: string;
          readonly detail?: string;
        }

        // One transport for both hosts: same-origin POSTs to /_spar. The loopback host needs the per-run token;
        // the desktop host answers them in-process (WebResourceRequested) and checks the origin instead.
        const auth: Record<string, string> = token ? { "Authorization": `Bearer ${token}` } : {};

        export async function invoke<T>(fingerprint: string, contract: string, command: string, request: unknown, signal?: AbortSignal): Promise<T> {
          let response: Response;
          try {
            response = await fetch(`/_spar/call/${contract}/${command}`, {
              method: "POST",
              signal: signal ?? null,
              headers: { ...auth, "X-Spar-Contracts": fingerprint, "Content-Type": "application/json" },
              body: request === undefined ? "" : JSON.stringify(request),
            });
          } catch (error) {
            if (signal?.aborted) throw error;
            throw new OutcomeUnknownError(`The call ${contract}.${command} lost its connection.`);
          }

          if (response.ok) return await response.json() as T;
          const body = await response.json().catch(() => ({})) as ErrorBody;
          if (response.status === 409) {
            dispatchEvent(new CustomEvent("spar:contract-mismatch"));
            throw new ContractMismatchError(body.error ?? "contract-mismatch", "The app was rebuilt with different contracts. Reload the page.");
          }

          if ([400, 401, 403, 404].includes(response.status)) {
            throw new NotExecutedError(body.error ?? String(response.status), `${body.error ?? response.status}: ${body.detail ?? ""}`.trim());
          }

          throw new OutcomeUnknownError(`${response.status} ${body.error ?? ""}`.trim());
        }

        /** Tells the host the UI is wired and can accept input. */
        export async function ready(): Promise<void> {
          await fetch("/_spar/ready", { method: "POST", headers: auth });
        }

        """;

    /// <summary>Short stable hash of the contract manifest; the host computes the same value from its embedded manifest.</summary>
    public static string Fingerprint(string manifest) =>
        Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(manifest)))[..16];

    /// <summary>Writes a deterministic zip of the directories' files. Returns true when the zip changed.</summary>
    public static bool Pack(string zipPath, IEnumerable<string> dirs)
    {
        var entries = dirs
            .SelectMany(dir => Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
                .Select(file => (Name: Path.GetRelativePath(dir, file).Replace(Path.DirectorySeparatorChar, '/'), File: file)))
            .OrderBy(e => e.Name, StringComparer.Ordinal)
            .ToList();

        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, file) in entries)
            {
                var entry = archive.CreateEntry(name, CompressionLevel.SmallestSize);
                entry.LastWriteTime = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);
                using var target = entry.Open();
                using var source = File.OpenRead(file);
                source.CopyTo(target);
            }
        }

        var bytes = buffer.ToArray();
        if (File.Exists(zipPath) && File.ReadAllBytes(zipPath).AsSpan().SequenceEqual(bytes))
        {
            return false;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(zipPath)!);
        File.WriteAllBytes(zipPath, bytes);
        return true;
    }
}
