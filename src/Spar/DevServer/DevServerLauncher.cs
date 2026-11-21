using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Microsoft.Win32.SafeHandles;

namespace Spar.DevServer;

/// <summary>
/// Starts the frontend's own <c>dev</c> script, <c>npm run dev</c> in the frontend folder (decision "M1d step 1's shape":
/// the app's <c>package.json</c> decides the command), and ends a server as a whole tree. On Windows npm is a batch
/// file, so the process Spar starts is <c>cmd.exe</c>, then npm's Node, then the script's shell and Vite's Node; on Linux
/// npm's Node, then the script's shell and Vite. Spar never parses that chain's output for the address: the Spar Vite
/// plugin records it (<see cref="DevServerState"/>).
/// <list type="bullet">
/// <item>The server outlives the app, so a backend restart reuses it: no kill-on-exit job, no tie to the app's process.</item>
/// <item>Its output goes to a file (<see cref="DevServerSettings.OutputFile"/>), never to a pipe of the app's, so it never
/// writes into a closed pipe after the app is gone; every run that uses the server reads the file.</item>
/// <item>Its standard input is the app's own when that is a pipe, so the server ends with the launcher's input (an
/// agent's or a tool's pipe), as Vite does when its input ends. Otherwise (a console, an IDE's null device) it is a pipe
/// whose writing end the launched process holds itself, so it never ends there, and the plugin's idle watchdog ends the
/// server instead. The server never reads the console, where its keyboard shortcuts would take the developer's keys.</item>
/// </list>
/// </summary>
internal static class DevServerLauncher
{
    /// <summary>The variables the Spar Vite plugin reads (src/Spar.Client/src/vite/devServer.ts).</summary>
    public const string StateFileVariable = "SPAR_DEV_SERVER_FILE";

    public const string PortVariable = "SPAR_DEV_SERVER_PORT";

    public const string InstanceVariable = "SPAR_DEV_SERVER_INSTANCE";

    public const string IdleVariable = "SPAR_DEV_SERVER_IDLE_SECONDS";

    /// <summary>The Node.js download page, which the missing-Node message names.</summary>
    public const string NodeHelpUrl = "https://nodejs.org/en/download";

    /// <summary>
    /// Spar's check before the window, when the app is to start the server: npm and Node on <c>PATH</c>. Without them
    /// the app stops with a message that says how to install Node or how to embed the page instead.
    /// </summary>
    public static StartupProblem? CheckNode()
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (FindOnPath(NpmName, path, File.Exists) is not null && FindOnPath(NodeName, path, File.Exists) is not null)
        {
            return null;
        }

        return new StartupProblem(
            StartupProblemKind.Missing,
            "Node.js",
            null,
            null,
            "This Debug build serves its page from the frontend's Vite development server, which needs Node.js and npm on " +
            "PATH, and they were not found there. Install Node.js (the LTS release, with npm) and start the app again, or build " +
            "with -p:SparDevServer=false to embed the page as a Release build does, without hot reload.",
            NodeHelpUrl);
    }

    /// <summary>The first file named <paramref name="name"/> in a folder of <paramref name="path"/>, in order; null when none is.</summary>
    internal static string? FindOnPath(string name, string? path, Func<string, bool> exists)
    {
        foreach (var folder in (path ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var candidate = Path.Combine(folder.Trim('"'), name);
            if (exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>A free port on 127.0.0.1, which the server takes with <c>strictPort</c>; a race with another program fails the start visibly.</summary>
    public static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    /// <summary>
    /// Starts <c>npm run dev</c> in the frontend folder, its output appended to <paramref name="output"/>, with the
    /// variables the plugin reads. Returns the started launcher, which the caller watches but never ties to the app.
    /// </summary>
    public static Process Start(DevServerSettings settings, int port, string instance, SafeFileHandle output, int idleSeconds)
    {
        var npm = FindOnPath(NpmName, Environment.GetEnvironmentVariable("PATH"), File.Exists)
            ?? throw new InvalidOperationException("npm is not on PATH.");
        var start = OperatingSystem.IsWindows()
            ? new ProcessStartInfo(CommandProcessor()) { Arguments = $"/d /s /c \"\"{npm}\" run dev\"" }
            : new ProcessStartInfo(npm, ["run", "dev"]);
        start.WorkingDirectory = settings.FrontendDirectory;
        start.UseShellExecute = false;
        start.CreateNoWindow = true;
        start.StandardOutputHandle = output;
        start.StandardErrorHandle = output;
        start.Environment[StateFileVariable] = settings.StateFile;
        start.Environment[PortVariable] = port.ToString(System.Globalization.CultureInfo.InvariantCulture);
        start.Environment[InstanceVariable] = instance;
        start.Environment[IdleVariable] = idleSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);

        // Colour codes and npm's update notice would only clutter the log; an address Spar did not start is not this
        // server's business.
        start.Environment["NO_COLOR"] = "1";
        start.Environment["npm_config_update_notifier"] = "false";
        start.Environment.Remove(DevServerSettings.UrlVariable);

        using var input = StandardInput(out var writer);
        start.StandardInputHandle = input;
        if (writer is not null)
        {
            start.InheritedHandles = [writer];
        }

        try
        {
            return Process.Start(start) ?? throw new InvalidOperationException("npm run dev did not start.");
        }
        finally
        {
            writer?.Dispose();
        }
    }

    /// <summary>
    /// Whether process <paramref name="processId"/> is still the one that started at <paramref name="startedUnixMilliseconds"/>
    /// (within 5 s, since the plugin's clock and the system's differ a little): a reused id is another process.
    /// </summary>
    public static bool IsAlive(int processId, long startedUnixMilliseconds)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            if (process.HasExited)
            {
                return false;
            }

            var started = new DateTimeOffset(process.StartTime.ToUniversalTime()).ToUnixTimeMilliseconds();
            return Math.Abs(started - startedUnixMilliseconds) < 5_000;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or Win32Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Ends the server as a whole tree: Vite's process and every process below it (its bundler's workers), after which
    /// npm and its wrappers above it end on their own; and the launcher's tree, when this run started it.
    /// </summary>
    public static void Stop(DevServerState? state, Process? launcher)
    {
        if (state is not null && IsAlive(state.ProcessId, state.StartedUnixMilliseconds))
        {
            try
            {
                using var vite = Process.GetProcessById(state.ProcessId);
                Kill(vite);
            }
            catch (ArgumentException)
            {
                // Already gone.
            }
        }

        if (launcher is not null)
        {
            Kill(launcher);
        }
    }

    private static string NpmName => OperatingSystem.IsWindows() ? "npm.cmd" : "npm";

    private static string NodeName => OperatingSystem.IsWindows() ? "node.exe" : "node";

    private static string CommandProcessor() =>
        Environment.GetEnvironmentVariable("ComSpec") is { Length: > 0 } comSpec
            ? comSpec
            : Path.Combine(Environment.SystemDirectory, "cmd.exe");

    /// <summary>
    /// The server's standard input: the app's own when it is a pipe; otherwise a new pipe whose writing end
    /// (<paramref name="writer"/>) the launched process inherits, so its input never ends while it runs.
    /// </summary>
    private static SafeFileHandle StandardInput(out SafeFileHandle? writer)
    {
        var own = Console.OpenStandardInputHandle();
        if (!own.IsInvalid && own.Type is FileHandleType.Pipe or FileHandleType.Socket)
        {
            writer = null;
            return own;
        }

        own.Dispose();
        SafeFileHandle.CreateAnonymousPipe(out var reader, out var write, false, false);
        writer = write;
        return reader;
    }

    private static void Kill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception or AggregateException)
        {
            // Already gone, or a process of the tree ended while it was being ended.
        }
    }
}
