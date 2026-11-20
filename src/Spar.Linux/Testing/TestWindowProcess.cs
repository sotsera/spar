using Spar.Hosting;
using Spar.Linux.Interop;
using Spar.Runtime;

namespace Spar.Linux.Testing;

/// <summary>
/// What every app window of a Linux test process shares (plan section 5, "Rules for Spar's own code"; decision "M1c
/// step 7's shape: Linux window and black-box tests"):
/// <list type="bullet">
/// <item>the session (<see cref="TestEnvironment"/>): private home and XDG folders, D-Bus and Xvfb;</item>
/// <item>the engine's prerequisite check, run once before the first window, so a missing WebKitGTK or a refused sandbox
/// fails the tests with the start-up check's message instead of a native abort, and the browser environment as an app
/// sets it before GTK starts (the overrides and every <c>JSC_*</c> option removed, WebAssembly as the first app declared
/// it, the software-GPU default);</item>
/// <item>one main-loop thread, which initialised GTK and owns every window of the process;</item>
/// <item>one colour scheme at a time, since GTK's dark preference, which the page's <c>prefers-color-scheme</c>
/// follows, is the whole process's;</item>
/// <item>trusted input (<see cref="XInput"/>), one event at a time for every window;</item>
/// <item>which web process serves which window: WebKitGTK names no process of a view, so the web process that appears
/// while a window loads its first page is that window's, and windows take turns at that.</item>
/// </list>
/// Created only by the test host, behind the test-host feature switch, so an app's executable has none of it.
/// </summary>
internal sealed class TestWindowProcess
{
    private static readonly Lock Gate = new();
    private static TestWindowProcess? s_shared;
    private static SparTestWindowUnavailableException? s_unavailable;

    private readonly Queue<(bool Dark, TaskCompletionSource Turn)> _waiting = new();
    private const int PlaceWidth = 1280;
    private const int PlaceHeight = 840;
    private const int PlaceColumns = 5;
    private const int PlaceRows = 5;

    private readonly HashSet<int> _owned = [];
    private readonly HashSet<int> _places = [];
    private readonly bool _webAssembly;
    private int _open;
    private bool _dark;
    private bool _schemeSet;

    private TestWindowProcess(MainLoop ui, XInput input, bool webAssembly)
    {
        Ui = ui;
        Input = input;
        _webAssembly = webAssembly;
    }

    /// <summary>The test process's one main-loop thread.</summary>
    public MainLoop Ui { get; }

    /// <summary>Trusted input on the test display.</summary>
    public XInput Input { get; }

    /// <summary>One window at a time loads its first page, so the web process that appears meanwhile is known to be its.</summary>
    public SemaphoreSlim FirstLoads { get; } = new(1, 1);

    /// <summary>
    /// The process's shared part, created by the first window of <paramref name="app"/>; it throws
    /// <see cref="SparTestWindowUnavailableException"/> when the machine cannot run window tests, the same for every window.
    /// </summary>
    public static TestWindowProcess Get(SparApp app, SparTestWindowRequest request)
    {
        lock (Gate)
        {
            if (s_unavailable is { } unavailable)
            {
                throw new SparTestWindowUnavailableException(unavailable.Message);
            }

            try
            {
                return s_shared ??= Create(app, request);
            }
            catch (SparTestWindowUnavailableException exception)
            {
                s_unavailable = exception;
                throw;
            }
        }
    }

    /// <summary>
    /// The engine's WebAssembly setting is the process's, set once before GTK started; a window whose app declares the
    /// other would run with it, so it is refused.
    /// </summary>
    public void CheckWebAssembly(SparPageSecurity security)
    {
        if (security.AllowWebAssembly != _webAssembly)
        {
            throw new InvalidOperationException(
                $"This test process's web engine has WebAssembly {(_webAssembly ? "on" : "off")}, as the first app's page security declared: JSC_useWasm is set once, before GTK starts. Test an app that declares {(security.AllowWebAssembly ? "AllowWebAssembly()" : "no AllowWebAssembly()")} in a test process of its own.");
        }
    }

    /// <summary>
    /// A place of the test display no other open window has (any thread): windows of tests that run at once never cover
    /// each other, so a window mapped meanwhile cannot take another's click. Its top-left corner, in screen pixels.
    /// </summary>
    public (int Slot, int X, int Y) RentPlace()
    {
        lock (_places)
        {
            var slot = 0;
            while (!_places.Add(slot))
            {
                slot++;
            }

            var place = slot % (PlaceColumns * PlaceRows);
            return (slot, (place % PlaceColumns) * PlaceWidth, (place / PlaceColumns) * PlaceHeight);
        }
    }

    public void ReturnPlace(int slot)
    {
        lock (_places)
        {
            _places.Remove(slot);
        }
    }

    /// <summary>
    /// Waits until windows of <paramref name="dark"/>'s scheme may open (loop thread), then counts this one in: GTK's dark
    /// preference, which WebKitGTK's <c>prefers-color-scheme</c> follows, is the default display's, so the process's
    /// windows have one scheme at a time (measured: a view on a second display with its own dark preference stayed
    /// light). A window of the other scheme waits until the open ones closed, and windows asking after it wait behind
    /// it, so neither scheme waits for ever. <see cref="LeaveScheme"/> when the window is gone.
    /// </summary>
    public Task EnterSchemeAsync(bool dark)
    {
        if (_waiting.Count == 0 && (_open == 0 || _dark == dark))
        {
            UseScheme(dark);
            return Task.CompletedTask;
        }

        var turn = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _waiting.Enqueue((dark, turn));
        return turn.Task;
    }

    /// <summary>A window counted in by <see cref="EnterSchemeAsync"/> is gone (loop thread): the next scheme may come.</summary>
    public void LeaveScheme()
    {
        _open--;
        while (_waiting.TryPeek(out var next) && (_open == 0 || _dark == next.Dark))
        {
            _waiting.Dequeue();
            UseScheme(next.Dark);
            next.Turn.TrySetResult();
        }
    }

    private unsafe void UseScheme(bool dark)
    {
        _open++;
        if (_schemeSet && _dark == dark)
        {
            return;
        }

        _dark = dark;
        _schemeSet = true;
        GValue value = default;
        GLib.g_value_init(&value, GLib.TypeBoolean);
        GLib.g_value_set_boolean(&value, dark ? 1 : 0);
        GLib.g_object_set_property(Gtk.gtk_settings_get_default(), "gtk-application-prefer-dark-theme", &value);
        GLib.g_value_unset(&value);
    }

    /// <summary>
    /// The web processes that appeared since the others were given to their windows: this window's, while it holds
    /// <see cref="FirstLoads"/>, or after a reload when exactly one is new and no window is loading its first page.
    /// </summary>
    public List<int> Claim(bool firstLoad)
    {
        var present = WebProcesses.Below(Environment.ProcessId).Where(IsWebProcess).Select(process => process.Id).ToList();
        lock (_owned)
        {
            _owned.IntersectWith(present);
            var unclaimed = present.Where(id => !_owned.Contains(id)).ToList();
            if (!firstLoad && (unclaimed.Count != 1 || FirstLoads.CurrentCount == 0))
            {
                return [];
            }

            _owned.UnionWith(unclaimed);
            return unclaimed;
        }
    }

    /// <summary>A WebKit web process (<c>/proc</c> names are cut at 15 characters).</summary>
    public static bool IsWebProcess((int Id, string Name) process) =>
        process.Name.StartsWith("WebKitWebProces", StringComparison.Ordinal);

    private static TestWindowProcess Create(SparApp app, SparTestWindowRequest request)
    {
        var log = new HostLog(standardError: false);
        var environment = TestEnvironment.Create(request.HomesFolder);
        try
        {
            if (Prerequisites.CheckDisplay(Environment.GetEnvironmentVariable) is { } noDisplay)
            {
                throw Unavailable(noDisplay);
            }

            // As an app does before WebKit's libraries load, stricter than a Development build: the inspector servers, the
            // sandbox override and every JSC_ option and GTK_DEBUG of the developer's environment are removed.
            if (EngineEnvironment.Filter(allowed: false, development: false, log) is { } kept)
            {
                throw new InvalidOperationException("Window tests cannot start: " + kept);
            }

            if (Prerequisites.CheckLibraries(Distribution.Read(), log, out _) is { } missing)
            {
                throw Unavailable(missing);
            }

            var security = PageSecuritySetup.Resolve(app, log.Logger);
            if (EngineEnvironment.Apply(security.AllowWebAssembly, log) is { } unset)
            {
                throw new InvalidOperationException("Window tests cannot start: " + unset);
            }

            GpuCheck.Apply(app.Builder.BrowserOptions.Acceleration, log);
            var ui = MainLoop.StartShared(
                exception => log.Error("The test windows' main loop failed", exception),
                _ => Gtk.gtk_init_check() != 0)
                ?? throw Unavailable(Prerequisites.DisplayNotOpened(environment.Display));
            var input = XInput.Open(environment.Display, () => TurnAsync(ui));
            var process = new TestWindowProcess(ui, input, security.AllowWebAssembly);
            AppDomain.CurrentDomain.ProcessExit += (_, _) => environment.Stop();
            log.Info($"Window tests on {environment.DisplayNote}; session under {environment.Root}.");
            return process;
        }
        catch
        {
            environment.Stop();
            throw;
        }
    }

    private static SparTestWindowUnavailableException Unavailable(StartupProblem problem) =>
        new("Window tests cannot run here: " + StartupReport.Describe(problem));

    /// <summary>Completes once the loop has run what was queued before it.</summary>
    private static Task TurnAsync(MainLoop ui)
    {
        var turned = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ui.Post(() => turned.TrySetResult(), "test input turn");
        return turned.Task;
    }
}
