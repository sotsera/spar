#:property AllowUnsafeBlocks=true
#:property PublishAot=false
#:property Nullable=enable
#:property TreatWarningsAsErrors=false

// The M1d step 3 desktop run on Windows (docs/evidence/m1d-hot-reload.md): `dotnet watch --non-interactive --verbose
// -lp Desktop` on the Debug Notebook, started on a desktop of this run's own (CreateDesktop; dotnet watch, the app, its
// WebView2 and Vite inherit it), so no window reaches the monitor, takes the focus or gets input. LOCALAPPDATA (and so the
// app's folders and WebView2 profile) is an absolute folder under artifacts/webview2-profiles/. Its steps, in order:
//   until:<text>   wait (180 s at most) for a new line of app.log that contains <text>
//   watch:<text>   wait (180 s at most) for a new line of dotnet watch's output that contains <text>
//   sleep:<ms>     wait
//   probe          add to the page's main.tsx an onBackendUpdate handler that, 50 ms after each update, requests
//                  /__spar-probe/backend-updated/<revision>/toasts-<n>/<time> through the app's origin, n being the toasts
//                  the development overlay shows; the proxy's Debug line in app.log carries the path
//   toastoff       configureSpar({ backendUpdateToast: false }) in the page's spar.ts
//   body           MarkdownPageStore.ListAsync's summary title gains " (edited)" (a method body)
//   dto            PageSummary gains `string? Excerpt = null`, and the page tests' summaries `excerpt: null`
//   restore:<f>    restore one edited file (body: MarkdownPageStore.cs; dto: PageTypes.cs and notebook.test.tsx;
//                  probe: main.tsx; toastoff: spar.ts)
//   client:<text>  the generated obj/spar/client/pages.d.ts contains <text> (or, with a leading !, does not)
//   suspend:<ms>   suspend every thread of the app's process (NtSuspendProcess, as a debugger's break does) for <ms>,
//                  then resume it
// With --exe <path> the run starts that executable instead (the Debug apphost, as an IDE's F5 starts it: the launch
// profile's variables, DOTNET_MODIFIABLE_ASSEMBLIES=debug as Visual Studio sets for Hot Reload, and --cwd as its working
// directory), with no dotnet watch.
// Every edited file is restored at the end, whatever happened, and every process this run started is ended (the
// dotnet watch tree, then the development server by its record). Each line of the run's log is the run's clock (ms),
// the UTC time, and `app:` (app.log), `watch:` (dotnet watch's output) or `run:`.
//   dotnet run WatchDesktop.cs -- --project <app dir> --root <dir> --log <file> --steps "a;b;c" [--exe <path> --cwd <dir> --env K=V]
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

static unsafe class WatchDesktop
{
    const uint StartfUseStdHandles = 0x100;
    const uint CreateUnicodeEnvironment = 0x400;
    const uint CreateNoWindow = 0x08000000;
    const uint DesktopAllAccess = 0x01FF;

    static readonly Stopwatch Clock = Stopwatch.StartNew();
    static readonly Dictionary<string, string> Originals = new();
    static StreamWriter Log = null!;

    static void Note(string text) => Write("run", text);

    static void Write(string source, string text) =>
        Log.WriteLine($"{Clock.Elapsed.TotalMilliseconds,10:0.0} {DateTime.UtcNow:HH:mm:ss.fff} {source}: {text}");

    static int Main(string[] argv)
    {
        string project = "", root = "", logPath = "", steps = "";
        string? exe = null, cwd = null;
        var extra = new List<(string, string)>();
        for (var i = 0; i < argv.Length; i += 2)
        {
            switch (argv[i])
            {
                case "--project": project = Path.GetFullPath(argv[i + 1]); break;
                case "--root": root = Path.GetFullPath(argv[i + 1]); break;
                case "--log": logPath = Path.GetFullPath(argv[i + 1]); break;
                case "--steps": steps = argv[i + 1]; break;
                case "--exe": exe = Path.GetFullPath(argv[i + 1]); break;
                case "--cwd": cwd = Path.GetFullPath(argv[i + 1]); break;
                case "--env":
                    var pair = argv[i + 1].Split('=', 2);
                    extra.Add((pair[0], pair[1]));
                    break;
            }
        }

        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }

        Directory.CreateDirectory(root);
        Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
        Log = new StreamWriter(logPath, append: false) { AutoFlush = true };
        var outputPath = Path.Combine(root, "watch-output.txt");

        var environment = Environment.GetEnvironmentVariables()
            .Cast<System.Collections.DictionaryEntry>()
            .ToDictionary(e => (string)e.Key, e => (string?)e.Value ?? "", StringComparer.OrdinalIgnoreCase);
        foreach (var name in environment.Keys.Where(k => k.StartsWith("SPAR_", StringComparison.OrdinalIgnoreCase)).ToList())
        {
            environment.Remove(name);
        }

        // Absolute, so Spar takes it (a relative LOCALAPPDATA falls back to the user's own folder).
        environment["LOCALAPPDATA"] = Path.Combine(root, "local");
        Directory.CreateDirectory(environment["LOCALAPPDATA"]);
        environment["MSBUILDDISABLENODEREUSE"] = "1";
        environment["DOTNET_WATCH_SUPPRESS_EMOJIS"] = "1";
        environment["DOTNET_NOLOGO"] = "1";
        foreach (var (key, value) in extra)
        {
            environment[key] = value;
        }

        var desktopName = $"spar-hot-reload-{Environment.ProcessId}";
        var desktop = CreateDesktopW(desktopName, null, null, 0, DesktopAllAccess, null);
        if (desktop == 0)
        {
            throw new InvalidOperationException($"CreateDesktop failed: {Marshal.GetLastPInvokeError()}");
        }

        var command = exe is null
            ? $"dotnet watch --non-interactive --verbose --project \"{project}\" -lp Desktop"
            : $"\"{exe}\"";
        Note($"desktop {desktopName}; {command} in {cwd ?? project}, with [{string.Join(" ", extra.Select(e => e.Item1 + "=" + e.Item2))}]");
        var (pid, thread, handle) = Start(command, cwd ?? project, outputPath, environment, $@"WinSta0\{desktopName}");
        using var watch = Process.GetProcessById(pid);
        string? appLog = null;
        long appRead = 0, outputRead = 0;
        var appLines = new List<string>();
        var watchLines = new List<string>();
        var result = 0;

        void Pump()
        {
            appLog ??= Directory.EnumerateDirectories(Path.Combine(root, "local"))
                .Select(d => Path.Combine(d, "logs", "app.log")).FirstOrDefault(File.Exists);
            if (appLog is not null)
            {
                appRead = Tail(appLog, appRead, appLines, "app");
            }

            outputRead = Tail(outputPath, outputRead, watchLines, "watch");
        }

        bool WaitFor(List<string> lines, string text, int seconds)
        {
            var seen = lines.Count;
            var deadline = Clock.Elapsed.TotalSeconds + seconds;
            while (Clock.Elapsed.TotalSeconds < deadline)
            {
                Pump();
                if (lines.Skip(seen).Any(l => l.Contains(text, StringComparison.Ordinal)))
                {
                    return true;
                }

                Thread.Sleep(10);
            }

            return false;
        }

        try
        {
            foreach (var step in steps.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var colon = step.IndexOf(':');
                var (name, value) = colon < 0 ? (step, "") : (step[..colon], step[(colon + 1)..]);
                Pump();
                switch (name)
                {
                    case "until":
                    case "watch":
                        var found = WaitFor(name == "until" ? appLines : watchLines, value, 180);
                        Note(found ? $"found '{value}'" : $"FAIL '{value}' NOT found");
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
                    case "probe":
                        Edit(project, "frontend/src/main.tsx", "import \"./styles.css\";\n",
                            "import \"./styles.css\";\nimport { onBackendUpdate } from \"@spar/app/sdk\";\n\n" +
                            "onBackendUpdate(update => setTimeout(() => {\n" +
                            "  const toasts = document.querySelector(\"[data-spar-dev-overlay]\")?.shadowRoot?.querySelectorAll(\".toast\").length ?? 0;\n" +
                            "  new Image().src = `/__spar-probe/backend-updated/${update.revision}/toasts-${toasts}/${Date.now()}`;\n" +
                            "}, 50));\n");
                        break;
                    case "toastoff":
                        Edit(project, "frontend/src/spar.ts", "  configureSpar({\n", "  configureSpar({\n    backendUpdateToast: false,\n");
                        break;
                    case "body":
                        Edit(project, "Pages/MarkdownPageStore.cs", "new PageSummary(page.Id, page.Title, page.UpdatedAt)",
                            "new PageSummary(page.Id, page.Title + \" (edited)\", page.UpdatedAt)");
                        break;
                    case "dto":
                        Edit(project, "frontend/src/notebook.test.tsx", "updatedAt: \"2026-11-01T09:00:00Z\" };", "updatedAt: \"2026-11-01T09:00:00Z\", excerpt: null };");
                        Edit(project, "frontend/src/notebook.test.tsx", "updatedAt: \"2026-11-16T09:00:00Z\" };", "updatedAt: \"2026-11-16T09:00:00Z\", excerpt: null };");
                        Edit(project, "Pages/PageTypes.cs", "PageSummary(string Id, string Title, DateTimeOffset UpdatedAt)",
                            "PageSummary(string Id, string Title, DateTimeOffset UpdatedAt, string? Excerpt = null)");
                        break;
                    case "restore":
                        foreach (var file in value.Split(','))
                        {
                            Restore(project, file);
                        }

                        break;
                    case "suspend":
                        var target = exe is null ? AppProcess(pid) : pid;
                        using (var suspended = Process.GetProcessById(target))
                        {
                            NtSuspendProcess(suspended.Handle);
                            Note($"app process {target} suspended for {value} ms");
                            Thread.Sleep(int.Parse(value));
                            NtResumeProcess(suspended.Handle);
                            Note($"app process {target} resumed");
                        }

                        break;
                    case "client":
                        var negate = value.StartsWith('!');
                        var text = negate ? value[1..] : value;
                        var declarations = File.ReadAllText(Path.Combine(project, "obj", "spar", "client", "pages.d.ts"));
                        var ok = declarations.Contains(text, StringComparison.Ordinal) != negate;
                        Note($"{(ok ? "PASS" : "FAIL")} the generated pages.d.ts {(negate ? "has no" : "has")} '{text}'");
                        result |= ok ? 0 : 1;
                        break;
                }
            }
        }
        finally
        {
            foreach (var (path, original) in Originals)
            {
                File.WriteAllText(path, original);
                Note($"restored {Path.GetRelativePath(project, path)}");
            }

            Pump();
            if (!watch.HasExited)
            {
                watch.Kill(entireProcessTree: true);
                watch.WaitForExit(15_000);
                Note("dotnet watch's tree ended");
            }

            EndDevServer(project);
            Thread.Sleep(1_000);
            var left = Process.GetProcessesByName("msedgewebview2").Count(p => CommandLineMentions(p, root));
            Note($"WebView2 processes with this run's root still running: {left}");
            CloseHandle(thread);
            CloseHandle(handle);
            CloseDesktop(desktop);
        }

        return result;
    }

    /// <summary>The app's process under dotnet watch: the newest process whose executable is the app's apphost.</summary>
    static int AppProcess(int watchPid) =>
        Process.GetProcessesByName("Notebook").OrderByDescending(p => p.StartTime).First().Id;

    static void Edit(string project, string relative, string from, string to)
    {
        var path = Path.Combine(project, relative);
        var current = File.ReadAllText(path);
        Originals.TryAdd(path, current);
        var edited = current.Replace(from, to, StringComparison.Ordinal);
        if (edited == current)
        {
            throw new InvalidOperationException($"The anchor of the edit is not in {relative}.");
        }

        File.WriteAllText(path, edited);
        Note($"edit written: {relative} at {DateTime.UtcNow:HH:mm:ss.fff}");
    }

    static void Restore(string project, string relative)
    {
        var path = Path.Combine(project, relative);
        if (Originals.Remove(path, out var original))
        {
            File.WriteAllText(path, original);
            Note($"restored: {relative} at {DateTime.UtcNow:HH:mm:ss.fff}");
        }
    }

    static void EndDevServer(string project)
    {
        var record = Path.Combine(project, "obj", "spar-dev-server.json");
        try
        {
            using var json = JsonDocument.Parse(File.ReadAllText(record));
            using var vite = Process.GetProcessById(json.RootElement.GetProperty("pid").GetInt32());
            vite.Kill(entireProcessTree: true);
            Note($"development server {vite.Id} ended");
        }
        catch (Exception exception) when (exception is IOException or ArgumentException or KeyNotFoundException or JsonException)
        {
            Note("the development server had already ended");
        }
    }

    /// <summary>Whether a process's command line names the run's root (WebView2's --user-data-dir is under it).</summary>
    static bool CommandLineMentions(Process process, string root)
    {
        try
        {
            var query = Process.Start(new ProcessStartInfo("powershell", $"-NoProfile -Command \"(Get-CimInstance Win32_Process -Filter 'ProcessId={process.Id}').CommandLine\"")
            {
                RedirectStandardOutput = true,
                CreateNoWindow = true,
            })!;
            var line = query.StandardOutput.ReadToEnd();
            query.WaitForExit();
            return line.Contains(root, StringComparison.OrdinalIgnoreCase);
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    static (int Process, nint Thread, nint Handle) Start(string command, string directory, string outputPath, Dictionary<string, string> environment, string desktop)
    {
        var security = new SecurityAttributes { Length = sizeof(SecurityAttributes), InheritHandle = 1 };
        var output = CreateFileW(outputPath, 0x40000000 /* GENERIC_WRITE */, 0x1 | 0x2, &security, 2 /* CREATE_ALWAYS */, 0x80, 0);
        if (output == -1)
        {
            throw new InvalidOperationException($"CreateFile failed: {Marshal.GetLastPInvokeError()}");
        }

        var nul = CreateFileW("NUL", 0x80000000 /* GENERIC_READ */, 0x1 | 0x2, &security, 3 /* OPEN_EXISTING */, 0, 0);
        var block = new StringBuilder();
        foreach (var (key, value) in environment.OrderBy(e => e.Key, StringComparer.OrdinalIgnoreCase))
        {
            block.Append(key).Append('=').Append(value).Append('\0');
        }

        block.Append('\0');
        var commandLine = (command + "\0").ToCharArray();
        var desktopText = desktop + "\0";
        var environmentText = block.ToString();
        ProcessInformation information;
        fixed (char* commandText = commandLine)
        fixed (char* desktopName = desktopText)
        fixed (char* environmentBlock = environmentText)
        {
            var startup = new StartupInfo
            {
                Size = sizeof(StartupInfo),
                Desktop = (nint)desktopName,
                Flags = StartfUseStdHandles,
                StdError = output,
                StdOutput = output,
                StdInput = nul,
            };
            if (CreateProcessW(null, commandText, null, null, 1, CreateUnicodeEnvironment | CreateNoWindow, environmentBlock, directory, &startup, &information) == 0)
            {
                throw new InvalidOperationException($"CreateProcess failed: {Marshal.GetLastPInvokeError()}");
            }
        }

        CloseHandle(output);
        CloseHandle(nul);
        return (information.ProcessId, information.Thread, information.Process);
    }

    static long Tail(string path, long from, List<string> lines, string source)
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
            Write(source, trimmed);
        }

        return from + Encoding.UTF8.GetByteCount(text[..(end + 1)]);
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

    [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern nint CreateFileW(string name, uint access, uint share, SecurityAttributes* security, uint disposition, uint flags, nint template);

    [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern int CreateProcessW(string? application, char* commandLine, void* processAttributes, void* threadAttributes,
        int inheritHandles, uint flags, char* environment, string? directory, StartupInfo* startup, ProcessInformation* information);

    [DllImport("kernel32")]
    static extern int CloseHandle(nint handle);

    [DllImport("user32", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern nint CreateDesktopW(string name, string? device, void* mode, uint flags, uint access, void* security);

    [DllImport("user32")]
    static extern int CloseDesktop(nint desktop);

    [DllImport("ntdll")]
    static extern int NtSuspendProcess(nint process);

    [DllImport("ntdll")]
    static extern int NtResumeProcess(nint process);
}
