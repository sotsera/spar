// The teardown leak probe (docs/evidence/m1b-host-details.md, plan M1b step 6): one WebView2 environment, and N
// cycles that each create a window and a controller, load a page from https://app.localhost (served from memory as
// the host serves it) that makes fetches to its own origin and, optionally, to a loopback server, then tear it down.
// After each cycle it writes the handle counts of its own process and of every WebView2 process it started, by
// kind. Teardown "plain" is the host's order (events removed, Close on the STA, window destroyed); "mitigated" is
// the earlier research's: the controller hidden, a navigation to about:blank, and the close posted to the window
// outside any WebView2 callback. Windows are "shown" (on screen, not activated: Windows Sandbox only), "offscreen"
// (shown at -20000,0) or "hidden" (never shown). An optional anchor controller keeps the browser alive across cycles,
// as a second window of the app or a test run's shared environment would.
//   LeakProbe.exe --out <log> --profile <folder> [--cycles 20] [--teardown plain|mitigated]
//       [--window shown|offscreen|hidden] [--anchor none|hidden|offscreen|shown] [--loopback true] [--settle 1500]
//       [--browser-folder <Runtime folder>]
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Spar.Windows.Interop;

namespace LeakProbe;

internal static unsafe class Program
{
    private const uint WmApp = 0x8000;
    private const uint WsOverlappedWindow = 0x00CF0000;
    private const int SwShowNoActivate = 4;

    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    private static readonly DateTime Started = DateTime.Now;
    private static Action? _posted;
    private static StreamWriter _log = StreamWriter.Null;

    [STAThread]
    private static int Main(string[] argv)
    {
        var options = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["--cycles"] = "20", ["--teardown"] = "plain", ["--window"] = "offscreen", ["--anchor"] = "none",
            ["--loopback"] = "false", ["--settle"] = "1500", ["--browser-folder"] = "", ["--out"] = "leak.log", ["--profile"] = "profile",
        };
        for (var i = 0; i + 1 < argv.Length; i += 2)
        {
            options[argv[i]] = argv[i + 1];
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(options["--out"]))!);
        _log = new StreamWriter(options["--out"], append: false) { AutoFlush = true };
        Note($"options {string.Join(" ", options.Select(o => o.Key + " " + o.Value))}");
        try
        {
            return Run(options);
        }
        catch (Exception exception)
        {
            Note($"FAILED {exception}");
            return 1;
        }
    }

    private static int Run(Dictionary<string, string> options)
    {
        var cycles = int.Parse(options["--cycles"]);
        var mitigated = options["--teardown"] == "mitigated";
        var settle = int.Parse(options["--settle"]);
        var port = options["--loopback"] == "true" ? StartLoopback() : 0;
        RegisterClass();

        var created = new EnvironmentCompleted();
        var folder = options["--browser-folder"] is { Length: > 0 } given ? given : null;
        var result = Native.CreateCoreWebView2EnvironmentWithOptions(folder, Path.GetFullPath(options["--profile"]), null, created);
        if (result < 0)
        {
            throw new WebView2Exception("CreateCoreWebView2EnvironmentWithOptions", result);
        }

        var environment = Com.Take<ICoreWebView2Environment10>(Wait(created.Task, 30_000));
        Note($"environment, Runtime {environment.GetBrowserVersionString()}, loopback port {port}");
        Measure("start");

        View? anchor = null;
        if (options["--anchor"] != "none")
        {
            anchor = View.Open(environment, options["--anchor"], port, 0);
            Note($"anchor ({options["--anchor"]}) loaded: {anchor.Loaded}");
        }

        Measure("before cycles");
        for (var cycle = 1; cycle <= cycles; cycle++)
        {
            var view = View.Open(environment, options["--window"], port, cycle);
            var opened = $"loaded {view.Loaded} in {view.LoadMilliseconds:0} ms";
            var closed = mitigated ? view.CloseMitigated() : view.ClosePlain();
            Pump(() => false, settle);
            Measure($"cycle {cycle}: {opened}, {closed}");
        }

        anchor?.ClosePlain();
        Com.Release(environment);
        Pump(() => false, 3_000);
        Measure("after the environment's release");
        return 0;
    }

    /// <summary>The probe's handle count, and each WebView2 process it started with its handles, by kind.</summary>
    private static void Measure(string label)
    {
        GetProcessHandleCount(GetCurrentProcess(), out var own);
        var parts = new List<string>();
        foreach (var process in Process.GetProcessesByName("msedgewebview2"))
        {
            try
            {
                if (process.StartTime >= Started)
                {
                    parts.Add($"{Kind(CommandLine(process.Id))}#{process.Id} {process.HandleCount}");
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

        parts.Sort(StringComparer.Ordinal);
        Note($"{label} | probe {own} | gdi {GetGuiResources(GetCurrentProcess(), 0)} user {GetGuiResources(GetCurrentProcess(), 1)} | {string.Join(", ", parts)}");
    }

    private static string Kind(string line) =>
        line.Contains("--type=renderer", StringComparison.Ordinal) ? "renderer"
        : line.Contains("--type=gpu-process", StringComparison.Ordinal) ? "gpu"
        : line.Contains("network.mojom.NetworkService", StringComparison.Ordinal) ? "network"
        : line.Contains("storage.mojom.StorageService", StringComparison.Ordinal) ? "storage"
        : line.Contains("--type=crashpad-handler", StringComparison.Ordinal) ? "crashpad"
        : line.Contains("--type=utility", StringComparison.Ordinal) ? "utility"
        : line.Contains("--type=", StringComparison.Ordinal) ? "other"
        : "browser";

    private static string CommandLine(int id)
    {
        var process = OpenProcess(0x1000, 0, id);
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

    internal static void Note(string text) => _log.WriteLine($"{Clock.Elapsed.TotalMilliseconds,10:0.0} | {text}");

    /// <summary>Pumps the thread's messages until <paramref name="done"/> or the timeout.</summary>
    internal static void Pump(Func<bool> done, int timeout)
    {
        var end = Clock.ElapsedMilliseconds + timeout;
        while (!done() && Clock.ElapsedMilliseconds < end)
        {
            Msg message;
            while (PeekMessageW(&message, 0, 0, 0, 1) != 0)
            {
                TranslateMessage(&message);
                DispatchMessageW(&message);
            }

            MsgWaitForMultipleObjectsEx(0, null, 10, 0x04FF, 0x0004);
        }
    }

    internal static T Wait<T>(Task<T> task, int timeout)
    {
        Pump(() => task.IsCompleted, timeout);
        return task.IsCompleted ? task.GetAwaiter().GetResult() : throw new TimeoutException("WebView2 did not complete in time.");
    }

    /// <summary>Runs <paramref name="work"/> from the window's own message, outside any WebView2 callback.</summary>
    internal static void Post(nint window, Action work)
    {
        _posted = work;
        PostMessageW(window, WmApp + 1, 0, 0);
    }

    internal static nint CreateWindow(string mode, int cycle)
    {
        var (x, y) = mode == "shown" ? (40 + (cycle % 5 * 30), 40 + (cycle % 5 * 30)) : (-20000, 0);
        fixed (char* name = "SparLeakProbe")
        fixed (char* title = $"Leak probe {cycle}")
        {
            var window = CreateWindowExW(0, name, title, WsOverlappedWindow, x, y, 800, 600, 0, 0, GetModuleHandleW(null), null);
            if (mode != "hidden")
            {
                ShowWindow(window, SwShowNoActivate);
            }

            return window;
        }
    }

    private static void RegisterClass()
    {
        fixed (char* name = "SparLeakProbe")
        {
            var windowClass = new WndClassEx
            {
                Size = (uint)sizeof(WndClassEx),
                Procedure = &WindowProcedure,
                Instance = GetModuleHandleW(null),
                Background = GetStockObject(4),
                ClassName = name,
            };
            RegisterClassExW(&windowClass);
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static nint WindowProcedure(nint window, uint message, nint wParam, nint lParam)
    {
        if (message == WmApp + 1 && _posted is { } work)
        {
            _posted = null;
            work();
            return 0;
        }

        return DefWindowProcW(window, message, wParam, lParam);
    }

    /// <summary>A loopback HTTP server the page fetches from, so requests go through the browser's network service.</summary>
    private static int StartLoopback()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var body = new string('x', 4096);
        var response = Encoding.ASCII.GetBytes(
            $"HTTP/1.1 200 OK\r\nContent-Type: text/plain\r\nAccess-Control-Allow-Origin: *\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n{body}");
        new Thread(() =>
        {
            while (true)
            {
                using var client = listener.AcceptTcpClient();
                var stream = client.GetStream();
                var buffer = new byte[4096];
                _ = stream.Read(buffer);
                stream.Write(response);
            }
        }) { IsBackground = true }.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct UnicodeString
    {
        public ushort Length;
        public ushort MaximumLength;
        public nint Buffer;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Msg
    {
        public nint Window;
        public uint Message;
        public nint WParam;
        public nint LParam;
        public uint Time;
        public int X;
        public int Y;
        public uint Private;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WndClassEx
    {
        public uint Size;
        public uint Style;
        public delegate* unmanaged[Stdcall]<nint, uint, nint, nint, nint> Procedure;
        public int ClassExtra;
        public int WindowExtra;
        public nint Instance;
        public nint Icon;
        public nint Cursor;
        public nint Background;
        public char* MenuName;
        public char* ClassName;
        public nint SmallIcon;
    }

    [DllImport("user32")] private static extern ushort RegisterClassExW(WndClassEx* windowClass);
    [DllImport("user32")] private static extern nint CreateWindowExW(uint exStyle, char* className, char* title, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, void* parameter);
    [DllImport("user32")] internal static extern int DestroyWindow(nint window);
    [DllImport("user32")] private static extern int ShowWindow(nint window, int command);
    [DllImport("user32")] internal static extern int GetClientRect(nint window, out Rect rect);
    [DllImport("user32")] private static extern nint DefWindowProcW(nint window, uint message, nint wParam, nint lParam);
    [DllImport("user32")] private static extern int PostMessageW(nint window, uint message, nint wParam, nint lParam);
    [DllImport("user32")] private static extern int PeekMessageW(Msg* message, nint window, uint min, uint max, uint remove);
    [DllImport("user32")] private static extern int TranslateMessage(Msg* message);
    [DllImport("user32")] private static extern nint DispatchMessageW(Msg* message);
    [DllImport("user32")] private static extern uint MsgWaitForMultipleObjectsEx(uint count, nint* handles, uint milliseconds, uint wake, uint flags);
    [DllImport("user32")] private static extern uint GetGuiResources(nint process, uint flags);
    [DllImport("gdi32")] private static extern nint GetStockObject(int index);
    [DllImport("kernel32")] private static extern nint GetModuleHandleW(char* name);
    [DllImport("kernel32")] private static extern nint GetCurrentProcess();
    [DllImport("kernel32")] private static extern int GetProcessHandleCount(nint process, out uint count);
    [DllImport("kernel32")] private static extern nint OpenProcess(uint access, int inherit, int id);
    [DllImport("kernel32")] private static extern int CloseHandle(nint handle);
    [DllImport("ntdll")] private static extern int NtQueryInformationProcess(nint process, int informationClass, void* information, int length, out int returned);
}
