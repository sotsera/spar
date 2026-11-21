#:property AllowUnsafeBlocks=true
#:property PublishAot=false
#:property Nullable=enable
#:property TreatWarningsAsErrors=false

// The M1d step 1 run (docs/evidence/m1d-dev-server.md), grown from the security defaults run's DefaultsRun.cs: starts a
// Spar app's Debug executable with LOCALAPPDATA in a disposable root under artifacts/, shown without activation
// (STARTF_USESHOWWINDOW, SW_SHOWNOACTIVATE) and moved offscreen as soon as its window exists, its standard output and
// error in a file, and no standard input, or with --stdin-pipe true a pipe whose writing end the run holds. It then runs
// steps in order against the app's log (app.log under the root), and copies every new line of it into the run's log with
// the run's clock:
//   until:<text>          wait (60 s at most) for a new line of app.log that contains <text>
//   sleep:<ms>            wait
//   marker:<n>            write the Fast Refresh marker n into the frontend's Sidebar.tsx (0 adds it; then n changes it)
//   violation:<n>         make Sidebar.tsx load an image from another origin, which the policy blocks and reports
//   worker                add a module worker (src/spar-worker-check.js, removed at the end) that tries eval and
//                         reports through /__spar-worker/<outcome>: blocked means its own response carried the policy
//   css:<n>               change a colour in the frontend's styles.css (n picks the colour)
//   kill-vite             end the development server's tree by the pid in obj/spar-dev-server.json
//   close-stdin           close the writing end of the app's standard input (with --stdin-pipe true)
//   close                 WM_CLOSE to the window, then wait 30 s for the exit
// Every edited file is restored at the end, whatever happened. It sends no mouse or keyboard input and never activates a
// window. Usage:
//   dotnet run DevRun.cs -- --exe <path> --root <dir> --log <file> --frontend <dir> --obj <dir> [--env K=V]... --steps "a;b;c" [--stdin-pipe true]
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

static unsafe class DevRun
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
    static readonly Dictionary<string, string> Originals = new();
    static readonly List<string> Added = [];

    static int Main(string[] argv)
    {
        string exe = "", root = "", logPath = "", frontend = "", obj = "", steps = "";
        var stdinPipe = false;
        var env = new List<(string, string)>();
        for (var i = 0; i < argv.Length; i += 2)
        {
            switch (argv[i])
            {
                case "--exe": exe = Path.GetFullPath(argv[i + 1]); break;
                case "--root": root = Path.GetFullPath(argv[i + 1]); break;
                case "--log": logPath = Path.GetFullPath(argv[i + 1]); break;
                case "--frontend": frontend = Path.GetFullPath(argv[i + 1]); break;
                case "--obj": obj = Path.GetFullPath(argv[i + 1]); break;
                case "--steps": steps = argv[i + 1]; break;
                case "--stdin-pipe": stdinPipe = argv[i + 1] == "true"; break;
                case "--env":
                    var kv = argv[i + 1].Split('=', 2);
                    env.Add((kv[0], kv[1]));
                    break;
            }
        }

        Directory.CreateDirectory(root);
        Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
        var outputPath = Path.Combine(root, "output.txt");
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

        var appLog = Directory.EnumerateDirectories(root).Select(d => Path.Combine(d, "logs", "app.log")).FirstOrDefault(File.Exists);
        Note($"start {Path.GetFileName(exe)} env [{string.Join(" ", env.Select(e => e.Item1 + "=" + e.Item2))}]");
        var (process, thread, handle, stdinWriter) = Start(exe, outputPath, environment, stdinPipe);
        using var app = Process.GetProcessById(process);
        var hidden = false;
        var appRead = 0L;
        var outputRead = 0L;
        var lines = new List<string>();
        var result = 0;

        void Pump()
        {
            if (!hidden && FindWindow(process) is var window and not 0)
            {
                SetWindowPos(window, 0, -20000, 0, 0, 0, SwpNoSize | SwpNoZOrder | SwpNoActivate);
                hidden = true;
                Note("window moved offscreen");
            }

            appLog ??= Directory.EnumerateDirectories(root).Select(d => Path.Combine(d, "logs", "app.log")).FirstOrDefault(File.Exists);
            if (appLog is not null)
            {
                appRead = Tail(appLog, appRead, lines, log, "app");
            }

            outputRead = Tail(outputPath, outputRead, new List<string>(), log, "out");
        }

        try
        {
            foreach (var step in steps.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var colon = step.IndexOf(':');
                var (name, value) = colon < 0 ? (step, "") : (step[..colon], step[(colon + 1)..]);
                switch (name)
                {
                    case "until":
                        var seen = lines.Count;
                        var deadline = Clock.Elapsed.TotalSeconds + 60;
                        var found = false;
                        while (!found && Clock.Elapsed.TotalSeconds < deadline && !app.HasExited)
                        {
                            Pump();
                            found = lines.Skip(seen).Any(l => l.Contains(value, StringComparison.Ordinal));
                            if (!found)
                            {
                                Thread.Sleep(10);
                            }
                        }

                        Pump();
                        found = found || lines.Skip(seen).Any(l => l.Contains(value, StringComparison.Ordinal));
                        Note(found ? $"found '{value}'" : $"'{value}' NOT found");
                        result |= found ? 0 : 1;
                        break;
                    case "sleep":
                        var until = Clock.Elapsed.TotalMilliseconds + int.Parse(value);
                        while (Clock.Elapsed.TotalMilliseconds < until)
                        {
                            Pump();
                            Thread.Sleep(10);
                        }

                        break;
                    case "marker":
                        Marker(Path.Combine(frontend, "src", "components", "Sidebar.tsx"), int.Parse(value), Note);
                        break;
                    case "violation":
                        Violation(Path.Combine(frontend, "src", "components", "Sidebar.tsx"), int.Parse(value), Note);
                        break;
                    case "worker":
                        Worker(frontend, Note);
                        break;
                    case "css":
                        Css(Path.Combine(frontend, "src", "styles.css"), int.Parse(value), Note);
                        break;
                    case "kill-vite":
                        KillVite(Path.Combine(obj, "spar-dev-server.json"), Note);
                        break;
                    case "close-stdin":
                        CloseHandle(stdinWriter);
                        stdinWriter = 0;
                        Note($"standard input's writing end closed at {DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}");
                        break;
                    case "close":
                        var window = FindWindow(process);
                        Note(window == 0 ? "no window to close" : "WM_CLOSE");
                        if (window != 0)
                        {
                            PostMessageW(window, WmClose, 0, 0);
                        }

                        var exited = app.WaitForExit(30_000);
                        Thread.Sleep(300);
                        Pump();
                        GetExitCodeProcess(handle, out var code);
                        Note(exited ? $"exit {code}" : "did not exit within 30 s");
                        result |= exited ? 0 : 1;
                        break;
                }
            }
        }
        finally
        {
            foreach (var (path, text) in Originals)
            {
                File.WriteAllText(path, text);
                Note($"restored {Path.GetFileName(path)}");
            }

            foreach (var path in Added)
            {
                File.Delete(path);
                Note($"removed {Path.GetFileName(path)}");
            }

            if (!app.HasExited)
            {
                Note("the app was still running: ended");
                app.Kill();
            }

            CloseHandle(thread);
            CloseHandle(handle);
        }

        return result;
    }

    /// <summary>
    /// The Fast Refresh marker: a ref that keeps one random id per mount and an effect that requests
    /// /__spar-hmr/n/id/time through the app's origin after each render, so the proxy's log shows when the page rendered
    /// the edit (the page's own clock, the system's) and whether the component kept its state (the same id).
    /// </summary>
    static void Marker(string path, int n, Action<string> note)
    {
        var text = Original(path);
        var edited = text
            .Replace("import { forwardRef, useMemo } from \"react\";", "import { forwardRef, useEffect, useMemo, useRef } from \"react\";")
            .Replace(
                "  const { summaries, selected, query, onQuery, onSelect, onNew } = props;\n",
                "  const { summaries, selected, query, onQuery, onSelect, onNew } = props;\n" +
                "  const sparMount = useRef(Math.random().toString(36).slice(2));\n" +
                $"  useEffect(() => {{ new Image().src = `/__spar-hmr/{n}/${{sparMount.current}}/${{Date.now()}}`; }});\n");
        if (edited == text)
        {
            throw new InvalidOperationException("The marker's anchors are not in Sidebar.tsx.");
        }

        File.WriteAllText(path, edited);
        note($"marker {n} written at {DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}");
    }

    /// <summary>A deliberate violation: an image from another origin, which img-src 'self' blocks, to see it reported.</summary>
    static void Violation(string path, int n, Action<string> note)
    {
        var text = Original(path);
        var edited = text
            .Replace("import { forwardRef, useMemo } from \"react\";", "import { forwardRef, useEffect, useMemo } from \"react\";")
            .Replace(
                "  const { summaries, selected, query, onQuery, onSelect, onNew } = props;\n",
                "  const { summaries, selected, query, onQuery, onSelect, onNew } = props;\n" +
                $"  useEffect(() => {{ new Image().src = \"https://spar-violation.invalid/{n}\"; }});\n");
        File.WriteAllText(path, edited);
        note($"violation {n} written at {DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}");
    }

    /// <summary>
    /// A module worker served through the proxy like any page script: it tries eval, which only a policy on the worker's
    /// own response blocks, and the page reports the outcome as a request the proxy logs. The worker's address goes
    /// through a Trusted Types policy, as the page's policy requires for any worker.
    /// </summary>
    static void Worker(string frontend, Action<string> note)
    {
        var script = Path.Combine(frontend, "src", "spar-worker-check.js");
        File.WriteAllText(script, "let outcome;\ntry { outcome = String((0, eval)(\"1 + 1\")); } catch (error) { outcome = error.name; }\npostMessage(outcome);\n");
        Added.Add(script);
        var sidebar = Path.Combine(frontend, "src", "components", "Sidebar.tsx");
        var text = Original(sidebar);
        var edited = text
            .Replace("import { forwardRef, useMemo } from \"react\";", "import { forwardRef, useEffect, useMemo } from \"react\";")
            .Replace(
                "  const { summaries, selected, query, onQuery, onSelect, onNew } = props;\n",
                "  const { summaries, selected, query, onQuery, onSelect, onNew } = props;\n" +
                "  useEffect(() => {\n" +
                "    const policy = trustedTypes.createPolicy(\"spar-worker-check\", { createScriptURL: (url: string) => url });\n" +
                "    const worker = new Worker(policy.createScriptURL(\"/src/spar-worker-check.js\") as unknown as string, { type: \"module\" });\n" +
                "    worker.onmessage = event => { new Image().src = `/__spar-worker/${String(event.data)}`; worker.terminate(); };\n" +
                "  }, []);\n");
        File.WriteAllText(sidebar, edited);
        note($"worker check written at {DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}");
    }

    static void Css(string path, int n, Action<string> note)
    {
        var text = Original(path);
        var colour = $"#2f5b{(0xd0 + n % 16):x2}";
        var edited = text.Replace("--color-accent: #2f5bd3;", $"--color-accent: {colour};");
        if (edited == text && n % 16 != 3)
        {
            throw new InvalidOperationException("The colour's anchor is not in styles.css.");
        }

        File.WriteAllText(path, edited);
        note($"css {n} ({colour}) written at {DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}");
    }

    static string Original(string path)
    {
        if (!Originals.TryGetValue(path, out var text))
        {
            text = File.ReadAllText(path);
            Originals[path] = text;
        }

        return text;
    }

    static void KillVite(string stateFile, Action<string> note)
    {
        using var json = JsonDocument.Parse(File.ReadAllText(stateFile));
        var pid = json.RootElement.GetProperty("pid").GetInt32();
        using var vite = Process.GetProcessById(pid);
        vite.Kill(entireProcessTree: true);
        vite.WaitForExit(10_000);
        note($"Vite (pid {pid}) ended with its tree at {DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}");
    }

    static (int Process, nint Thread, nint Handle, nint StdinWriter) Start(string exe, string outputPath, Dictionary<string, string> environment, bool stdinPipe)
    {
        nint stdinReader = 0, stdinWriter = 0;
        if (stdinPipe)
        {
            var inheritable = new SecurityAttributes { Length = sizeof(SecurityAttributes), InheritHandle = 1 };
            if (CreatePipe(out stdinReader, out stdinWriter, &inheritable, 0) == 0 || SetHandleInformation(stdinWriter, 1 /* HANDLE_FLAG_INHERIT */, 0) == 0)
            {
                throw new InvalidOperationException($"CreatePipe failed: {Marshal.GetLastPInvokeError()}");
            }
        }

        var security = new SecurityAttributes { Length = sizeof(SecurityAttributes), InheritHandle = 1 };
        var output = CreateFileW(outputPath, 0x40000000 /* GENERIC_WRITE */, 0x1 | 0x2 /* share read, write */, &security, 2 /* CREATE_ALWAYS */, 0x80, 0);
        if (output == -1)
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
            StdError = output,
            StdOutput = output,
            StdInput = stdinReader,
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

        CloseHandle(output);
        if (stdinReader != 0)
        {
            CloseHandle(stdinReader);
        }

        return (information.ProcessId, information.Thread, information.Process, stdinWriter);
    }

    static long Tail(string path, long from, List<string> lines, StreamWriter log, string source)
    {
        if (!File.Exists(path))
        {
            return from;
        }

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
            log.WriteLine($"{Clock.Elapsed.TotalMilliseconds,10:0.0} | {source}: {trimmed}");
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

    [DllImport("kernel32", SetLastError = true)]
    static extern int CreatePipe(out nint read, out nint write, SecurityAttributes* attributes, uint size);

    [DllImport("kernel32", SetLastError = true)]
    static extern int SetHandleInformation(nint handle, uint mask, uint flags);

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

    [DllImport("user32")]
    static extern int PostMessageW(nint window, uint message, nint wParam, nint lParam);
}
