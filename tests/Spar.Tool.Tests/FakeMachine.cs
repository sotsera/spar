using System.Runtime.InteropServices;
using Spar.Tool.Machine;

namespace Spar.Tool.Tests;

/// <summary>
/// A machine made of what a test says it has: variables, files (their folders follow), text, programs' answers, the
/// WebView2 loader's answer, libraries and registry values. Paths are compared with <c>/</c>, whatever the OS the
/// tests run on, so a test of Linux's checks runs on Windows too.
/// </summary>
internal sealed class FakeMachine : IMachine
{
    private readonly HashSet<string> _files = new(StringComparer.Ordinal);

    public Platform Platform { get; init; } = Platform.Linux;

    public Architecture Architecture { get; init; } = Architecture.X64;

    public string Home { get; init; } = "/home/user";

    public Dictionary<string, string> Variables { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, string> Texts { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, LibraryAnswer> Libraries { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, int> Registry { get; } = new(StringComparer.Ordinal);

    /// <summary>The programs' answers by program and first argument (<c>node --version</c>); not started otherwise.</summary>
    public Dictionary<string, ProcessResult> Programs { get; } = new(StringComparer.Ordinal);

    public WebView2Answer WebView2Answer { get; set; } = new(unchecked((int)0x80070002), null, null);

    /// <summary>Every program the checks ran, with its arguments.</summary>
    public List<string> Ran { get; } = [];

    public FakeMachine WithFile(params string[] paths)
    {
        foreach (var path in paths)
        {
            _files.Add(Normal(path));
        }

        return this;
    }

    public FakeMachine WithProgram(string path, string firstArgument, ProcessResult result)
    {
        WithFile(path);
        Programs[Normal(path) + " " + firstArgument] = result;
        return this;
    }

    public string? Variable(string name) => Variables.TryGetValue(name, out var value) ? value : null;

    public bool FileExists(string path) => _files.Contains(Normal(path));

    public bool DirectoryExists(string path)
    {
        var folder = Normal(path).TrimEnd('/') + "/";
        return _files.Any(file => file.StartsWith(folder, StringComparison.Ordinal));
    }

    public IReadOnlyList<string> Directories(string path)
    {
        var folder = Normal(path).TrimEnd('/') + "/";
        return [.. _files
            .Where(file => file.StartsWith(folder, StringComparison.Ordinal) && file.IndexOf('/', folder.Length) > 0)
            .Select(file => folder + file[folder.Length..file.IndexOf('/', folder.Length)])
            .Distinct()];
    }

    public IReadOnlyList<string> Files(string path, string pattern)
    {
        var folder = Normal(path).TrimEnd('/') + "/";
        var prefix = pattern.TrimEnd('*');
        return [.. _files.Where(file => file.StartsWith(folder + prefix, StringComparison.Ordinal) && file.IndexOf('/', folder.Length) < 0)];
    }

    public string? ReadText(string path) => Texts.TryGetValue(Normal(path), out var text) ? text : null;

    public ProcessResult Run(string program, IReadOnlyList<string> arguments, string? workingDirectory = null, TimeSpan? timeout = null)
    {
        Ran.Add(Normal(program) + " " + string.Join(' ', arguments));
        return Programs.TryGetValue(Normal(program) + " " + (arguments.Count > 0 ? arguments[0] : ""), out var result)
            ? result
            : ProcessResult.NotStarted($"{program} is not on this fake machine.");
    }

    public WebView2Answer WebView2() => WebView2Answer;

    public LibraryAnswer Library(string name, string? versionPrefix = null) =>
        Libraries.TryGetValue(name, out var answer) ? answer : new LibraryAnswer(false, null);

    public int? MachineRegistryNumber(string key, string name) => Registry.TryGetValue(key + "\\" + name, out var value) ? value : null;

    public static ProcessResult Says(string output, int exitCode = 0) => new(true, exitCode, output, "");

    private static string Normal(string path) => path.Replace('\\', '/');
}
