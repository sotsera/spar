using System.Globalization;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;

namespace Integ.Linux;

/// <summary>
/// Question 7: a second launch hands its arguments to the first instance.
/// <list type="bullet">
/// <item><c>--mech gapp</c>: GApplication uniqueness over D-Bus (the mechanism GtkApplication uses). The primary owns
/// the application id on the session bus; a secondary forwards its command line and exits with the primary's status.</item>
/// <item><c>--mech socket</c>: an flock'd lock file plus a Unix socket in $XDG_RUNTIME_DIR (mode 0600). A secondary
/// sends its arguments and waits for an acknowledgement with its own timeout; a refused connection while the lock is
/// held means the primary is exiting, so it waits for the lock and becomes the primary.</item>
/// </list>
/// Options: --app-id ID, --hold-ms N (primary lifetime), --slow-exit-ms N (primary keeps the name or lock while
/// exiting), --ack-timeout-ms N (socket secondary). Arguments after "--" are the ones forwarded.
/// </summary>
internal static unsafe class SingleInstance
{
    private static nint s_app;
    private static int s_holdMs;
    private static int s_slowExitMs;
    private static bool s_held;

    public static int Run(string[] args, Func<string, string?> option)
    {
        var appId = option("--app-id") ?? "org.spar.IntegProbe";
        s_holdMs = int.Parse(option("--hold-ms") ?? "6000", CultureInfo.InvariantCulture);
        s_slowExitMs = int.Parse(option("--slow-exit-ms") ?? "0", CultureInfo.InvariantCulture);
        var separator = Array.IndexOf(args, "--");
        var forwarded = separator < 0 ? [] : args[(separator + 1)..];
        return (option("--mech") ?? "gapp") == "socket"
            ? RunSocket(appId, forwarded, int.Parse(option("--ack-timeout-ms") ?? "2000", CultureInfo.InvariantCulture))
            : RunGApplication(appId, forwarded);
    }

    // ---- GApplication ----

    private static int RunGApplication(string appId, string[] forwarded)
    {
        s_app = Gio.g_application_new(appId, Gio.ApplicationHandlesCommandLine);
        GObject.Connect(s_app, "command-line", (delegate* unmanaged<nint, nint, nint, int>)&OnCommandLine);
        GObject.Connect(s_app, "shutdown", (delegate* unmanaged<nint, nint, void>)&OnShutdown);
        var argv = new List<string> { "integ" };
        argv.AddRange(forwarded);
        var pointers = (nint*)NativeMemory.AllocZeroed((nuint)(argv.Count + 1), (nuint)sizeof(nint));
        for (var i = 0; i < argv.Count; i++)
        {
            pointers[i] = Marshal.StringToCoTaskMemUTF8(argv[i]);
        }

        Log.Mark("gapp run");
        var status = Gio.g_application_run(s_app, argv.Count, (nint)pointers);
        Log.Mark($"gapp run returned status={status}");
        GObject.g_object_unref(s_app);
        return status;
    }

    [UnmanagedCallersOnly]
    private static int OnCommandLine(nint app, nint commandLine, nint data)
    {
        var strings = Gio.g_application_command_line_get_arguments(commandLine, out var argc);
        var list = new List<string>();
        for (var i = 1; i < argc; i++)
        {
            list.Add(Marshal.PtrToStringUTF8(((nint*)strings)[i]) ?? "");
        }

        GLib.g_strfreev(strings);
        var remote = Gio.g_application_command_line_get_is_remote(commandLine);
        Log.Mark($"gapp command-line remote={remote} args=[{string.Join(' ', list)}]");
        if (!s_held)
        {
            s_held = true;
            Gio.g_application_hold(app);
            GLib.g_timeout_add_full(0, (uint)s_holdMs, &OnHoldExpired, 0, 0);
            Log.Mark($"gapp primary holding for {s_holdMs} ms");
        }

        return remote != 0 ? 7 : 0;
    }

    [UnmanagedCallersOnly]
    private static int OnHoldExpired(nint data)
    {
        Log.Mark("gapp primary releasing");
        Gio.g_application_release(s_app);
        return 0;
    }

    [UnmanagedCallersOnly]
    private static void OnShutdown(nint app, nint data)
    {
        if (s_held && s_slowExitMs > 0)
        {
            Log.Mark($"gapp shutdown: blocking {s_slowExitMs} ms while still owning the name");
            Thread.Sleep(s_slowExitMs);
        }

        Log.Mark("gapp shutdown");
    }

    // ---- Lock file + Unix socket ----

    private static int RunSocket(string appId, string[] forwarded, int ackTimeoutMs)
    {
        var runtime = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
        if (string.IsNullOrEmpty(runtime))
        {
            Log.Mark("socket: no XDG_RUNTIME_DIR");
            return 2;
        }

        var lockPath = Path.Combine(runtime, appId + ".lock");
        var socketPath = Path.Combine(runtime, appId + ".sock");
        var deadline = Log.Monotonic + 3000;
        while (true)
        {
            var lockFile = TryLock(lockPath);
            if (lockFile is not null)
            {
                return RunPrimary(lockFile, socketPath, forwarded);
            }

            var outcome = TrySend(socketPath, forwarded, ackTimeoutMs);
            if (outcome is not null)
            {
                return outcome.Value;
            }

            if (Log.Monotonic > deadline)
            {
                Log.Mark("socket: the lock stays held and nobody answers; giving up");
                return 5;
            }

            Thread.Sleep(20);
        }
    }

    private static FileStream? TryLock(string path)
    {
        try
        {
            // FileShare.None is an exclusive flock on Linux; it is released when the process ends, even on a crash.
            var stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            return stream;
        }
        catch (IOException)
        {
            return null;
        }
    }

    /// <summary>0 when the primary acknowledged; 4 when it accepted but never answered; null when nobody listens.</summary>
    private static int? TrySend(string socketPath, string[] forwarded, int ackTimeoutMs)
    {
        using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        var started = Log.Monotonic;
        try
        {
            socket.Connect(new UnixDomainSocketEndPoint(socketPath));
        }
        catch (SocketException exception)
        {
            Log.Mark($"socket: connect failed ({exception.SocketErrorCode}); the primary is starting or exiting");
            return null;
        }

        socket.Send(Encoding.UTF8.GetBytes(string.Join('\u001f', forwarded) + "\n"));
        socket.ReceiveTimeout = ackTimeoutMs;
        var buffer = new byte[16];
        try
        {
            var read = socket.Receive(buffer);
            Log.Mark(string.Create(CultureInfo.InvariantCulture, $"socket: forwarded, ack '{Encoding.UTF8.GetString(buffer, 0, read).Trim()}' after {Log.Monotonic - started:F1} ms"));
            return read > 0 ? 0 : null;
        }
        catch (SocketException exception) when (exception.SocketErrorCode is SocketError.TimedOut or SocketError.WouldBlock)
        {
            Log.Mark(string.Create(CultureInfo.InvariantCulture, $"socket: the primary accepted but did not answer within {ackTimeoutMs} ms (hung?); not starting a second primary"));
            return 4;
        }
    }

    private static int RunPrimary(FileStream lockFile, string socketPath, string[] forwarded)
    {
        File.Delete(socketPath); // a stale socket from a crashed primary; the lock proves nobody owns it
        using var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        listener.Bind(new UnixDomainSocketEndPoint(socketPath));
        File.SetUnixFileMode(socketPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        listener.Listen(8);
        Log.Mark($"socket primary listening args=[{string.Join(' ', forwarded)}]");
        var stop = Log.Monotonic + s_holdMs;
        listener.Blocking = false;
        while (Log.Monotonic < stop)
        {
            if (listener.Poll(20_000, SelectMode.SelectRead))
            {
                using var client = listener.Accept();
                client.Blocking = true;
                client.ReceiveTimeout = 1000;
                var buffer = new byte[4096];
                var read = client.Receive(buffer);
                var text = Encoding.UTF8.GetString(buffer, 0, read).TrimEnd('\n');
                Log.Mark($"socket primary received args=[{text.Replace('\u001f', ' ')}]");
                try
                {
                    client.Send("ok\n"u8);
                }
                catch (SocketException exception)
                {
                    // The secondary gave up (its own timeout) before the answer: it has already reported a failure.
                    Log.Mark($"socket primary: the secondary is gone ({exception.SocketErrorCode}); its arguments were handled anyway");
                }
            }
        }

        listener.Close();
        File.Delete(socketPath);
        if (s_slowExitMs > 0)
        {
            Log.Mark($"socket primary exiting slowly ({s_slowExitMs} ms) with the socket closed and the lock held");
            Thread.Sleep(s_slowExitMs);
        }

        lockFile.Dispose();
        Log.Mark("socket primary exited");
        return 0;
    }
}
