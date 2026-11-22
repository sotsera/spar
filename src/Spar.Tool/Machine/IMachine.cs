using System.Runtime.InteropServices;

namespace Spar.Tool.Machine;

/// <summary>The operating systems the checks tell apart.</summary>
internal enum Platform
{
    Windows,
    Linux,
    Other,
}

/// <summary>
/// Everything the machine checks read from the computer: variables, files, programs they run, and the native answers
/// (the WebView2 loader, shared libraries, the registry). <see cref="RealMachine"/> asks the computer; the tests give a
/// fake, so no check needs anything installed or uninstalled to be tested. The checks only read: nothing here writes.
/// </summary>
internal interface IMachine
{
    Platform Platform { get; }

    Architecture Architecture { get; }

    /// <summary>The user's home folder: <c>USERPROFILE</c> on Windows, <c>HOME</c> elsewhere; empty when unknown.</summary>
    string Home { get; }

    string? Variable(string name);

    bool FileExists(string path);

    bool DirectoryExists(string path);

    /// <summary>The folders directly in <paramref name="path"/>, full paths; empty when it does not exist.</summary>
    IReadOnlyList<string> Directories(string path);

    /// <summary>The files directly in <paramref name="path"/> whose names match <paramref name="pattern"/>; empty when none.</summary>
    IReadOnlyList<string> Files(string path, string pattern);

    /// <summary>A text file's content, or null when it cannot be read.</summary>
    string? ReadText(string path);

    /// <summary>
    /// Runs a program and waits for it (at most <paramref name="timeout"/>, 30 s by default), capturing its output. On
    /// Windows a <c>.cmd</c> program, such as npm, runs through the command interpreter.
    /// </summary>
    ProcessResult Run(string program, IReadOnlyList<string> arguments, string? workingDirectory = null, TimeSpan? timeout = null);

    /// <summary>What the WebView2 loader answers about the Runtime it would start (Windows only).</summary>
    WebView2Answer WebView2();

    /// <summary>
    /// Whether a shared library loads (Linux), and its version when it exports <c>&lt;prefix&gt;get_major_version</c>,
    /// <c>_minor_</c> and <c>_micro_</c> (GTK's and WebKitGTK's convention).
    /// </summary>
    LibraryAnswer Library(string name, string? versionPrefix = null);

    /// <summary>A DWORD value under <c>HKEY_LOCAL_MACHINE</c> (Windows only), or null when there is none.</summary>
    int? MachineRegistryNumber(string key, string name);
}

/// <summary>A program's run: whether it started, its exit code and what it wrote.</summary>
internal sealed record ProcessResult(bool Started, int ExitCode, string Output, string Error)
{
    public static ProcessResult NotStarted(string why) => new(false, -1, "", why);

    public bool Succeeded => Started && ExitCode == 0;
}

/// <summary>The loader's answer: its HRESULT and the version, or that the loader itself is missing.</summary>
internal sealed record WebView2Answer(int Status, string? Version, string? LoaderProblem);

/// <summary>Whether a library loaded, and its version when it reports one.</summary>
internal sealed record LibraryAnswer(bool Loaded, Version? Version);
