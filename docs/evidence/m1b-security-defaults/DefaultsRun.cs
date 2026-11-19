#:property AllowUnsafeBlocks=true
#:property PublishAot=false
#:property Nullable=enable
#:property TreatWarningsAsErrors=false

// The M1b security defaults run (docs/evidence/m1b-security-defaults.md), grown from the page security run's
// SecurityRun.cs: starts a Spar app's executable with LOCALAPPDATA in a disposable root under artifacts/,
// shown without activation (STARTF_USESHOWWINDOW, SW_SHOWNOACTIVATE) and moved offscreen as soon as its window exists,
// with its standard error in a file the run reads with its own clock. It waits for a line, keeps the app running for
// a while, samples the app process's working set, closes the window with WM_CLOSE and waits for the exit. It sends no
// mouse or keyboard input and never activates a window. Beyond the page security run it can check that a TCP port on
// 127.0.0.1 is open or closed while the app runs and after it exits (--port), close any dialog window (#32770) of the app
// or a WebView2 process that appears during the run and report it (it posts WM_CLOSE, never input), seed the browser
// profile's Preferences with a download folder before the start (--seed-preferences, --downloads), and list the folders
// the WebView2 processes started from (--processes). Usage:
//   dotnet run DefaultsRun.cs -- --exe <path> --root <dir> --log <file> [--env K=V]... [--until <text>] [--timeout <s>]
//       [--after <ms>] [--port <n>] [--seed-preferences <file> --downloads <dir>] [--processes true]
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

static unsafe class DefaultsRun
{
    const uint StartfUseShowWindow = 0x1;
    const uint StartfUseStdHandles = 0x100;
    const ushort SwShowNoActivate = 4;
    const uint CreateUnicodeEnvironment = 0x400;
    const uint SwpNoSize = 0x1;
    const uint SwpNoZOrder = 0x4;
    const uint SwpNoActivate = 0x10;
    const uint WmClose = 0x10;

    static readonly Stopwatch Clock = Stopwatch.StartNew();

    static int Main(string[] argv)
    {
        string exe = "", root = "", logPath = "", until = "", after = "3000", seed = "", downloads = "";
        var port = 0;
        var processes = false;
        var timeout = 60.0;
        var env = new List<(string, string)>();
        for (var i = 0; i < argv.Length; i += 2)
        {
            switch (argv[i])
            {
                case "--exe": exe = Path.GetFullPath(argv[i + 1]); break;
                case "--root": root = Path.GetFullPath(argv[i + 1]); break;
                case "--log": logPath = Path.GetFullPath(argv[i + 1]); break;
                case "--until": until = argv[i + 1]; break;
                case "--timeout": timeout = double.Parse(argv[i + 1], System.Globalization.CultureInfo.InvariantCulture); break;
                case "--after": after = argv[i + 1]; break;
                case "--port": port = int.Parse(argv[i + 1], System.Globalization.CultureInfo.InvariantCulture); break;
                case "--seed-preferences": seed = Path.GetFullPath(argv[i + 1]); break;
                case "--downloads": downloads = Path.GetFullPath(argv[i + 1]); break;
                case "--processes": processes = argv[i + 1] == "true"; break;
                case "--env":
                    var kv = argv[i + 1].Split('=', 2);
                    env.Add((kv[0], kv[1]));
                    break;
            }
        }

        Directory.CreateDirectory(root);
        Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
        var stderrPath = Path.Combine(root, "stderr.txt");
        using var log = new StreamWriter(logPath, append: false) { AutoFlush = true };
        void Note(string text) => log.WriteLine($"{Clock.Elapsed.TotalMilliseconds,10:0.0} | run: {text}");

        var environment = Environment.GetEnvironmentVariables()
            .Cast<System.Collections.DictionaryEntry>()
            .ToDictionary(e => (string)e.Key, e => (string?)e.Value ?? "", StringComparer.OrdinalIgnoreCase);
        environment["LOCALAPPDATA"] = root;
        foreach (var (key, value) in env)
        {
            environment[key] = value;
        }

        if (seed.Length > 0)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(seed)!);
            Directory.CreateDirectory(downloads);
            var folder = downloads.Replace("\\", "\\\\", StringComparison.Ordinal);
            File.WriteAllText(seed, $"{{\"download\":{{\"default_directory\":\"{folder}\",\"prompt_for_download\":false,\"directory_upgrade\":true}}}}");
            Note($"seeded {seed} with the download folder");
        }

        var dialogsBefore = Dialogs(0);
        var startedAt = DateTime.Now;
        if (port > 0)
        {
            Note($"port {port} before the start: {(PortOpen(port) ? "OPEN" : "closed")}");
        }

        Note($"start {Path.GetFileName(exe)} env [{string.Join(" ", env.Select(e => e.Item1 + "=" + e.Item2))}]");
        var (process, thread, handle) = Start(exe, stderrPath, environment);
        using var app = Process.GetProcessById(process);
        var hidden = false;
        var read = 0L;
        var lines = new List<string>();
        var found = until.Length == 0;
        var deadline = Clock.Elapsed.TotalSeconds + timeout;
        long peak = 0;
        while (!app.HasExited && Clock.Elapsed.TotalSeconds < deadline && !found)
        {
            if (!hidden && FindWindow(process) is var window and not 0)
            {
                SetWindowPos(window, 0, -20000, 0, 0, 0, SwpNoSize | SwpNoZOrder | SwpNoActivate);
                hidden = true;
                Note("window moved offscreen");
            }

            CloseNewDialogs(process, dialogsBefore, Note);
            read = Tail(stderrPath, read, lines, log);
            found = until.Length > 0 && lines.Any(l => l.Contains(until, StringComparison.Ordinal));
            peak = Math.Max(peak, WorkingSet(app));
            Thread.Sleep(20);
        }

        Note(found ? $"found '{until}'" : $"'{until}' not found within {timeout} s");
        var stayUntil = Clock.Elapsed.TotalMilliseconds + int.Parse(after, System.Globalization.CultureInfo.InvariantCulture);
        if (port > 0)
        {
            Note($"port {port} while the app runs: {(PortOpen(port) ? "OPEN" : "closed")}");
        }

        if (processes)
        {
            foreach (var folder in WebViewFolders(startedAt))
            {
                Note($"WebView2 processes started from {folder}");
            }
        }

        while (!app.HasExited && Clock.Elapsed.TotalMilliseconds < stayUntil)
        {
            CloseNewDialogs(process, dialogsBefore, Note);
            read = Tail(stderrPath, read, lines, log);
            peak = Math.Max(peak, WorkingSet(app));
            Thread.Sleep(20);
        }

        if (!app.HasExited)
        {
            app.Refresh();
            Note($"app working set {app.WorkingSet64 / 1024} KiB, peak seen {peak / 1024} KiB, private {app.PrivateMemorySize64 / 1024} KiB");
            var window = FindWindow(process);
            Note(window == 0 ? "no window to close" : "WM_CLOSE");
            if (window != 0)
            {
                PostMessageW(window, WmClose, 0, 0);
            }
        }

        var exited = app.WaitForExit(30_000);
        Thread.Sleep(200);
        read = Tail(stderrPath, read, lines, log);
        GetExitCodeProcess(handle, out var code);
        Note(exited ? $"exit {code}" : "did not exit within 30 s");
        if (port > 0)
        {
            Thread.Sleep(1_000);
            Note($"port {port} after the exit: {(PortOpen(port) ? "OPEN" : "closed")}");
        }

        if (downloads.Length > 0)
        {
            var files = Directory.Exists(downloads) ? Directory.GetFiles(downloads) : [];
            Note($"download folder holds {files.Length} file(s): {string.Join(", ", files.Select(Path.GetFileName))}");
        }

        CloseHandle(thread);
        CloseHandle(handle);
        return found && exited ? 0 : 1;
    }

    /// <summary>The working set, or 0 once the process has exited between the loop's check and the read.</summary>
    static long WorkingSet(Process app)
    {
        try
        {
            app.Refresh();
            return app.WorkingSet64;
        }
        catch (InvalidOperationException)
        {
            return 0;
        }
    }

    static bool PortOpen(int port)
    {
        using var client = new System.Net.Sockets.TcpClient();
        try
        {
            return client.ConnectAsync(System.Net.IPAddress.Loopback, port).Wait(1_000);
        }
        catch (AggregateException)
        {
            return false;
        }
    }

    /// <summary>Visible dialog windows (#32770) of the app or of any WebView2 process.</summary>
    static HashSet<nint> Dialogs(int app)
    {
        var found = new HashSet<nint>();
        var name = new char[64];
        EnumWindows((window, _) =>
        {
            GetWindowThreadProcessId(window, out var owner);
            if (IsWindowVisible(window) == 0)
            {
                return 1;
            }

            fixed (char* buffer = name)
            {
                var length = GetClassNameW(window, buffer, name.Length);
                if (new string(buffer, 0, length) != "#32770")
                {
                    return 1;
                }
            }

            try
            {
                using var process = Process.GetProcessById(owner);
                if (owner == app || process.ProcessName.Equals("msedgewebview2", StringComparison.OrdinalIgnoreCase))
                {
                    found.Add(window);
                }
            }
            catch (ArgumentException)
            {
            }

            return 1;
        }, 0);
        return found;
    }

    static void CloseNewDialogs(int app, HashSet<nint> before, Action<string> note)
    {
        foreach (var dialog in Dialogs(app))
        {
            if (before.Add(dialog))
            {
                note($"DIALOG appeared (0x{dialog:X}); WM_CLOSE posted");
                PostMessageW(dialog, WmClose, 0, 0);
            }
        }
    }

    /// <summary>The folders of the msedgewebview2 processes that started after the app did.</summary>
    static IEnumerable<string> WebViewFolders(DateTime after)
    {
        var folders = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var process in Process.GetProcessesByName("msedgewebview2"))
        {
            try
            {
                if (process.StartTime >= after && process.MainModule?.FileName is { } file)
                {
                    folders.Add(Path.GetDirectoryName(file)!);
                }
            }
            catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
            }
            finally
            {
                process.Dispose();
            }
        }

        return folders;
    }

    static (int Process, nint Thread, nint Handle) Start(string exe, string stderrPath, Dictionary<string, string> environment)
    {
        var security = new SecurityAttributes { Length = sizeof(SecurityAttributes), InheritHandle = 1 };
        var stderr = CreateFileW(stderrPath, 0x40000000 /* GENERIC_WRITE */, 0x1 | 0x2 /* share read, write */, &security, 2 /* CREATE_ALWAYS */, 0x80, 0);
        if (stderr == -1)
        {
            throw new InvalidOperationException($"CreateFile failed: {Marshal.GetLastPInvokeError()}");
        }

        var block = new StringBuilder();
        foreach (var (key, value) in environment.OrderBy(e => e.Key, StringComparer.OrdinalIgnoreCase))
        {
            block.Append(key).Append('=').Append(value).Append('\0');
        }

        block.Append('\0');
        var startup = new StartupInfo
        {
            Size = sizeof(StartupInfo),
            Flags = StartfUseShowWindow | StartfUseStdHandles,
            ShowWindow = SwShowNoActivate,
            StdError = stderr,
            StdOutput = stderr,
        };
        var commandLine = $"\"{exe}\"\0".ToCharArray();
        var environmentText = block.ToString();
        ProcessInformation information;
        fixed (char* command = commandLine)
        fixed (char* environmentBlock = environmentText)
        {
            if (CreateProcessW(null, command, null, null, 1, CreateUnicodeEnvironment, environmentBlock, Path.GetDirectoryName(exe), &startup, &information) == 0)
            {
                throw new InvalidOperationException($"CreateProcess failed: {Marshal.GetLastPInvokeError()}");
            }
        }

        CloseHandle(stderr);
        return (information.ProcessId, information.Thread, information.Process);
    }

    static long Tail(string path, long from, List<string> lines, StreamWriter log)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (stream.Length <= from)
        {
            return from;
        }

        stream.Position = from;
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var text = reader.ReadToEnd();
        var end = text.LastIndexOf('\n');
        if (end < 0)
        {
            return from;
        }

        foreach (var line in text[..end].Split('\n'))
        {
            var trimmed = line.TrimEnd('\r');
            lines.Add(trimmed);
            log.WriteLine($"{Clock.Elapsed.TotalMilliseconds,10:0.0} | {trimmed}");
        }

        return from + Encoding.UTF8.GetByteCount(text[..(end + 1)]);
    }

    static nint FindWindow(int process)
    {
        nint found = 0;
        EnumWindows((window, _) =>
        {
            GetWindowThreadProcessId(window, out var owner);
            if (owner == process && IsWindowVisible(window) != 0 && GetWindow(window, 4 /* GW_OWNER */) == 0)
            {
                found = window;
                return 0;
            }

            return 1;
        }, 0);
        return found;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct SecurityAttributes
    {
        public int Length;
        public nint Descriptor;
        public int InheritHandle;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct StartupInfo
    {
        public int Size;
        public nint Reserved;
        public nint Desktop;
        public nint Title;
        public int X, Y, XSize, YSize, XCountChars, YCountChars, FillAttribute;
        public uint Flags;
        public ushort ShowWindow;
        public ushort Reserved2Size;
        public nint Reserved2;
        public nint StdInput;
        public nint StdOutput;
        public nint StdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct ProcessInformation
    {
        public nint Process;
        public nint Thread;
        public int ProcessId;
        public int ThreadId;
    }

    delegate int EnumWindowsProc(nint window, nint parameter);

    [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern nint CreateFileW(string name, uint access, uint share, SecurityAttributes* security, uint disposition, uint flags, nint template);

    [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern int CreateProcessW(string? application, char* commandLine, void* processAttributes, void* threadAttributes,
        int inheritHandles, uint flags, char* environment, string? directory, StartupInfo* startup, ProcessInformation* information);

    [DllImport("kernel32")]
    static extern int CloseHandle(nint handle);

    [DllImport("kernel32")]
    static extern int GetExitCodeProcess(nint process, out int code);

    [DllImport("user32")]
    static extern int EnumWindows(EnumWindowsProc callback, nint parameter);

    [DllImport("user32")]
    static extern uint GetWindowThreadProcessId(nint window, out int process);

    [DllImport("user32")]
    static extern int IsWindowVisible(nint window);

    [DllImport("user32")]
    static extern nint GetWindow(nint window, uint command);

    [DllImport("user32")]
    static extern int SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);

    [DllImport("user32", CharSet = CharSet.Unicode)]
    static extern int GetClassNameW(nint window, char* name, int length);

    [DllImport("user32")]
    static extern int PostMessageW(nint window, uint message, nint wParam, nint lParam);
}
