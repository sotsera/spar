using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using Spar.SingleInstance;
using static Spar.SingleInstance.Native;

var options = Options.Parse(args);
Log.Open(options.Value("--log"), options.Verb);
Log.Mark($"start {string.Join(' ', args)}");

var code = options.Verb switch
{
    "run" => Instance.Run(options),
    "squat" => Squat(options),
    "probe" => Probe(options),
    "suspend" => SuspendOrResume(options, suspend: true),
    "resume" => SuspendOrResume(options, suspend: false),
    "launcher" => Launcher(options),
    "click" => Click(options),
    "spawn" => Spawn(options),
    _ => 2,
};
Log.Mark($"exit {code}");
return code;

// Starts secondaries (sequentially, or all at once with --parallel) and records each exit code and wall time.
static int Spawn(Options options)
{
    var exe = Environment.ProcessPath!;
    var count = options.Number("--count", 1);
    var gap = options.Number("--gap-ms", 100);
    var parallel = options.Value("--parallel") is not null;
    var running = new List<(Process Process, long Started, int Index)>();
    for (var i = 0; i < count; i++)
    {
        var info = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in options.Forwarded)
        {
            info.ArgumentList.Add(argument.Replace("{i}", i.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal));
        }

        var started = Log.Ticks;
        info.ArgumentList.Insert(1, "--t0");
        info.ArgumentList.Insert(2, started.ToString(System.Globalization.CultureInfo.InvariantCulture));
        var process = Process.Start(info)!;
        running.Add((process, started, i));
        if (!parallel)
        {
            Finish(running[^1]);
            Thread.Sleep(gap);
        }
    }

    if (parallel)
    {
        foreach (var entry in running)
        {
            Finish(entry);
        }
    }

    return 0;

    static void Finish((Process Process, long Started, int Index) entry)
    {
        entry.Process.WaitForExit();
        var wall = Log.Now - Log.Milliseconds(entry.Started);
        Log.Mark($"spawned #{entry.Index} pid {entry.Process.Id}: exit {entry.Process.ExitCode} after {wall:F1} ms");
    }
}

// Holds the app's pipe name with a DACL that lets everyone connect, as another user squatting the name would.
static unsafe int Squat(Options options)
{
    var lockPath = Path.Combine(options.Data, "instance.lock");
    Directory.CreateDirectory(options.Data);
    if (!File.Exists(lockPath))
    {
        File.Create(lockPath).Dispose();
    }

    var name = Instance.PipeName(options.Naming, lockPath, null);

    // --owner BA: a squatter running as SYSTEM may make Administrators the owner, which an elevated client's
    // CurrentUserOnly check compares with.
    var owner = options.Value("--owner") is { } sid ? $"O:{sid}" : "";
    ConvertStringToSecurityDescriptor(owner + "D:(A;;GA;;;WD)", 1, out var descriptor, 0);
    var attributes = new SecurityAttributes { Length = sizeof(SecurityAttributes), SecurityDescriptor = descriptor };
    var handle = CreateNamedPipe(
        @"\\.\pipe\" + name, PipeAccessDuplex | FileFlagFirstPipeInstance, PipeRejectRemoteClients, 1, 4096, 4096, 0,
        &attributes);
    LocalFree(descriptor);
    if (handle.IsInvalid)
    {
        Log.Mark($"squat: CreateNamedPipe failed, error {Marshal.GetLastPInvokeError()}");
        return 1;
    }

    using var server = new NamedPipeServerStream(PipeDirection.InOut, false, false, handle);
    Log.Mark($"squat: holding {name} sddl={Instance.Sddl(handle)}");
    if (options.Value("--accept") is not null)
    {
        var thread = new Thread(() => AcceptOnce(server)) { IsBackground = true };
        thread.Start();
    }

    Thread.Sleep(options.HoldMs);
    return 0;
}

static void AcceptOnce(NamedPipeServerStream server)
{
    server.WaitForConnection();
    var buffer = new byte[4096];
    try
    {
        var read = server.Read(buffer);
        Log.Mark($"squat: a client connected and sent {read} bytes; answering with a fake ack");
        server.Write("ok\n"u8);
    }
    catch (IOException exception)
    {
        Log.Mark($"squat: the client left ({exception.Message})");
    }
}

// Connects to the app's pipe without CurrentUserOnly: what another user's process could do.
static int Probe(Options options)
{
    var lockPath = Path.Combine(options.Data, "instance.lock");
    var name = Instance.PipeName(options.Naming, lockPath, null);
    using var client = new NamedPipeClientStream(".", name, PipeDirection.InOut);
    try
    {
        client.Connect(500);
        GetNamedPipeServerProcessId(client.SafePipeHandle, out var serverId);
        Log.Mark($"probe: connected to {name}, server pid {serverId}");
        return 0;
    }
    catch (Exception exception) when (exception is UnauthorizedAccessException or TimeoutException or IOException)
    {
        Log.Mark($"probe: {name} refused, {exception.GetType().Name}: {exception.Message}");
        return 1;
    }
}

static int SuspendOrResume(Options options, bool suspend)
{
    var id = (uint)options.Number("--pid", 0);
    var process = OpenProcess(ProcessSuspendResume, false, id);
    if (process == 0)
    {
        Log.Mark($"OpenProcess({id}) failed, error {Marshal.GetLastPInvokeError()}");
        return 1;
    }

    var status = suspend ? NtSuspendProcess(process) : NtResumeProcess(process);
    CloseHandle(process);
    Log.Mark($"{(suspend ? "suspended" : "resumed")} {id}: status 0x{status:x}");
    return status == 0 ? 0 : 1;
}

// A window that starts a secondary when clicked: the click makes it the foreground process, as a user's launch would.
static int Launcher(Options options)
{
    var exe = options.Required("--exe");
    var forwarded = options.Forwarded;
    AppWindow.Run("spar-si-launcher", "normal", options.HoldMs, (window, message, wParam, lParam) =>
    {
        if (message != WmLButtonUp)
        {
            return false;
        }

        var info = new ProcessStartInfo(exe) { UseShellExecute = false };
        foreach (var argument in forwarded)
        {
            info.ArgumentList.Add(argument);
        }

        info.ArgumentList.Insert(1, "--t0");
        info.ArgumentList.Insert(2, Log.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture));
        var child = Process.Start(info);
        Log.Mark($"launcher: clicked, started pid {child?.Id}; {ForegroundDescription()}");
        return true;
    });
    return 0;
}

// Real input inside the guest: move to the window's centre and click it.
static unsafe int Click(Options options)
{
    var title = options.Value("--title") ?? "spar-si-launcher";
    var window = FindWindow(null, title);
    if (window == 0 || !GetWindowRect(window, out var rect))
    {
        Log.Mark($"click: no window '{title}'");
        return 1;
    }

    Log.Mark($"click: before, {ForegroundDescription()}");
    SetCursorPos((rect.Left + rect.Right) / 2, (rect.Top + rect.Bottom) / 2);
    Thread.Sleep(100);
    var inputs = stackalloc Input[2];
    inputs[0] = new Input { Type = InputMouse, Mouse = new MouseInput { Flags = MouseEventLeftDown } };
    inputs[1] = new Input { Type = InputMouse, Mouse = new MouseInput { Flags = MouseEventLeftUp } };
    var sent = SendInput(2, inputs, sizeof(Input));
    Thread.Sleep(200);
    Log.Mark($"click: sent {sent}; after, {ForegroundDescription()}");
    return sent == 2 ? 0 : 1;
}
