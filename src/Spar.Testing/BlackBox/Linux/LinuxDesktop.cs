using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Spar.Testing;

/// <summary>
/// The black box's desktop on Linux, the counterpart of Windows' hidden desktop (decision "M1c step 7's shape: Linux
/// window and black-box tests"): one private Xvfb per test process, on an abstract socket only, from display :180 (the
/// Window surface's own starts at :140), and a private D-Bus session per launched app, which activates at-spi2-core's
/// accessibility bus and its registry for that app alone. Both are started with <c>KillOnParentExit</c>, so neither
/// outlives the test process however it ends; the activated services end with their bus. Nothing here uses the session's
/// own display or bus: a launched app never shows on a desktop someone uses.
/// <para>
/// What the black box needs and the machine lacks is <see cref="Problem"/>: the tests skip with it, and fail when
/// <c>CI=true</c>.
/// </para>
/// </summary>
[SupportedOSPlatform("linux")]
internal static class LinuxDesktop
{
    /// <summary>The first display number tried; the Window surface's private Xvfbs take :140 and the ones after it.</summary>
    private const int FirstDisplay = 180;

    private const string Screen = "3840x2160x24";

    private static readonly TimeSpan HelperStart = TimeSpan.FromSeconds(10);
    private static readonly Lock Gate = new();
    private static readonly Lazy<string?> Missing = new(FindProblem);
    private static string? s_display;
    private static string? s_inputDisplay;

    /// <summary>Why launched apps cannot be read here (a missing Xvfb, D-Bus or at-spi2-core), or null when they can.</summary>
    public static string? Problem => Missing.Value;

    /// <summary>This test process's private display (<c>:180</c>…), started at the first launch.</summary>
    public static string Display
    {
        get
        {
            lock (Gate)
            {
                if (s_display is null)
                {
                    CleanRuntimeFolders();
                }

                return s_display ??= StartXvfb();
            }
        }
    }

    /// <summary>
    /// Another private Xvfb of this test process, for Spar's own tests that send real input (XTEST) to the window under
    /// the pointer or read its pixels: no other test's window can be mapped over theirs. Started at the first use.
    /// </summary>
    public static string InputDisplay
    {
        get
        {
            lock (Gate)
            {
                return s_inputDisplay ??= StartXvfb();
            }
        }
    }

    /// <summary>
    /// A private session bus for one launch, with <paramref name="environment"/> (the app's own, so its activated services
    /// share the app's folders), ended with the test process; its address is the process's first line of output.
    /// </summary>
    public static (Process Daemon, string Address) StartBus(IReadOnlyDictionary<string, string> environment)
    {
        var daemon = StartHelper(Find("dbus-daemon")!, ["--session", "--nofork", "--nopidfile", "--print-address=1"], environment, out var first);
        if (first is not { Length: > 0 } address)
        {
            Stop(daemon);
            throw new InvalidOperationException("The black box's private D-Bus session did not start (dbus-daemon printed no address).");
        }

        return (daemon, address);
    }

    /// <summary>Ends a helper and waits for it briefly.</summary>
    public static void Stop(Process helper)
    {
        try
        {
            if (!helper.HasExited)
            {
                helper.Kill(entireProcessTree: true);
                helper.WaitForExit(2_000);
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // Gone already.
        }
        finally
        {
            helper.Dispose();
        }
    }

    /// <summary>The program on <c>PATH</c>, or null.</summary>
    public static string? Find(string program) =>
        (Environment.GetEnvironmentVariable("PATH") ?? "").Split(':', StringSplitOptions.RemoveEmptyEntries)
            .Select(folder => Path.Combine(folder, program))
            .FirstOrDefault(File.Exists);

    private static string? FindProblem()
    {
        const string Prefix = "The black box cannot read a launched app here: ";
        if (Find("Xvfb") is null)
        {
            return Prefix + "Xvfb was not found on PATH (the xvfb package): launched apps show on a private Xvfb of the test process's own.";
        }

        if (Find("dbus-daemon") is null)
        {
            return Prefix + "dbus-daemon was not found on PATH (the dbus-daemon package): each launched app gets a private D-Bus session.";
        }

        var dataDirectories = Environment.GetEnvironmentVariable("XDG_DATA_DIRS") is { Length: > 0 } directories ? directories : "/usr/local/share:/usr/share";
        var service = dataDirectories.Split(':', StringSplitOptions.RemoveEmptyEntries)
            .Select(folder => Path.Combine(folder, "dbus-1", "services", "org.a11y.Bus.service"))
            .FirstOrDefault(File.Exists);
        if (service is null)
        {
            return Prefix + "no D-Bus session service provides org.a11y.Bus (the at-spi2-core package): the app's windows are read over AT-SPI.";
        }

        if (!NativeLibrary.TryLoad("libgio-2.0.so.0", out _))
        {
            return Prefix + "libgio-2.0.so.0 could not be loaded: the accessibility client talks D-Bus through GIO.";
        }

        return null;
    }

    /// <summary>The private Xvfb on the first free display number from <see cref="FirstDisplay"/>, on an abstract socket only.</summary>
    private static string StartXvfb()
    {
        var xvfb = Find("Xvfb") ?? throw new InvalidOperationException(Problem);
        var environment = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            environment[(string)entry.Key] = (string?)entry.Value ?? "";
        }

        for (var number = FirstDisplay; number < FirstDisplay + 60; number++)
        {
            if (DisplayTaken(number))
            {
                continue;
            }

            // -displayfd prints the number once the server accepts clients; a number another server took meanwhile fails.
            // -noreset: by default the server resets when its last client leaves, and a connection made meanwhile is
            // refused; launched apps come and go with no other client, so an app's GTK could not open the display
            // (measured in the CI container: 42 of 3,000 back-to-back opens failed, none with -noreset; pitfalls).
            var server = StartHelper(xvfb, [$":{number}", "-displayfd", "1", "-noreset", "-screen", "0", Screen, "-nolisten", "tcp", "-nolisten", "unix"], environment, out var ready);
            if (ready?.Trim() == number.ToString(CultureInfo.InvariantCulture))
            {
                return $":{number}";
            }

            Stop(server);
        }

        throw new InvalidOperationException($"The black box's Xvfb did not start on any display from :{FirstDisplay}.");
    }


    /// <summary>
    /// The launches' runtime folders (<c>/dev/shm/spar-bb-&lt;root&gt;-&lt;test pid&gt;</c>): a root several launches share
    /// (a test that keeps its data and starts again) leaves its folder to whoever deletes the root, which may be the test
    /// itself. So this test process deletes its own at exit (its apps end with it), and the first launch of a later one
    /// deletes those of test processes that are gone. Only folders are deleted: no process is ended at exit.
    /// </summary>
    private static void CleanRuntimeFolders()
    {
        DeleteRuntimeFolders(id => id != Environment.ProcessId && !Directory.Exists($"/proc/{id}"));
        AppDomain.CurrentDomain.ProcessExit += (_, _) => DeleteRuntimeFolders(id => id == Environment.ProcessId);
    }

    private static void DeleteRuntimeFolders(Func<int, bool> whose)
    {
        if (!Directory.Exists("/dev/shm"))
        {
            return;
        }

        foreach (var folder in Directory.EnumerateDirectories("/dev/shm", "spar-bb-*"))
        {
            var name = Path.GetFileName(folder);
            if (!int.TryParse(name.AsSpan(name.LastIndexOf('-') + 1), NumberStyles.None, CultureInfo.InvariantCulture, out var id) || !whose(id))
            {
                continue;
            }

            try
            {
                Directory.Delete(folder, recursive: true);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // In use, or not this user's: a later run tries again.
            }
        }
    }

    /// <summary>Whether a server holds the display: its abstract or path socket, or its lock file.</summary>
    private static bool DisplayTaken(int number)
    {
        var socket = $"/tmp/.X11-unix/X{number}";
        if (File.Exists($"/tmp/.X{number}-lock") || File.Exists(socket))
        {
            return true;
        }

        try
        {
            return File.ReadLines("/proc/net/unix").Any(line => line.EndsWith("@" + socket, StringComparison.Ordinal));
        }
        catch (IOException)
        {
            return false;
        }
    }

    /// <summary>Starts a helper that ends with the test process; <paramref name="first"/> is its first line of output.</summary>
    private static Process StartHelper(string program, string[] arguments, IReadOnlyDictionary<string, string> environment, out string? first)
    {
        var start = new ProcessStartInfo(program)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            KillOnParentExit = true,
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        start.Environment.Clear();
        foreach (var (name, value) in environment)
        {
            start.Environment[name] = value;
        }

        var process = Process.Start(start) ?? throw new InvalidOperationException($"{program} could not be started.");
        process.ErrorDataReceived += (_, _) => { };
        process.BeginErrorReadLine();
        var line = process.StandardOutput.ReadLineAsync();
        first = line.Wait(HelperStart) ? line.Result : null;
        _ = DrainAsync(process.StandardOutput);
        return process;
    }

    private static async Task DrainAsync(StreamReader output)
    {
        try
        {
            while (await output.ReadLineAsync() is not null)
            {
            }
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException)
        {
            // The helper ended.
        }
    }
}
