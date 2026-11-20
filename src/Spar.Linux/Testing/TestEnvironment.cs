using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using Spar.Hosting;
using Spar.Linux.Interop;

namespace Spar.Linux.Testing;

/// <summary>
/// A Linux test process's own session (plan section 5, "Linux isolation"; decision "M1c step 7's shape: Linux window and
/// black-box tests"), set up once before GTK starts: a private <c>HOME</c> and every <c>XDG_*</c> folder under a root of
/// the process's own in the test homes folder (<c>artifacts/linux-homes/test-host/</c>), <c>XDG_RUNTIME_DIR</c> in
/// <c>/dev/shm</c> (WebKit's sandbox refuses one under <c>/var/tmp</c>), a private D-Bus session, and a private Xvfb on an
/// abstract socket only, with <c>GDK_BACKEND=x11</c> so XTEST can give trusted input. No window of a test ever shows on
/// a desktop. <see cref="SessionDisplayVariable"/> set to <c>session</c> uses the session's own display instead (for
/// watching a test locally; on a machine someone uses, that is their desktop).
/// <para>
/// The helpers are tethered to this process through their standard input: when it closes, at exit or when the process
/// dies, each is ended, so none outlives its tests. A later run deletes the roots and runtime folders whose process is
/// gone.
/// </para>
/// </summary>
internal sealed class TestEnvironment
{
    /// <summary>The setting that uses the session's display instead of a private Xvfb.</summary>
    public const string SessionDisplayVariable = "SPAR_TEST_DISPLAY";

    /// <summary>The private display's size: 25 places of 1280 × 840 for the windows of tests that run at once.</summary>
    private const string Screen = "6400x4200x24";

    /// <summary>The first display number tried for the private Xvfb; numbers below are left to desktops and other runs.</summary>
    private const int FirstDisplay = 140;

    /// <summary>Ends the command in <c>$@</c> once standard input closes: this process exited, or died.</summary>
    private const string Tether = "\"$@\" & child=$!; read -r _; kill -TERM \"$child\" 2>/dev/null; wait \"$child\"";

    private static readonly TimeSpan HelperStart = TimeSpan.FromSeconds(10);
    private static readonly Lock AppendGate = new();

    private readonly List<Process> _helpers = [];

    private TestEnvironment(string root, string runtime)
    {
        Root = root;
        Runtime = runtime;
    }

    /// <summary>This process's root, with the home and XDG folders in it.</summary>
    public string Root { get; }

    /// <summary>This process's <c>XDG_RUNTIME_DIR</c>.</summary>
    public string Runtime { get; }

    /// <summary>The display GTK and the input connection open (<c>:140</c>).</summary>
    public string Display { get; private set; } = "";

    /// <summary>How the display was chosen, for the log.</summary>
    public string DisplayNote { get; private set; } = "";

    /// <summary>
    /// The session for this process, or <see cref="SparTestWindowUnavailableException"/> naming what is missing (Xvfb,
    /// D-Bus, XTEST's library). Changes this process's environment, libc's and .NET's copies alike.
    /// </summary>
    public static TestEnvironment Create(string homesFolder)
    {
        var useSession = string.Equals(Environment.GetEnvironmentVariable(SessionDisplayVariable), "session", StringComparison.Ordinal);
        var xvfb = useSession ? null : Find("Xvfb")
            ?? throw Unavailable("Xvfb was not found on PATH. Install the xvfb package (window tests run on a private Xvfb of their own), "
                + $"or set {SessionDisplayVariable}=session to use this session's display.");
        var bus = Find("dbus-daemon") ?? throw Unavailable("dbus-daemon was not found on PATH: window tests run in a private D-Bus session.");
        if (!NativeLibrary.TryLoad("libXtst.so.6", out _))
        {
            throw Unavailable("libXtst.so.6 could not be loaded: trusted input in window tests goes through XTEST (the libxtst6 package).");
        }

        var session = useSession ? Environment.GetEnvironmentVariable("DISPLAY") : null;
        if (useSession && string.IsNullOrEmpty(session))
        {
            throw Unavailable($"{SessionDisplayVariable}=session needs DISPLAY: the session has no X display.");
        }

        var stamp = $"{DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}-{Environment.ProcessId}";
        Directory.CreateDirectory(homesFolder);
        DeleteLeftovers(homesFolder);
        var root = Path.Combine(homesFolder, stamp);
        var shm = Directory.Exists("/dev/shm") ? "/dev/shm" : Path.GetTempPath();
        DeleteLeftovers(shm, "spar-test-");
        var runtime = Path.Combine(shm, $"spar-test-{stamp}");
        var environment = new TestEnvironment(root, runtime);
        foreach (var folder in new[] { "home", "cache", "config", "data", "state" })
        {
            Directory.CreateDirectory(Path.Combine(root, folder));
        }

        Directory.CreateDirectory(runtime);
        _ = Libc.Chmod(runtime, Convert.ToUInt32("700", 8));

        Set("HOME", Path.Combine(root, "home"));
        Set("XDG_CACHE_HOME", Path.Combine(root, "cache"));
        Set("XDG_CONFIG_HOME", Path.Combine(root, "config"));
        Set("XDG_DATA_HOME", Path.Combine(root, "data"));
        Set("XDG_STATE_HOME", Path.Combine(root, "state"));
        Set("XDG_RUNTIME_DIR", runtime);
        foreach (var name in new[] { "WAYLAND_DISPLAY", "DBUS_SESSION_BUS_ADDRESS", "XDG_ACTIVATION_TOKEN", "DESKTOP_STARTUP_ID", "XDG_CURRENT_DESKTOP" })
        {
            Unset(name);
        }

        try
        {
            var address = environment.StartHelper(bus, ["--session", "--nofork", "--print-address=1"], "dbus")
                ?? throw Unavailable("The private D-Bus session did not start (dbus-daemon printed no address).");
            Set("DBUS_SESSION_BUS_ADDRESS", address);
            if (xvfb is null)
            {
                environment.Display = session!;
                environment.DisplayNote = $"the session's display {session} ({SessionDisplayVariable}=session)";
            }
            else
            {
                environment.StartXvfb(xvfb);
            }
        }
        catch
        {
            environment.Stop();
            throw;
        }

        Set("DISPLAY", environment.Display);
        Set("GDK_BACKEND", "x11");
        return environment;
    }

    /// <summary>At process exit: the helpers end, and this process's folders go.</summary>
    public void Stop()
    {
        foreach (var helper in _helpers)
        {
            try
            {
                helper.StandardInput.Close();
                if (!helper.WaitForExit(2_000))
                {
                    helper.Kill(entireProcessTree: true);
                }
            }
            catch (Exception exception) when (exception is InvalidOperationException or IOException)
            {
                // Gone already.
            }
        }

        TryDelete(Runtime);
        TryDelete(Root);
    }

    private static SparTestWindowUnavailableException Unavailable(string why) => new("Window tests cannot run here: " + why);

    /// <summary>The program on PATH, or null.</summary>
    private static string? Find(string program) =>
        (Environment.GetEnvironmentVariable("PATH") ?? "").Split(':', StringSplitOptions.RemoveEmptyEntries)
            .Select(folder => Path.Combine(folder, program))
            .FirstOrDefault(File.Exists);

    /// <summary>Libc's environment (GLib, GTK, WebKit and every helper read it) and .NET's copy (its Process.Start).</summary>
    private static void Set(string name, string value)
    {
        if (Libc.SetEnv(name, value, 1) != 0)
        {
            throw new InvalidOperationException($"{name} could not be set for the window tests.");
        }

        Environment.SetEnvironmentVariable(name, value);
    }

    private static void Unset(string name)
    {
        _ = Libc.UnsetEnv(name);
        Environment.SetEnvironmentVariable(name, null);
    }

    /// <summary>The private Xvfb on the first free display number from <see cref="FirstDisplay"/>, on an abstract socket only.</summary>
    private void StartXvfb(string xvfb)
    {
        for (var number = FirstDisplay; number < FirstDisplay + 60; number++)
        {
            if (DisplayTaken(number))
            {
                continue;
            }

            // -displayfd prints the number once the server accepts clients; a number another server took meanwhile fails.
            var ready = StartHelper(xvfb, [$":{number}", "-displayfd", "1", "-screen", "0", Screen, "-nolisten", "tcp", "-nolisten", "unix"], $"xvfb-{number}");
            if (ready?.Trim() == number.ToString(CultureInfo.InvariantCulture))
            {
                Display = $":{number}";
                DisplayNote = $"a private Xvfb on display :{number} (abstract socket only, {Screen})";
                return;
            }
        }

        throw Unavailable("Xvfb did not start on any display from :" + FirstDisplay + "; its output is in " + Root + ".");
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

    /// <summary>
    /// Starts <paramref name="program"/> tethered to this process, its output kept in <c>&lt;name&gt;.log</c> under the
    /// root; returns its first line of output, or null when it ended first or printed nothing in time.
    /// </summary>
    private string? StartHelper(string program, string[] arguments, string name)
    {
        var start = new ProcessStartInfo("/bin/sh")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add("-c");
        start.ArgumentList.Add(Tether);
        start.ArgumentList.Add("sh");
        start.ArgumentList.Add(program);
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        var process = Process.Start(start) ?? throw new InvalidOperationException($"{program} could not be started.");
        _helpers.Add(process);
        var log = Path.Combine(Root, name + ".log");
        process.ErrorDataReceived += (_, line) => Append(log, line.Data);
        process.BeginErrorReadLine();
        var first = process.StandardOutput.ReadLineAsync();
        if (!first.Wait(HelperStart) || first.Result is null)
        {
            Append(log, $"(no first line within {HelperStart.TotalSeconds:0} s; exited: {process.HasExited})");
            return null;
        }

        _ = DrainAsync(process.StandardOutput, log);
        return first.Result;
    }

    private static async Task DrainAsync(StreamReader output, string log)
    {
        while (await output.ReadLineAsync() is { } line)
        {
            Append(log, line);
        }
    }

    private static void Append(string log, string? line)
    {
        if (line is null)
        {
            return;
        }

        try
        {
            lock (AppendGate)
            {
                File.AppendAllLines(log, [line]);
            }
        }
        catch (IOException)
        {
            // The root is being deleted at exit.
        }
    }

    /// <summary>The folders under <paramref name="parent"/> named <c>…-&lt;process id&gt;</c> whose process is gone.</summary>
    private static void DeleteLeftovers(string parent, string prefix = "")
    {
        foreach (var folder in Directory.EnumerateDirectories(parent, prefix + "*"))
        {
            var name = Path.GetFileName(folder);
            if (int.TryParse(name.AsSpan(name.LastIndexOf('-') + 1), NumberStyles.None, CultureInfo.InvariantCulture, out var id)
                && id != Environment.ProcessId && !Directory.Exists($"/proc/{id}"))
            {
                TryDelete(folder);
            }
        }
    }

    private static void TryDelete(string folder)
    {
        try
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Another run may be using it; a later one tries again.
        }
    }
}
