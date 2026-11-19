#:property AllowUnsafeBlocks=true
#:property PublishAot=false
#:property Nullable=enable
#:property TreatWarningsAsErrors=false

// The M1b host details run on the desktop (docs/evidence/m1b-host-details.md), grown from the security defaults run's
// DefaultsRun.cs: starts a Spar app's executable with LOCALAPPDATA in a disposable root under artifacts/, shown without
// activation (STARTF_USESHOWWINDOW, SW_SHOWNOACTIVATE) and moved offscreen as soon as its window exists, with its
// standard error in a file the run reads with its own clock. Once a line appears it plays a list of window actions,
// each of which a user's own action would cause, but without activating anything: minimize (SW_SHOWMINNOACTIVE),
// restore and show (SW_SHOWNOACTIVATE), hide (SW_HIDE), move (SetWindowPos with SWP_NOACTIVATE, still offscreen),
// handle counts of the app and of the WebView2 processes it started, and close (WM_CLOSE). It sends no mouse or
// keyboard input, never activates a window, and closes any dialog of the app or a WebView2 process with WM_CLOSE.
// Usage:
//   dotnet run HostRun.cs -- --exe <path> --root <dir> --log <file> [--env K=V]... [--until <text>] [--timeout <s>]
//       [--actions "wait:2000;minimize;wait:5000;restore;handles;close"]
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

static unsafe class HostRun
{
    const uint StartfUseShowWindow = 0x1;
    const uint StartfUseStdHandles = 0x100;
    const ushort SwShowNoActivate = 4;
    const int SwHide = 0;
    const int SwShowMinNoActive = 7;
    const uint CreateUnicodeEnvironment = 0x400;
    const uint SwpNoSize = 0x1;
    const uint SwpNoZOrder = 0x4;
    const uint SwpNoActivate = 0x10;
    const uint WmClose = 0x10;

    static readonly Stopwatch Clock = Stopwatch.StartNew();

    static int Main(string[] argv)
    {
        string exe = "", root = "", logPath = "", until = "", actions = "close";
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
                case "--actions": actions = argv[i + 1]; break;
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

        var dialogsBefore = Dialogs(0);
        var startedAt = DateTime.Now;
        Note($"start {Path.GetFileName(exe)} env [{string.Join(" ", env.Select(e => e.Item1 + "=" + e.Item2))}]");
        var (process, thread, handle) = Start(exe, stderrPath, environment);
        using var app = Process.GetProcessById(process);
        nint window = 0;
        var read = 0L;
        var lines = new List<string>();
        var found = until.Length == 0;
        var deadline = Clock.Elapsed.TotalSeconds + timeout;

        void Pump(double milliseconds)
        {
            var end = Clock.Elapsed.TotalMilliseconds + milliseconds;
            do
            {
                if (window == 0 && FindWindow(process) is var shown and not 0)
                {
                    window = shown;
                    SetWindowPos(window, 0, -20000, 0, 0, 0, SwpNoSize | SwpNoZOrder | SwpNoActivate);
                    Note($"window 0x{window:X} moved offscreen");
                }

                CloseNewDialogs(process, dialogsBefore, Note);
                read = Tail(stderrPath, read, lines, log);
                Thread.Sleep(20);
            }
            while (!app.HasExited && Clock.Elapsed.TotalMilliseconds < end);
        }

        while (!app.HasExited && Clock.Elapsed.TotalSeconds < deadline && !found)
        {
            Pump(0);
            found = until.Length > 0 && lines.Any(l => l.Contains(until, StringComparison.Ordinal));
        }

        Note(found ? $"found '{until}'" : $"'{until}' not found within {timeout} s");
        foreach (var action in actions.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (app.HasExited)
            {
                break;
            }

            var (name, argument) = action.Split(':', 2) is [var n, var a] ? (n, a) : (action, "");
            switch (name)
            {
                case "wait":
                    Pump(double.Parse(argument, System.Globalization.CultureInfo.InvariantCulture));
                    continue;
                case "minimize":
                    ShowWindow(window, SwShowMinNoActive);
                    break;
                case "restore":
                case "show":
                    ShowWindow(window, SwShowNoActivate);
                    break;
                case "hide":
                    ShowWindow(window, SwHide);
                    break;
                case "move":
                    var by = argument.Split(',').Select(int.Parse).ToArray();
                    GetWindowRect(window, out var rect);
                    SetWindowPos(window, 0, rect.Left + by[0], rect.Top + by[1], 0, 0, SwpNoSize | SwpNoZOrder | SwpNoActivate);
                    break;
                case "handles":
                    Note(Handles(app, startedAt));
                    continue;
                case "close":
                    PostMessageW(window, WmClose, 0, 0);
                    break;
            }

            GetWindowRect(window, out var after);
            Note($"{action}: visible {IsWindowVisible(window)}, iconic {IsIconic(window)}, at {after.Left},{after.Top}, foreground is the app {GetForegroundWindow() == window}");
        }

        var exited = app.WaitForExit(30_000);
        Thread.Sleep(200);
        read = Tail(stderrPath, read, lines, log);
        GetExitCodeProcess(handle, out var code);
        Note(exited ? $"exit {code}" : "did not exit within 30 s");
        Thread.Sleep(1_000);
        Note($"WebView2 processes left from this run: {WebViewProcesses(startedAt).Count}");
        CloseHandle(thread);
        CloseHandle(handle);
        return found && exited ? 0 : 1;
    }

    /// <summary>Handle counts: the app's, and each WebView2 process started since the app, by kind.</summary>
    static string Handles(Process app, DateTime after)
    {
        app.Refresh();
        var parts = new List<string> { $"app {app.HandleCount}" };
        foreach (var (id, kind, handles) in WebViewProcesses(after).OrderBy(p => p.Kind, StringComparer.Ordinal))
        {
            parts.Add($"{kind} {handles}");
        }

        return "handles: " + string.Join(", ", parts);
    }

    static List<(int Id, string Kind, int Handles)> WebViewProcesses(DateTime after)
    {
        var found = new List<(int, string, int)>();
        foreach (var process in Process.GetProcessesByName("msedgewebview2"))
        {
            try
            {
                if (process.StartTime >= after)
                {
                    found.Add((process.Id, Kind(CommandLine(process.Id)), process.HandleCount));
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

        return found;
    }

    /// <summary>A WebView2 process's role, from its command line.</summary>
    static string Kind(string line) =>
        line.Contains("--type=renderer", StringComparison.Ordinal) ? "renderer"
        : line.Contains("--type=gpu-process", StringComparison.Ordinal) ? "gpu"
        : line.Contains("network.mojom.NetworkService", StringComparison.Ordinal) ? "network"
        : line.Contains("storage.mojom.StorageService", StringComparison.Ordinal) ? "storage"
        : line.Contains("--type=crashpad-handler", StringComparison.Ordinal) ? "crashpad"
        : line.Contains("--type=utility", StringComparison.Ordinal) ? "utility"
        : line.Contains("--type=", StringComparison.Ordinal) ? "other"
        : "browser";

    /// <summary>A process's command line (<c>ProcessCommandLineInformation</c>), or empty when it cannot be read.</summary>
    static string CommandLine(int id)
    {
        var process = OpenProcess(0x1000 /* PROCESS_QUERY_LIMITED_INFORMATION */, 0, id);
        if (process == 0)
        {
            return "";
        }

        try
        {
            var buffer = new byte[65536];
            fixed (byte* data = buffer)
            {
                if (NtQueryInformationProcess(process, 60, data, buffer.Length, out _) != 0)
                {
                    return "";
                }

                var text = (UnicodeString*)data;
                return new string((char*)text->Buffer, 0, text->Length / 2);
            }
        }
        finally
        {
            CloseHandle(process);
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
    struct UnicodeString
    {
        public ushort Length;
        public ushort MaximumLength;
        public nint Buffer;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct Rect
    {
        public int Left, Top, Right, Bottom;
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
    static extern nint OpenProcess(uint access, int inherit, int id);

    [DllImport("ntdll")]
    static extern int NtQueryInformationProcess(nint process, int informationClass, void* information, int length, out int returned);

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
    static extern int IsIconic(nint window);

    [DllImport("user32")]
    static extern int ShowWindow(nint window, int command);

    [DllImport("user32")]
    static extern int GetWindowRect(nint window, out Rect rect);

    [DllImport("user32")]
    static extern nint GetForegroundWindow();

    [DllImport("user32")]
    static extern nint GetWindow(nint window, uint command);

    [DllImport("user32")]
    static extern int SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);

    [DllImport("user32", CharSet = CharSet.Unicode)]
    static extern int GetClassNameW(nint window, char* name, int length);

    [DllImport("user32")]
    static extern int PostMessageW(nint window, uint message, nint wParam, nint lParam);
}
