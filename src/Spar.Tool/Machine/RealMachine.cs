using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Spar.Tool.Machine;

/// <summary>The computer the tool runs on, read only: nothing here writes or installs anything.</summary>
internal sealed partial class RealMachine : IMachine
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    public Platform Platform { get; } =
        OperatingSystem.IsWindows() ? Platform.Windows : OperatingSystem.IsLinux() ? Platform.Linux : Platform.Other;

    public Architecture Architecture => RuntimeInformation.OSArchitecture;

    public string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public string? Variable(string name) => Environment.GetEnvironmentVariable(name);

    public bool FileExists(string path) => File.Exists(path);

    public bool DirectoryExists(string path) => Directory.Exists(path);

    public IReadOnlyList<string> Directories(string path)
    {
        try
        {
            return Directory.Exists(path) ? [.. Directory.EnumerateDirectories(path)] : [];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    public IReadOnlyList<string> Files(string path, string pattern)
    {
        try
        {
            return Directory.Exists(path) ? [.. Directory.EnumerateFiles(path, pattern)] : [];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    public string? ReadText(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public ProcessResult Run(string program, IReadOnlyList<string> arguments, string? workingDirectory = null, TimeSpan? timeout = null)
    {
        var start = Processes.StartInfo(program, arguments);
        start.WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory;
        start.RedirectStandardOutput = true;
        start.RedirectStandardError = true;
        start.RedirectStandardInput = false;
        try
        {
            var result = Process.RunAndCaptureText(start, timeout ?? DefaultTimeout);
            if (result.ExitStatus.Canceled)
            {
                return new ProcessResult(true, -1, result.StandardOutput, $"{program} did not finish within {(timeout ?? DefaultTimeout).TotalSeconds:0} s.");
            }

            return new ProcessResult(true, result.ExitStatus.ExitCode, result.StandardOutput, result.StandardError);
        }
        catch (Exception exception) when (exception is Win32Exception or FileNotFoundException or InvalidOperationException)
        {
            return ProcessResult.NotStarted(exception.Message);
        }
    }

    public WebView2Answer WebView2()
    {
        if (!OperatingSystem.IsWindows())
        {
            return new WebView2Answer(-1, null, "not Windows");
        }

        try
        {
            var status = GetAvailableCoreWebView2BrowserVersionString(null, out var version);
            string? found = null;
            if (version != 0)
            {
                found = Marshal.PtrToStringUni(version);
                Marshal.FreeCoTaskMem(version);
            }

            return new WebView2Answer(status, found, null);
        }
        catch (DllNotFoundException exception)
        {
            return new WebView2Answer(-1, null, exception.Message);
        }
    }

    public LibraryAnswer Library(string name, string? versionPrefix = null)
    {
        if (!NativeLibrary.TryLoad(name, out var handle))
        {
            return new LibraryAnswer(false, null);
        }

        try
        {
            return new LibraryAnswer(true, versionPrefix is null ? null : NativeVersion.Read(handle, versionPrefix));
        }
        finally
        {
            NativeLibrary.Free(handle);
        }
    }

    public int? MachineRegistryNumber(string key, string name) =>
        OperatingSystem.IsWindows() ? ReadRegistryNumber(key, name) : null;

    [SupportedOSPlatform("windows")]
    private static int? ReadRegistryNumber(string key, string name)
    {
        using var opened = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(key);
        return opened?.GetValue(name) is int value ? value : null;
    }

    /// <summary>
    /// The version of the Runtime the loader would start, as the Windows host reads it before its window (never from the
    /// registry); <c>0x80070002</c> when there is none. <paramref name="versionInfo"/> is a string the caller frees.
    /// </summary>
    [LibraryImport("WebView2Loader", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int GetAvailableCoreWebView2BrowserVersionString(string? browserExecutableFolder, out nint versionInfo);
}

/// <summary>GTK's and WebKitGTK's version, read from a loaded library's <c>&lt;prefix&gt;get_major_version</c> and the others.</summary>
internal static class NativeVersion
{
    public static unsafe Version? Read(nint library, string prefix)
    {
        if (!NativeLibrary.TryGetExport(library, prefix + "get_major_version", out var major)
            || !NativeLibrary.TryGetExport(library, prefix + "get_minor_version", out var minor)
            || !NativeLibrary.TryGetExport(library, prefix + "get_micro_version", out var micro))
        {
            return null;
        }

        var majorValue = ((delegate* unmanaged<uint>)major)();
        var minorValue = ((delegate* unmanaged<uint>)minor)();
        var microValue = ((delegate* unmanaged<uint>)micro)();
        return new Version((int)majorValue, (int)minorValue, (int)microValue);
    }
}
