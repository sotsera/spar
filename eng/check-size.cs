// Checks the size limits of AGENTS.md ("Boundaries") for C# and TypeScript sources outside spikes/.
// Run it from anywhere in the repository: dotnet run eng/check-size.cs
//
// - A file with more than 800 lines gets a warning.
// - A file with more than 1,500 lines, or a folder with more than 40 source files, is an error.
// - eng/size-exceptions.txt lists the agreed exceptions, one per line, as "<path> <ceiling>".
//   A path that ends in "/" is a folder, and its ceiling counts source files. Any other path is a file, and its
//   ceiling counts lines. Text after "#" is a comment.
// - A ceiling must equal the current size. A size above it is an error. A size below it is an error too, asking
//   for the ceiling to be lowered, so ceilings only go down. An entry that matches nothing is an error.
//
// The exit code is 0 when there are no errors, and 1 otherwise.

using System.Diagnostics;

const int WarnAtLines = 800;
const int FailAtLines = 1500;
const int FailAtFilesPerFolder = 40;
const string ExceptionsFile = "eng/size-exceptions.txt";
const string SkippedRoot = "spikes/";

string[] sourceExtensions = [".cs", ".ts", ".tsx", ".mts", ".cts"];

Directory.SetCurrentDirectory(await RunGitAsync("rev-parse --show-toplevel", trimOutput: true));

var errors = new List<string>();
var warnings = new List<string>();
var ceilings = ReadCeilings(errors);
var unusedCeilings = new HashSet<string>(ceilings.Keys, StringComparer.Ordinal);

var listing = await RunGitAsync("ls-files --cached --others --exclude-standard -z", trimOutput: false);
var sources = listing
    .Split('\0', StringSplitOptions.RemoveEmptyEntries)
    .Where(IsCheckedSource)
    .Where(File.Exists)
    .ToList();

foreach (var source in sources)
{
    var lineCount = File.ReadLines(source).Count();
    CheckSize(source, lineCount, "lines", WarnAtLines, FailAtLines);
}

foreach (var folder in sources.GroupBy(FolderOf, StringComparer.Ordinal))
{
    CheckSize(folder.Key, folder.Count(), "source files", warnAbove: null, FailAtFilesPerFolder);
}

foreach (var path in unusedCeilings.Order(StringComparer.Ordinal))
{
    errors.Add($"{ExceptionsFile}: '{path}' matches no checked file or folder. Remove the entry.");
}

foreach (var warning in warnings)
{
    Console.WriteLine($"warning: {warning}");
}

foreach (var error in errors)
{
    Console.Error.WriteLine($"error: {error}");
}

Console.WriteLine($"Checked {sources.Count} source files: {errors.Count} errors, {warnings.Count} warnings.");
return errors.Count == 0 ? 0 : 1;

bool IsCheckedSource(string path)
{
    if (path.StartsWith(SkippedRoot, StringComparison.Ordinal))
    {
        return false;
    }

    return sourceExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
}

// git prints paths with forward slashes, so the folder is everything up to the last one. Root files are in "./".
static string FolderOf(string path)
{
    var lastSlash = path.LastIndexOf('/');
    return lastSlash < 0 ? "./" : path[..(lastSlash + 1)];
}

void CheckSize(string path, int size, string unit, int? warnAbove, int failAbove)
{
    if (ceilings.TryGetValue(path, out var ceiling))
    {
        unusedCeilings.Remove(path);

        if (size > ceiling)
        {
            errors.Add($"{path}: {size} {unit}, over its agreed ceiling of {ceiling}.");
        }
        else if (size < ceiling)
        {
            errors.Add($"{path}: {size} {unit}. Lower its ceiling in {ExceptionsFile} from {ceiling} to {size}.");
        }

        return;
    }

    if (size > failAbove)
    {
        errors.Add($"{path}: {size} {unit}, over the limit of {failAbove}. Split it, or agree an exception.");
    }
    else if (size > warnAbove)
    {
        warnings.Add($"{path}: {size} {unit}. Keep it under {warnAbove}.");
    }
}

static Dictionary<string, int> ReadCeilings(List<string> errors)
{
    var ceilings = new Dictionary<string, int>(StringComparer.Ordinal);

    if (!File.Exists(ExceptionsFile))
    {
        return ceilings;
    }

    var lineNumber = 0;

    foreach (var line in File.ReadLines(ExceptionsFile))
    {
        lineNumber++;

        var commentStart = line.IndexOf('#');
        var entry = (commentStart < 0 ? line : line[..commentStart]).Trim();

        if (entry.Length == 0)
        {
            continue;
        }

        // The ceiling is the last word, so a path may contain spaces.
        var separator = entry.LastIndexOf(' ');
        var path = separator < 0 ? "" : entry[..separator].Trim();

        if (path.Length == 0 || !int.TryParse(entry[(separator + 1)..], out var ceiling) || ceiling <= 0)
        {
            errors.Add($"{ExceptionsFile}({lineNumber}): expected '<path> <ceiling>'.");
            continue;
        }

        if (!ceilings.TryAdd(path, ceiling))
        {
            errors.Add($"{ExceptionsFile}({lineNumber}): '{path}' is listed twice.");
        }
    }

    return ceilings;
}

static async Task<string> RunGitAsync(string arguments, bool trimOutput)
{
    var startInfo = new ProcessStartInfo("git", arguments)
    {
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
    };

    using var git = Process.Start(startInfo) ?? throw new InvalidOperationException("git did not start.");

    // Read both streams at once: a full, unread stderr pipe would block git.
    var errorText = git.StandardError.ReadToEndAsync();
    var output = await git.StandardOutput.ReadToEndAsync();
    await git.WaitForExitAsync();

    if (git.ExitCode != 0)
    {
        throw new InvalidOperationException($"git {arguments} failed with exit code {git.ExitCode}: {await errorText}");
    }

    return trimOutput ? output.Trim() : output;
}
