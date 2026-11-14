using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Spar.PageHeartbeat.Interop;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Spar.PageHeartbeat;

/// <summary>
/// The Windows app under test: one window with one WebView2 controller on a user-data folder under
/// artifacts/webview2-profiles/page-heartbeat/ (or <c>--profile-root</c> inside Windows Sandbox). Offscreen and never
/// activated by default; <c>--placement x,y</c> puts it on screen, which the host allows only inside Windows Sandbox.
/// The injected script answers the heartbeat; everything else is <see cref="Session"/>.
/// </summary>
internal sealed class Host : Session
{
    private const string Origin = "https://app.localhost";
    private const int ReloadButton = 101;
    private const int WaitButton = 102;
    private static Host? s_current;

    private readonly string _udf;
    private readonly bool _inSandbox = Environment.UserName.Equals("WDAGUtilityAccount", StringComparison.OrdinalIgnoreCase);
    private HWND _messageWindow;
    private HWND _window;
    private HWND _cover;
    private UiContext? _ui;
    private object? _environmentObject;
    private ICoreWebView2Environment8 _environment = null!;
    private ICoreWebView2Controller? _controller;
    private ICoreWebView2 _webView = null!;
    private nint _webView3;
    private nint _webView19;
    private readonly List<object> _keepAlive = [];
    private readonly TaskCompletionSource _browserExited = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private (int X, int Y) _placement = (-32000, -32000);
    private bool _controllerHidden;
    private nint _dialog;
    private bool _dialogOpen;

    private Host(Options options)
        : base(options)
    {
        var root = options.Get("profile-root") ?? Paths.Root;
        var name = options.Get("profile", "default");
        _udf = Path.Combine(root, Paths.CheckName(name));
        if (options.Get("placement") is { Length: > 0 } placement)
        {
            var parts = placement.Split(',');
            _placement = (int.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture), int.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture));
        }
    }

    public static int Run(Options options)
    {
        Log.Open(options.Get("log"));
        Win32.AssertLayouts();
        var host = new Host(options);
        if (host._placement.X > -10000 && !host._inSandbox)
        {
            Log.Line("REFUSED", "on-screen placement runs only inside Windows Sandbox");
            return 64;
        }

        var thread = new Thread(host.RunOnSta) { Name = "Spar UI" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        Log.Line("HOST-EXIT", "code=0");
        return 0;
    }

    private void RunOnSta()
    {
        s_current = this;
        CreateWindows();
        SynchronizationContext.SetSynchronizationContext(_ui);
        _ui!.Post(static async state => await ((Host)state!).MainAsync(), this);
        while (PInvoke.GetMessage(out var message, HWND.Null, 0, 0) > 0)
        {
            PInvoke.TranslateMessage(message);
            PInvoke.DispatchMessage(message);
        }
    }

    private async Task MainAsync()
    {
        try
        {
            await StartAsync();
            StartSession(_ui!);
            var start = Options.Get("start", "/index.html");
            Log.Line("NAVIGATE", $"uri={Origin}{start}");
            _webView.Navigate(Origin + start);
        }
        catch (Exception exception)
        {
            Log.Line("HOST-FAILURE", exception.ToString().ReplaceLineEndings(" | "));
            Quit();
        }
    }

    // ---- Start-up ------------------------------------------------------------------------------------------------

    private async Task StartAsync()
    {
        var started = Stopwatch.GetTimestamp();
        var handler = new EnvironmentCompletedHandler();
        var hr = NativeLoader.CreateCoreWebView2EnvironmentWithOptions(Options.Get("browser-folder")?.Trim('"'), _udf, 0, handler);
        if (hr < 0)
        {
            throw new ComFailureException("CreateCoreWebView2EnvironmentWithOptions", hr);
        }

        _environmentObject = await handler.Task;
        _environment = _environmentObject as ICoreWebView2Environment8 ?? throw new InvalidOperationException("ICoreWebView2Environment8 missing.");
        Log.Line("ENV", $"ms={Log.Ms(started):F1} version={_environment.GetBrowserVersionString()} folder={_environment.GetUserDataFolder()} sandbox={_inSandbox} cpus={Environment.ProcessorCount}");

        var exited = new BrowserProcessExitedHandler(() =>
        {
            Log.Line("BROWSER-EXITED", "");
            _browserExited.TrySetResult();
        });
        var changed = new ProcessInfosChangedHandler(() => Log.Line("PROCESS-INFOS-CHANGED", ListProcesses()));
        _keepAlive.Add(exited);
        _keepAlive.Add(changed);
        _environment.AddBrowserProcessExited(exited);
        _environment.AddProcessInfosChanged(changed);

        var controllerHandler = new ControllerCompletedHandler();
        unsafe
        {
            _environment.CreateCoreWebView2Controller((nint)_window.Value, controllerHandler);
        }

        _controller = await controllerHandler.Task;
        _webView = _controller.GetCoreWebView2();
        ResizeController();
        var settings = _webView.GetSettings();
        settings.PutAreDefaultScriptDialogsEnabled(0);
        settings.PutIsReputationCheckingRequired(0);

        AddHandler(new WebResourceRequestedHandler(OnWebResourceRequested), h => _webView.AddWebResourceRequested(h));
        ((ICoreWebView2_22)_webView).AddWebResourceRequestedFilterWithRequestSourceKinds(Origin + "/*", 0, unchecked((int)0xFFFFFFFF));
        // A second site served with the same files, to see whether a cross-site navigation escapes a hung renderer.
        ((ICoreWebView2_22)_webView).AddWebResourceRequestedFilterWithRequestSourceKinds("https://other.localhost/*", 0, unchecked((int)0xFFFFFFFF));
        AddHandler(new WebMessageReceivedHandler(OnPageMessage), h => _webView.AddWebMessageReceived(h));
        AddHandler(new NavigationStartingHandler(args =>
        {
            Log.Line("NAV-STARTING", $"id={args.GetNavigationId()} uri={args.GetUri()}");
            OnNavigationEvent("starting");
        }), h => _webView.AddNavigationStarting(h));
        AddHandler(new ContentLoadingHandler(args =>
        {
            Log.Line("CONTENT-LOADING", $"id={args.GetNavigationId()} errorPage={args.GetIsErrorPage()}");
            OnNavigationEvent("content-loading");
        }), h => _webView.AddContentLoading(h));
        AddHandler(new NavigationCompletedHandler(args =>
        {
            var ok = args.GetIsSuccess() != 0;
            Log.Line("NAV-COMPLETED", $"id={args.GetNavigationId()} success={ok} status={args.GetWebErrorStatus()}");
            OnNavigationEvent(ok ? "completed-ok" : "completed-fail");
        }), h => _webView.AddNavigationCompleted(h));
        AddHandler(new ProcessFailedHandler(OnProcessFailed), h => _webView.AddProcessFailed(h));

        _webView3 = WebView2Raw.Query(_webView, WebView2Raw.IidWebView2_3);
        _webView19 = WebView2Raw.Query(_webView, WebView2Raw.IidWebView2_19);
        if (!Options.Has("no-inject"))
        {
            var script = Embedded("www/inject.js");
            var added = new ScriptAddedHandler();
            _webView.AddScriptToExecuteOnDocumentCreated(System.Text.Encoding.UTF8.GetString(script), added);
            Log.Line("INJECT", $"hr=0x{await added.Task:X8}");
        }

        Log.Line("CONTROLLER", $"ms={Log.Ms(started):F1} browserPid={_webView.GetBrowserProcessId()} webView3={_webView3 != 0} webView19={_webView19 != 0} processes=[{ListProcesses()}]");
    }

    private void AddHandler<T>(T handler, Func<T, EventRegistrationToken> add)
        where T : class
    {
        _keepAlive.Add(handler);
        add(handler);
    }

    private void OnProcessFailed(ICoreWebView2ProcessFailedEventArgs2 args)
    {
        var kind = args.GetProcessFailedKind();
        var reason = args.GetReason();
        var detail = $"kind={KindName(kind)} reason={ReasonName(reason)} exitCode={args.GetExitCode()} description={args.GetProcessDescription()} processes=[{ListProcesses()}]";
        Log.Line("PROCESS-FAILED", detail);
        if (kind is 1 or 3)
        {
            // RENDER_PROCESS_EXITED, FRAME_RENDER_PROCESS_EXITED: the page is gone; reload it (at most once).
            OnRendererExited(detail);
        }
    }

    private static string KindName(int kind) => kind switch
    {
        0 => "browser-exited",
        1 => "render-exited",
        2 => "render-unresponsive",
        3 => "frame-render-exited",
        4 => "utility-exited",
        5 => "sandbox-helper-exited",
        6 => "gpu-exited",
        _ => "kind" + kind,
    };

    private static string ReasonName(int reason) => reason switch
    {
        0 => "unexpected",
        1 => "unresponsive",
        2 => "terminated",
        3 => "crashed",
        4 => "launch-failed",
        5 => "out-of-memory",
        6 => "profile-deleted",
        7 => "normal-exit",
        8 => "abnormal-exit",
        _ => "reason" + reason,
    };

    // ---- Processes -----------------------------------------------------------------------------------------------

    private List<(int Pid, int Kind)> Processes()
    {
        var list = new List<(int, int)>();
        if (_environment.GetProcessInfos(out var collection) < 0 || collection is null)
        {
            return list;
        }

        collection.GetCount(out var count);
        for (uint i = 0; i < count; i++)
        {
            collection.GetValueAtIndex(i, out var info);
            info!.GetProcessId(out var pid);
            info.GetKind(out var kind);
            list.Add((pid, kind));
        }

        return list;
    }

    private string ListProcesses() => string.Join(' ', Processes().Select(p => $"{ProcessKind(p.Kind)}:{p.Pid}"));

    private static string ProcessKind(int kind) => kind switch
    {
        0 => "browser",
        1 => "renderer",
        2 => "utility",
        3 => "sandbox-helper",
        4 => "gpu",
        _ => "kind" + kind,
    };

    protected override void OnStressSample() => LogMemory("stress");

    private void LogMemory(string label)
    {
        var parts = Processes().Select(p => $"{ProcessKind(p.Kind)}:{Win32.PrivateKb(p.Pid)}").ToList();
        Log.Line("MEMORY", $"label={label} hostKb={Win32.PrivateKb(0)} {string.Join(' ', parts)}");
    }

    // ---- Session hooks -------------------------------------------------------------------------------------------

    protected override int PostToPage(string text) => _webView.PostWebMessageAsString(text);

    protected override void NavigateTo(string path) => Log.Line("NAVIGATE", $"uri={path} hr=0x{_webView.Navigate(path.Contains("://", StringComparison.Ordinal) ? path : Origin + path):X8}");

    protected override void ReloadPage()
    {
        var started = Stopwatch.GetTimestamp();
        var hr = _webView.Reload();
        Log.Line("RELOAD", $"hr=0x{hr:X8} callUs={Stopwatch.GetElapsedTime(started).TotalMicroseconds:F0}");
    }

    protected override void KillRenderer()
    {
        // No WebView2 API terminates a renderer (WebView2.h 1.0.4314.35 and 1.0.4365-prerelease). The host's own browser
        // lists its renderer through GetProcessInfos; the host terminates that process.
        foreach (var (pid, kind) in Processes().Where(p => p.Kind == 1))
        {
            var started = Stopwatch.GetTimestamp();
            var error = Win32.Terminate(pid);
            Log.Line("KILL-RENDERER", $"pid={pid} error={error} callUs={Stopwatch.GetElapsedTime(started).TotalMicroseconds:F0}");
        }
    }

    protected override string WindowHiddenReason()
    {
        if (PInvoke.IsIconic(_window))
        {
            return "minimised";
        }

        if (!PInvoke.IsWindowVisible(_window))
        {
            return "window-hidden";
        }

        return _controllerHidden ? "controller-hidden" : "";
    }

    protected override bool HostAction(TimedAction action)
    {
        switch (action.Verb)
        {
            case "minimize":
                RequireSandbox(action);
                PInvoke.ShowWindow(_window, SHOW_WINDOW_CMD.SW_SHOWMINNOACTIVE);
                break;
            case "restore":
                RequireSandbox(action);
                PInvoke.ShowWindow(_window, SHOW_WINDOW_CMD.SW_SHOWNOACTIVATE);
                break;
            case "cover":
                RequireSandbox(action);
                Cover(true);
                break;
            case "uncover":
                Cover(false);
                break;
            case "offscreen":
                Move(-32000, -32000);
                break;
            case "onscreen":
                RequireSandbox(action);
                Move(_placement.X, _placement.Y);
                break;
            case "hide-controller":
                _controllerHidden = true;
                Log.Line("CONTROLLER-VISIBLE", $"value=0 hr=0x{_controller!.PutIsVisible(0):X8}");
                break;
            case "show-controller":
                _controllerHidden = false;
                Log.Line("CONTROLLER-VISIBLE", $"value=1 hr=0x{_controller!.PutIsVisible(1):X8}");
                break;
            case "suspend":
                _ = SuspendAsync();
                break;
            case "resume":
                Log.Line("RESUME", $"hr=0x{WebView2Raw.Resume(_webView3):X8}");
                break;
            case "mem":
                var level = action.Arg == "low" ? 1 : 0;
                WebView2Raw.PutMemoryUsageTargetLevel(_webView19, level);
                WebView2Raw.GetMemoryUsageTargetLevel(_webView19, out var now);
                Log.Line("MEMORY-TARGET", $"set={level} read={now}");
                break;
            case "memory":
                LogMemory(action.Arg);
                break;
            case "click":
                RequireSandbox(action);
                PInvoke.GetWindowRect(_window, out var rect);
                var sent = Win32.Click((rect.left + rect.right) / 2, (rect.top + rect.bottom) / 2);
                Log.Line("INPUT", $"kind=click sent={sent} at={(rect.left + rect.right) / 2},{(rect.top + rect.bottom) / 2}");
                break;
            case "key":
                RequireSandbox(action);
                Log.Line("INPUT", $"kind=key sent={Win32.Key(0x41)}");
                break;
            case "stop":
                Log.Line("STOP", $"hr=0x{_webView.Stop():X8}");
                break;
            case "exec":
                _ = ExecAsync(action.Arg);
                break;
            default:
                return false;
        }

        return true;
    }

    private void RequireSandbox(TimedAction action)
    {
        if (!_inSandbox)
        {
            Log.Line("REFUSED", $"verb={action.Verb} runs only inside Windows Sandbox");
            throw new InvalidOperationException("Window-state changes and input run only inside Windows Sandbox.");
        }
    }

    private async Task SuspendAsync()
    {
        var handler = new TrySuspendHandler();
        var started = Stopwatch.GetTimestamp();
        var hr = WebView2Raw.TrySuspend(_webView3, handler);
        var (completedHr, result) = await handler.Task.WaitAsync(TimeSpan.FromSeconds(30));
        WebView2Raw.IsSuspended(_webView3, out var suspended);
        Log.Line("SUSPEND", $"hr=0x{hr:X8} completedHr=0x{completedHr:X8} result={result} isSuspended={suspended} ms={Log.Ms(started):F1}");
    }

    private async Task ExecAsync(string script)
    {
        var handler = new ExecuteScriptHandler();
        var started = Stopwatch.GetTimestamp();
        var hr = _webView.ExecuteScript(script, handler);
        var completed = await Task.WhenAny(handler.Task, Task.Delay(60000));
        Log.Line("EXEC", completed == handler.Task
            ? $"hr=0x{hr:X8} completedHr=0x{handler.Task.Result.Hr:X8} result={handler.Task.Result.Result} ms={Log.Ms(started):F0}"
            : $"hr=0x{hr:X8} completed=False after=60000ms");
    }

    // ---- Unresponsive dialog (Windows Sandbox only) -------------------------------------------------------------

    protected override unsafe void ShowUnresponsiveDialog()
    {
        if (!_inSandbox)
        {
            Log.Line("DIALOG-SKIPPED", "native dialogs run only inside Windows Sandbox");
            return;
        }

        if (_dialogOpen)
        {
            return;
        }

        // Shown from a posted callback, so the heartbeat tick that raised the alarm returns first. TaskDialogIndirect
        // runs a nested message loop: the UI context, the heartbeat and the pump probe keep running inside it.
        _ui!.Post(static state => ((Host)state!).RunDialog(), this);
    }

    private unsafe void RunDialog()
    {
        _dialogOpen = true;
        var strings = new List<nint>();
        char* S(string value)
        {
            var pointer = Marshal.StringToHGlobalUni(value);
            strings.Add(pointer);
            return (char*)pointer;
        }

        try
        {
            var buttons = stackalloc Win32.TaskDialogButton[2];
            buttons[0] = new Win32.TaskDialogButton { Id = WaitButton, Text = S("&Wait") };
            buttons[1] = new Win32.TaskDialogButton { Id = ReloadButton, Text = S("&Reload page") };
            var config = new Win32.TaskDialogConfig
            {
                Size = (uint)sizeof(Win32.TaskDialogConfig),
                Owner = (nint)_window.Value,
                Flags = Win32.TdfAllowDialogCancellation | Win32.TdfPositionRelativeToWindow | Win32.TdfCallbackTimer,
                WindowTitle = S("Page heartbeat spike"),
                MainIcon = Win32.TdWarningIcon,
                MainInstruction = S("The page is not responding"),
                Content = S("You can wait for it to respond, or reload the page. Work in progress in the page may be lost."),
                ButtonCount = 2,
                Buttons = buttons,
                DefaultButton = WaitButton,
                Callback = &DialogCallback,
            };
            if (Pump is not null)
            {
                Pump.Phase = "dialog";
            }

            var started = Stopwatch.GetTimestamp();
            Log.Line("DIALOG-SHOW", "");
            int pressed;
            var hr = Win32.TaskDialogIndirect(&config, &pressed, null, null);
            Log.Line("DIALOG-CLOSED", $"hr=0x{hr:X8} button={(pressed == ReloadButton ? "reload" : pressed == WaitButton ? "wait" : pressed.ToString(System.Globalization.CultureInfo.InvariantCulture))} openMs={Log.Ms(started):F0}");
            _dialogOpen = false;
            _dialog = 0;
            if (pressed == ReloadButton)
            {
                ReloadFromDialog();
            }
        }
        finally
        {
            strings.ForEach(Marshal.FreeHGlobal);
            _dialogOpen = false;
        }
    }

    protected override void CloseUnresponsiveDialog(string why)
    {
        if (_dialog != 0)
        {
            Log.Line("DIALOG-AUTO-CLOSE", $"why={why}");
            PInvoke.PostMessage(new HWND(_dialog), Win32.TdmClickButton, new WPARAM(WaitButton), default);
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int DialogCallback(nint hwnd, uint message, nint wParam, nint lParam, nint data)
    {
        try
        {
            var host = s_current!;
            switch (message)
            {
                case Win32.TdnCreated:
                    host._dialog = hwnd;
                    Log.Line("DIALOG-CREATED", $"hwnd=0x{hwnd:X}");
                    if (host.Options.Get("dialog-answer") is { Length: > 0 } answer)
                    {
                        // Test automation: answer after a delay, from the UI context (no input on any desktop).
                        var after = host.Options.Int("dialog-after", 3000);
                        _ = AnswerLaterAsync(hwnd, answer == "reload" ? ReloadButton : WaitButton, after);
                    }

                    break;
                case Win32.TdnButtonClicked:
                    Log.Line("DIALOG-BUTTON", $"id={wParam}");
                    break;
                case Win32.TdnDestroyed:
                    host._dialog = 0;
                    break;
            }
        }
        catch (Exception exception)
        {
            Log.Line("DIALOG-CALLBACK-ERROR", exception.Message);
        }

        return 0;
    }

    private static async Task AnswerLaterAsync(nint hwnd, int button, int after)
    {
        await Task.Delay(after);
        if (s_current?._dialog == hwnd)
        {
            Log.Line("DIALOG-ANSWER", $"button={(button == ReloadButton ? "reload" : "wait")}");
            PInvoke.PostMessage(new HWND(hwnd), Win32.TdmClickButton, new WPARAM((nuint)button), default);
        }
    }

    // ---- Shutdown ------------------------------------------------------------------------------------------------

    protected override void Quit() => _ = QuitAsync();

    private async Task QuitAsync()
    {
        if (_controller is not null)
        {
            _controller.Close();
            _controller = null;
        }

        await Task.WhenAny(_browserExited.Task, Task.Delay(Options.Int("exit-wait", 8000)));
        if (_cover != HWND.Null)
        {
            PInvoke.DestroyWindow(_cover);
        }

        PInvoke.DestroyWindow(_window);
        PInvoke.PostQuitMessage(0);
    }

    // ---- Window and assets ---------------------------------------------------------------------------------------

    private unsafe void CreateWindows()
    {
        var instance = (HINSTANCE)(nint)PInvoke.GetModuleHandle((char*)null).Value;
        fixed (char* className = "SparPageHeartbeatSpike")
        fixed (char* coverClass = "SparPageHeartbeatCover")
        fixed (char* title = "Page heartbeat spike")
        {
            var windowClass = new WNDCLASSEXW
            {
                cbSize = (uint)sizeof(WNDCLASSEXW),
                lpfnWndProc = &WindowProcedure,
                hInstance = instance,
                hCursor = PInvoke.LoadCursor(HINSTANCE.Null, PInvoke.IDC_ARROW),
                lpszClassName = className,
            };
            if (PInvoke.RegisterClassEx(windowClass) == 0)
            {
                throw new InvalidOperationException("RegisterClassEx failed: " + Marshal.GetLastPInvokeError());
            }

            var cover = windowClass;
            cover.lpszClassName = coverClass;
            cover.hbrBackground = new Windows.Win32.Graphics.Gdi.HBRUSH(9); // COLOR_WINDOWTEXT + 1: opaque, dark
            PInvoke.RegisterClassEx(cover);

            _messageWindow = PInvoke.CreateWindowEx(0, className, null, 0, 0, 0, 0, 0, new HWND(-3), HMENU.Null, instance, null);
            _ui = new UiContext(_messageWindow);

            // Never activated: runs must not take focus. Offscreen unless --placement (Windows Sandbox only).
            _window = PInvoke.CreateWindowEx(WINDOW_EX_STYLE.WS_EX_NOACTIVATE, className, title,
                WINDOW_STYLE.WS_OVERLAPPEDWINDOW, _placement.X, _placement.Y, Options.Int("width", 640), Options.Int("height", 420), HWND.Null, HMENU.Null, instance, null);
            PInvoke.ShowWindow(_window, SHOW_WINDOW_CMD.SW_SHOWNOACTIVATE);
            Log.Line("WINDOW", $"hwnd=0x{(nint)_window.Value:X} placement={_placement.X},{_placement.Y}");
        }
    }

    private unsafe void Cover(bool on)
    {
        if (!on)
        {
            if (_cover != HWND.Null)
            {
                PInvoke.DestroyWindow(_cover);
                _cover = HWND.Null;
            }

            Log.Line("COVER", "on=False");
            return;
        }

        PInvoke.GetWindowRect(_window, out var rect);
        var instance = (HINSTANCE)(nint)PInvoke.GetModuleHandle((char*)null).Value;
        fixed (char* coverClass = "SparPageHeartbeatCover")
        {
            // A plain opaque top-level window of this process, 40 px larger on every side, above the app window.
            _cover = PInvoke.CreateWindowEx(WINDOW_EX_STYLE.WS_EX_NOACTIVATE, coverClass, null, WINDOW_STYLE.WS_POPUP,
                rect.left - 40, rect.top - 40, rect.right - rect.left + 80, rect.bottom - rect.top + 80, HWND.Null, HMENU.Null, instance, null);
        }

        PInvoke.ShowWindow(_cover, SHOW_WINDOW_CMD.SW_SHOWNOACTIVATE);
        Log.Line("COVER", $"on=True hwnd=0x{(nint)_cover.Value:X}");
    }

    private void Move(int x, int y)
    {
        PInvoke.SetWindowPos(_window, HWND.Null, x, y, 0, 0, SET_WINDOW_POS_FLAGS.SWP_NOSIZE | SET_WINDOW_POS_FLAGS.SWP_NOZORDER | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE);
        _controller?.NotifyParentWindowPositionChanged();
        Log.Line("MOVE", $"to={x},{y}");
    }

    private void ResizeController()
    {
        if (_controller is null)
        {
            return;
        }

        PInvoke.GetClientRect(_window, out var client);
        _controller.PutBounds(new Rect { Left = 0, Top = 0, Right = client.right, Bottom = client.bottom });
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static LRESULT WindowProcedure(HWND window, uint message, WPARAM wParam, LPARAM lParam)
    {
        try
        {
            var host = s_current;
            switch (message)
            {
                case UiContext.DrainMessage:
                    host?._ui?.Drain();
                    return default;
                case PInvoke.WM_SIZE when host is not null && window == host._window:
                    Log.Line("WM-SIZE", $"type={wParam.Value}");
                    host.ResizeController();
                    break;
                case PInvoke.WM_CLOSE:
                    return default;
            }
        }
        catch (Exception exception)
        {
            Log.Line("WNDPROC-FAILURE", exception.ToString().ReplaceLineEndings(" | "));
        }

        return PInvoke.DefWindowProc(window, message, wParam, lParam);
    }

    private void OnWebResourceRequested(ICoreWebView2WebResourceRequestedEventArgs args)
    {
        var path = new Uri(args.GetRequest().GetUri()).AbsolutePath;
        var content = Embedded("www/" + path.TrimStart('/'));
        if (content.Length == 0)
        {
            args.PutResponse(Respond([], 404, "Not Found", "text/plain"));
            return;
        }

        var type = Path.GetExtension(path) switch
        {
            ".js" => "text/javascript; charset=utf-8",
            ".css" => "text/css; charset=utf-8",
            _ => "text/html; charset=utf-8",
        };
        args.PutResponse(Respond(content, 200, "OK", type));
    }

    private static byte[] Embedded(string key)
    {
        using var stream = typeof(Host).Assembly.GetManifestResourceStream(key);
        if (stream is null)
        {
            return [];
        }

        var content = new byte[stream.Length];
        stream.ReadExactly(content);
        return content;
    }

    private unsafe ICoreWebView2WebResourceResponse Respond(ReadOnlySpan<byte> content, int status, string reason, string contentType)
    {
        nint stream;
        fixed (byte* data = content)
        {
            stream = NativeLoader.SHCreateMemStream(data, (uint)content.Length);
        }

        try
        {
            return _environment.CreateWebResourceResponse(stream, status, reason, $"Content-Type: {contentType}\r\nCache-Control: no-store");
        }
        finally
        {
            Marshal.Release(stream);
        }
    }
}

/// <summary>User-data folders of this spike live under artifacts/webview2-profiles/page-heartbeat/.</summary>
internal static class Paths
{
    private static readonly Lazy<string> s_repository = new(() =>
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "global.json")) && Directory.Exists(Path.Combine(directory.FullName, "spikes")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("Run from inside the repository, or pass --profile-root.");
    });

    public static string Root => Path.Combine(s_repository.Value, "artifacts", "webview2-profiles", "page-heartbeat");

    public static string CheckName(string name) =>
        name.Length > 0 && name.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_')
            ? name
            : throw new ArgumentException("Profile names are ASCII letters, digits, '-' and '_'.", nameof(name));
}
