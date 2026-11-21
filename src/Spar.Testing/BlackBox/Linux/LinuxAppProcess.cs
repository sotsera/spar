using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace Spar.Testing;

/// <summary>
/// A launched app's processes on Linux, the counterpart of Windows' job object: the app is started with .NET's
/// <c>KillOnParentExit</c>, so the kernel ends it when the test process ends however it ends (WebKit's processes follow
/// it: measured in <c>spikes/linux-blackbox-atspi</c>), and every process of the launch carries a tag in its environment
/// (<see cref="LaunchVariable"/>), so the tree is found even after the app exited and the system adopted its children.
/// The launch's private session bus (<see cref="LinuxDesktop.StartBus"/>) and the services it activated carry a tag of
/// their own and end with the launch. Standard output and error are read as they come.
/// </summary>
[SupportedOSPlatform("linux")]
internal sealed partial class LinuxAppProcess : IBlackBoxProcess
{
    /// <summary>The variable that tags every process of one launch: the app and everything it starts.</summary>
    public const string LaunchVariable = "SPAR_BLACK_BOX_LAUNCH";

    /// <summary>The variable that tags the launch's session bus and the services it starts.</summary>
    public const string BusVariable = "SPAR_BLACK_BOX_BUS";

    private const int SignalTerminate = 15;
    private const int SignalKill = 9;
    private const int SignalStop = 19;

    private readonly Process _process;
    private readonly Process _bus;
    private readonly string _tag;
    private readonly string? _wrapped;
    private readonly StringBuilder _output = new();
    private readonly StringBuilder _error = new();
    private readonly TaskCompletionSource _outputEnded = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _errorEnded = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int? _processId;

    private LinuxAppProcess(Process process, Process bus, string tag, string? wrapped)
    {
        _process = process;
        _bus = bus;
        _tag = tag;
        _wrapped = wrapped;
        Exited = ExitAsync();
        process.OutputDataReceived += (_, line) => Append(_output, line.Data, _outputEnded);
        process.ErrorDataReceived += (_, line) => Append(_error, line.Data, _errorEnded);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
    }

    /// <summary>The app's process: the one started, or through a wrapper the descendant that runs the executable.</summary>
    public int ProcessId
    {
        get
        {
            if (_wrapped is null || _processId is not null)
            {
                return _processId ?? _process.Id;
            }

            var found = Descendant(_process.Id, _wrapped);
            if (found != _process.Id)
            {
                _processId = found;
            }

            return found;
        }
    }

    public string StandardOutput => Text(_output);

    public string StandardError => Text(_error);

    public Task<int> Exited { get; }

    public IReadOnlyList<int> ProcessIds => Tagged(LaunchVariable, _tag);

    /// <summary>
    /// Starts <paramref name="executable"/> with <paramref name="environment"/> as its whole environment, its private
    /// bus started first with the same environment; through <paramref name="wrapper"/> (a command the executable is
    /// appended to) when given, for Spar's own tests that hide a library from the app.
    /// </summary>
    public static LinuxAppProcess Start(
        string executable,
        IReadOnlyList<string> arguments,
        Dictionary<string, string> environment,
        string workingDirectory,
        string? sessionBus,
        IReadOnlyList<string>? wrapper)
    {
        var tag = Guid.NewGuid().ToString("N");
        var busEnvironment = new Dictionary<string, string>(environment, StringComparer.Ordinal) { [BusVariable] = tag };
        busEnvironment.Remove(LaunchVariable);

        // No display for the bus and what it starts: at-spi2-core's bus launcher publishes its bus's address on the
        // display's root window (AT_SPI_BUS), where the registry and GTK look before their own session bus. Every launch
        // of a test process shares one Xvfb, so parallel launchers overwrote each other's address, and a registry
        // registered on another launch's bus: measured, 10 of 80 starts eight at a time, 80 of 80 without a display.
        busEnvironment.Remove("DISPLAY");
        var (bus, address) = LinuxDesktop.StartBus(busEnvironment);
        try
        {
            environment["DBUS_SESSION_BUS_ADDRESS"] = sessionBus ?? address;
            environment[LaunchVariable] = tag;
            var start = new ProcessStartInfo(wrapper is [var command, ..] ? command : executable)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                UseShellExecute = false,
                WorkingDirectory = workingDirectory,
                KillOnParentExit = true,
            };
            foreach (var argument in wrapper?.Skip(1) ?? [])
            {
                start.ArgumentList.Add(argument);
            }

            if (wrapper is { Count: > 0 })
            {
                start.ArgumentList.Add(executable);
            }

            foreach (var argument in arguments)
            {
                start.ArgumentList.Add(argument);
            }

            start.Environment.Clear();
            foreach (var (variable, value) in environment)
            {
                start.Environment[variable] = value;
            }

            var process = Process.Start(start) ?? throw new InvalidOperationException($"The app could not be started: '{executable}'.");
            var name = wrapper is { Count: > 0 } ? Path.GetFileName(executable) : null;
            return new LinuxAppProcess(process, bus, tag, name is null ? null : name[..Math.Min(15, name.Length)]) { BusAddress = sessionBus ?? address };
        }
        catch
        {
            LinuxDesktop.Stop(bus);
            throw;
        }
    }

    /// <summary>The app's session bus, which its accessibility bus is found through.</summary>
    public string BusAddress { get; private init; } = "";

    /// <summary>Ends every process of the launch at once (<c>SIGKILL</c>); the app's exit code is then 137.</summary>
    public void Kill()
    {
        foreach (var id in ProcessIds.Prepend(_process.Id).Distinct())
        {
            _ = SendSignal(id, SignalKill);
        }
    }

    /// <summary>
    /// The launch's accessibility client: AT-SPI on its bus, its dialogs reported with <paramref name="appName"/>, and
    /// its session end <c>SIGTERM</c>, waited for until the app exited or <paramref name="sessionEndWait"/> passed.
    /// </summary>
    public AtSpiAutomation CreateAutomation(string appName, TimeSpan sessionEndWait) =>
        new(BusAddress, appName, async _ =>
        {
            Terminate();
            await Task.WhenAny(Exited, Task.Delay(sessionEndWait));
            return true;
        });

    /// <summary>Sends <c>SIGTERM</c>, the session's end on Linux, to the app's own process.</summary>
    public void Terminate() => _ = SendSignal(ProcessId, SignalTerminate);

    public async Task<bool> WaitForTreeAsync(TimeSpan limit)
    {
        var deadline = DateTime.UtcNow + limit;
        while (ProcessIds.Count > 0 || !_process.HasExited)
        {
            if (DateTime.UtcNow >= deadline)
            {
                return false;
            }

            await Task.Delay(25, CancellationToken.None);
        }

        return true;
    }

    public Task<bool> WaitForOutputAsync(TimeSpan limit) =>
        Task.WhenAll(_outputEnded.Task, _errorEnded.Task).WaitAsync(limit).ContinueWith(static task => task.IsCompletedSuccessfully, TaskScheduler.Default);

    /// <summary>Stops the app's own process (<c>SIGSTOP</c>), as a hung app: for Spar's own tests.</summary>
    public void SuspendProcess() => _ = SendSignal(ProcessId, SignalStop);

    /// <summary>Ends the launch's bus and what it started; the app's processes are ended by disposal of the app first.</summary>
    public void Dispose()
    {
        LinuxDesktop.Stop(_bus);
        foreach (var id in Tagged(BusVariable, _tag))
        {
            _ = SendSignal(id, SignalKill);
        }

        _process.Dispose();
    }

    /// <summary>The running processes whose environment carries <paramref name="variable"/>=<paramref name="tag"/>.</summary>
    internal static List<int> Tagged(string variable, string tag)
    {
        var marker = Encoding.UTF8.GetBytes($"{variable}={tag}\0");
        var found = new List<int>();
        foreach (var folder in Directory.EnumerateDirectories("/proc"))
        {
            if (!int.TryParse(Path.GetFileName(folder), NumberStyles.None, CultureInfo.InvariantCulture, out var id) || IsZombie(id))
            {
                continue;
            }

            try
            {
                var environment = File.ReadAllBytes(Path.Combine(folder, "environ"));
                if (environment.AsSpan().IndexOf(marker) >= 0 || environment.AsSpan().EndsWith(marker.AsSpan(0, marker.Length - 1)))
                {
                    found.Add(id);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Gone meanwhile, or not ours to read.
            }
        }

        return found;
    }

    private static bool IsZombie(int id)
    {
        try
        {
            var stat = File.ReadAllText($"/proc/{id}/stat");
            var state = stat.LastIndexOf(')') + 2;
            return state < stat.Length && stat[state] is 'Z' or 'X';
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    /// <summary>The first process below <paramref name="root"/> named <paramref name="name"/> (the kernel keeps 15 characters), or the root.</summary>
    private static int Descendant(int root, string name)
    {
        var pending = new Stack<int>([root]);
        while (pending.TryPop(out var parent))
        {
            foreach (var task in Directory.Exists($"/proc/{parent}/task") ? Directory.EnumerateDirectories($"/proc/{parent}/task") : [])
            {
                string children;
                try
                {
                    children = File.ReadAllText(Path.Combine(task, "children"));
                }
                catch (IOException)
                {
                    continue;
                }

                foreach (var child in children.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(text => int.Parse(text, CultureInfo.InvariantCulture)))
                {
                    try
                    {
                        if (File.ReadAllText($"/proc/{child}/comm").Trim() == name)
                        {
                            return child;
                        }
                    }
                    catch (IOException)
                    {
                        // Ended meanwhile.
                    }

                    pending.Push(child);
                }
            }
        }

        return root;
    }

    private async Task<int> ExitAsync()
    {
        await _process.WaitForExitAsync();
        return _process.ExitCode;
    }

    private static string Text(StringBuilder text)
    {
        lock (text)
        {
            return text.ToString();
        }
    }

    private static void Append(StringBuilder text, string? line, TaskCompletionSource ended)
    {
        if (line is null)
        {
            ended.TrySetResult();
            return;
        }

        lock (text)
        {
            text.Append(line).Append('\n');
        }
    }

    private static int SendSignal(int processId, int signal) => kill(processId, signal);

    [LibraryImport("libc", SetLastError = true)]
    private static partial int kill(int pid, int sig);
}
