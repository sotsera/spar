using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Spar.Tool.Dev;
using Spar.Tool.Devtools;

namespace Spar.Tool.Snap;

/// <summary>
/// <c>spar snap</c> (plan, M2c; decision "M2c's decide-first questions"): builds the end-to-end test project that holds
/// the app's scenes, lists them (the test executable's <c>--spar-scene-list</c>), and captures each one asked for by
/// running that scene's generated test through the executable's own xUnit runner, with <c>SPAR_SNAP_SCENE</c> and
/// <c>SPAR_SNAP_OUT</c> telling it where to write: <c>page.png</c>, <c>tree.json</c>, <c>calls.json</c> and
/// <c>problems.txt</c>, to which the tool adds <c>tree.txt</c> in <c>spar tree</c>'s form. Every process it starts is in
/// a job object (Windows) or its own process group (Linux), as <c>spar dev</c>'s are.
/// </summary>
internal static class SnapCommand
{
    public const string Usage = """
        Usage: spar snap <scene>... [options]
               spar snap --all [options]
               spar snap --list [options]

        Captures scenes of the app: [SparScene("name")] methods of its end-to-end test project, each run with a fixed
        clock, the light theme and a 1280 x 800 page. Each capture is a folder: page.png, tree.txt and tree.json (the
        accessibility tree), calls.json (the bridge calls) and problems.txt.

        Options:
          --project <path>   The app's project or its end-to-end test project (default: the current folder).
          --out <folder>     Where the scenes' folders go (default: obj/spar-snap/ in the end-to-end project).
          --no-build         Use the end-to-end project's last Debug build.
          --json             The result as JSON.
        Exit codes: 0 written, 2 a scene or the build failed, 64 a wrong command line or an unknown scene.
        """;

    public static int Run(string[] args, TextWriter output, TextWriter error, string workingDirectory)
    {
        var scenes = new List<string>();
        string? project = null;
        string? outFolder = null;
        bool list = false, all = false, json = false, build = true;
        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "-h" or "--help":
                    output.WriteLine(Usage);
                    return ExitCodes.Success;
                case "--project" when index + 1 < args.Length:
                    project = args[++index];
                    break;
                case "--out" when index + 1 < args.Length:
                    outFolder = args[++index];
                    break;
                case "--list":
                    list = true;
                    break;
                case "--all":
                    all = true;
                    break;
                case "--json":
                    json = true;
                    break;
                case "--no-build":
                    build = false;
                    break;
                case var name when !name.StartsWith('-'):
                    scenes.Add(name);
                    break;
                default:
                    return UsageError(error, $"unknown or incomplete option '{args[index]}'.");
            }
        }

        if ((list ? 1 : 0) + (all ? 1 : 0) + (scenes.Count > 0 ? 1 : 0) != 1)
        {
            return UsageError(error, "name the scenes to capture, or give --all or --list.");
        }

        if (SnapProject.Find(Path.Combine(workingDirectory, project ?? "."), out var problem) is not { } tests)
        {
            error.WriteLine($"spar snap: {problem}");
            return ExitCodes.Errors;
        }

        using var children = IChildProcesses.Create(desktop: null);
        var session = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(4));
        var executable = BuildAsync(children, tests, session, build, error).GetAwaiter().GetResult();
        if (executable is null)
        {
            return ExitCodes.Errors;
        }

        var known = ListAsync(children, executable, Path.GetDirectoryName(tests)!).GetAwaiter().GetResult();
        if (list)
        {
            return PrintList(known, json, output);
        }

        var wanted = all ? known : scenes;
        if (wanted.FirstOrDefault(name => !known.Contains(name, StringComparer.Ordinal)) is { } unknown)
        {
            error.WriteLine($"spar snap: there is no scene '{unknown}' in {Path.GetFileName(tests)}; the scenes: {(known.Count == 0 ? "none" : string.Join(", ", known))}.");
            return ExitCodes.Usage;
        }

        var root = Path.GetFullPath(outFolder is null ? Path.Combine(Path.GetDirectoryName(tests)!, "obj", "spar-snap") : Path.Combine(workingDirectory, outFolder));
        var results = new List<SnapResult>();
        foreach (var name in wanted)
        {
            var result = SnapAsync(children, executable, Path.GetDirectoryName(tests)!, name, Path.Combine(root, name)).GetAwaiter().GetResult();
            results.Add(result);
            if (!json)
            {
                output.WriteLine(result.Describe());
                foreach (var line in result.Failure)
                {
                    error.WriteLine("  " + line);
                }
            }
        }

        if (json)
        {
            output.WriteLine(Json(results));
        }

        return results.All(result => result.Written) ? ExitCodes.Success : ExitCodes.Errors;
    }

    private static int UsageError(TextWriter error, string problem)
    {
        error.WriteLine($"spar snap: {problem}");
        error.WriteLine(Usage);
        return ExitCodes.Usage;
    }

    private static async Task<string?> BuildAsync(IChildProcesses children, string tests, string session, bool build, TextWriter error)
    {
        var result = await new AppBuild(children, tests, session, _ => { }).RunAsync(CancellationToken.None, build);
        if (result.Succeeded)
        {
            return result.Executable;
        }

        error.WriteLine(build ? $"spar snap: the build of {Path.GetFileName(tests)} failed:" : $"spar snap: {Path.GetFileName(tests)} has no Debug build to use:");
        foreach (var diagnostic in result.Errors)
        {
            error.WriteLine("  " + (diagnostic.Location.Length > 0 ? diagnostic.Location + ": " : "") + $"error {diagnostic.Code}: {diagnostic.Message}");
        }

        return null;
    }

    private static async Task<IReadOnlyList<string>> ListAsync(IChildProcesses children, string executable, string folder)
    {
        var (_, lines) = await RunAsync(children, executable, ["--spar-scene-list"], folder, new Dictionary<string, string?>());
        return [.. lines.Where(line => line.Length > 0 && !line.Contains(' ', StringComparison.Ordinal))];
    }

    private static async Task<SnapResult> SnapAsync(IChildProcesses children, string executable, string folder, string name, string target)
    {
        if (Directory.Exists(target))
        {
            Directory.Delete(target, recursive: true);
        }

        var environment = new Dictionary<string, string?> { ["SPAR_SNAP_SCENE"] = name, ["SPAR_SNAP_OUT"] = target };
        var (code, lines) = await RunAsync(children, executable, ["-trait", "SparScene=" + name, "-noLogo", "-noColor"], folder, environment);
        var png = Path.Combine(target, "page.png");
        if (code != 0 || !File.Exists(png))
        {
            var failure = lines.SkipWhile(line => !line.Contains("[FAIL]", StringComparison.Ordinal)).Take(30).ToList();
            return new SnapResult(name, target, false, failure.Count > 0 ? failure : [.. lines.TakeLast(20)], 0, 0, 0);
        }

        var nodes = WriteTreeText(target);
        var calls = CountArray(Path.Combine(target, "calls.json"));
        var problems = File.ReadAllLines(Path.Combine(target, "problems.txt")).Count(line => line.Length > 0);
        return new SnapResult(name, target, true, [], nodes, calls, problems);
    }

    /// <summary>tree.txt beside tree.json, in <c>spar tree</c>'s form; returns how many elements the tree has.</summary>
    private static int WriteTreeText(string target)
    {
        using var tree = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(target, "tree.json")));
        using var text = new StringWriter { NewLine = "\n" };
        DevtoolsOutput.Tree(text, tree.RootElement);
        File.WriteAllText(Path.Combine(target, "tree.txt"), text.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return tree.RootElement.TryGetProperty("nodes", out var nodes) ? nodes.GetArrayLength() : 0;
    }

    private static int CountArray(string file)
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(file));
        return document.RootElement.ValueKind == JsonValueKind.Array ? document.RootElement.GetArrayLength() : 0;
    }

    private static async Task<(int Code, List<string> Lines)> RunAsync(
        IChildProcesses children, string executable, IReadOnlyList<string> arguments, string folder, IReadOnlyDictionary<string, string?> environment)
    {
        var lines = new List<string>();
        using var child = children.Start(new ChildStart(executable, arguments, folder, environment), (line, _) =>
        {
            lock (lines)
            {
                lines.Add(line);
            }
        });
        var code = await child.Exited;

        // The output pipes end a moment after the exit.
        await Task.Delay(100, CancellationToken.None);
        lock (lines)
        {
            return (code, [.. lines]);
        }
    }

    private static int PrintList(IReadOnlyList<string> scenes, bool json, TextWriter output)
    {
        if (json)
        {
            var buffer = new ArrayBufferWriter<byte>();
            using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
            {
                writer.WriteStartArray();
                foreach (var scene in scenes)
                {
                    writer.WriteStringValue(scene);
                }

                writer.WriteEndArray();
            }

            output.WriteLine(Encoding.UTF8.GetString(buffer.WrittenSpan));
        }
        else
        {
            output.WriteLine(scenes.Count == 0 ? "no scenes: add [SparScene(\"name\")] methods to the end-to-end test project" : string.Join('\n', scenes));
        }

        return ExitCodes.Success;
    }

    private static string Json(IReadOnlyList<SnapResult> results)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            writer.WriteStartArray();
            foreach (var result in results)
            {
                result.Write(writer);
            }

            writer.WriteEndArray();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }
}

/// <summary>One scene's capture: where it went, or why it failed.</summary>
internal sealed record SnapResult(string Name, string Folder, bool Written, IReadOnlyList<string> Failure, int Nodes, int Calls, int Problems)
{
    public string Describe() => Written
        ? $"{Name}: {Folder} ({Nodes} elements, {Calls} call{(Calls == 1 ? "" : "s")}, {Problems} problem{(Problems == 1 ? "" : "s")})"
        : $"{Name}: failed";

    public void Write(Utf8JsonWriter writer)
    {
        writer.WriteStartObject();
        writer.WriteString("scene", Name);
        writer.WriteBoolean("written", Written);
        writer.WriteString("folder", Folder);
        if (Written)
        {
            writer.WriteNumber("elements", Nodes);
            writer.WriteNumber("calls", Calls);
            writer.WriteNumber("problems", Problems);
        }
        else
        {
            writer.WriteStartArray("failure");
            foreach (var line in Failure)
            {
                writer.WriteStringValue(line);
            }

            writer.WriteEndArray();
        }

        writer.WriteEndObject();
    }
}
