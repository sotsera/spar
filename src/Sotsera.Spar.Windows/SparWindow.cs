using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using Sotsera.Spar.Windows.Interop;

namespace Sotsera.Spar.Windows;

public sealed unsafe class SparWindow
{
    private const uint CloseMessage = 0x0010;
    private const uint DestroyMessage = 0x0002;
    private const uint SizeMessage = 0x0005;
    private const uint DispatchMessage = 0x8001;
    private const uint FinishMessage = 0x8002;
    private const int MaximumPostedActions = 256;

    [ThreadStatic]
    private static SparWindow? _current;

    private readonly SparWindowOptions _options;
    private readonly ConcurrentQueue<Action> _posted = new();
    private readonly uint _threadId;
    private IWebViewEnvironment? _environment;
    private IWebViewController? _controller;
    private IWebView? _webView;
    private Exception? _failure;
    private nint _handle;
    private int _pendingCreations;
    private long _navigationToken;
    private bool _subscribed;
    private bool _closeRequested;
    private bool _closed;

    public nint Handle => _handle;

    public string? BrowserVersion
    {
        get;
        private set;
    }

    public string? UserDataFolder
    {
        get;
        private set;
    }

    private SparWindow(SparWindowOptions options)
    {
        _options = options;
        _threadId = NativeMethods.GetCurrentThreadId();
    }

    /// <summary>Queues work onto the native UI thread. Rejected after shutdown begins.</summary>
    /// <remarks>Callback exceptions end the session and are rethrown from Run after native cleanup.</remarks>
    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        lock (_posted)
        {
            ObjectDisposedException.ThrowIf(_closeRequested || _closed, this);
            if (_posted.Count >= MaximumPostedActions)
            {
                throw new InvalidOperationException("The Spar UI work queue is full. Wait before posting more work.");
            }

            _posted.Enqueue(action);
            if (!NativeMethods.PostMessage(_handle, DispatchMessage, 0, 0))
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }
        }
    }

    /// <summary>Requests shutdown from any thread. Outstanding native creation callbacks must finish first.</summary>
    public void Close()
    {
        lock (_posted)
        {
            if (_closed || _closeRequested)
            {
                return;
            }

            if (!NativeMethods.PostMessage(_handle, CloseMessage, 0, 0))
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }
        }
    }

    internal static void Run(SparWindowOptions options)
    {
        if (_current is not null)
        {
            throw new InvalidOperationException("A Spar window is already running on this thread.");
        }

        Marshal.ThrowExceptionForHR(NativeMethods.CoInitializeEx(0, 2));
        try
        {
            var profile = Path.GetFullPath(options.UserDataFolder);
            Directory.CreateDirectory(profile);
            using var lease = new FileStream(Path.Combine(profile, ".spar-profile.lock"), FileMode.OpenOrCreate,
                FileAccess.ReadWrite, FileShare.None);
            var window = new SparWindow(options);
            _current = window;
            try
            {
                window.RunWindow(profile);
            }
            finally
            {
                _current = null;
            }
        }
        finally
        {
            NativeMethods.CoUninitialize();
        }
    }

    private void RunWindow(string profile)
    {
        var className = $"Sotsera.Spar.{Guid.NewGuid():N}";
        var instance = NativeMethods.GetModuleHandle(null);
        fixed (char* name = className)
        {
            var windowClass = new NativeWindowClass
            {
                Size = (uint)sizeof(NativeWindowClass),
                Instance = instance,
                ClassName = name,
                WindowProcedure = &WindowProcedure,
                Background = 6
            };
            if (NativeMethods.RegisterClassEx(in windowClass) == 0)
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }
        }

        try
        {
            _handle = NativeMethods.CreateWindowEx(0, className, _options.Title, 0x00CF0000,
                unchecked((int)0x80000000), unchecked((int)0x80000000), _options.Width, _options.Height,
                0, 0, instance, 0);
            if (_handle == 0)
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }

            NativeMethods.ShowWindow(_handle, 5);
            Guard(() =>
            {
                _options.Created?.Invoke(this);
                StartEnvironment(profile);
            });

            while (!_closed)
            {
                var result = NativeMethods.GetMessage(out var message, 0, 0, 0);
                if (result < 0)
                {
                    Guard(() => throw new Win32Exception(Marshal.GetLastPInvokeError()));
                    continue;
                }

                if (result == 0)
                {
                    // WM_QUIT is not evidence that an asynchronous WebView creation finished.
                    Guard(() => throw new InvalidOperationException("The native message pump was stopped before shutdown."));
                    continue;
                }

                NativeMethods.TranslateMessage(in message);
                NativeMethods.DispatchMessage(in message);
            }

            // Consume our WM_QUIT so a later Run on the same thread starts with a clean queue.
            NativeMethods.PeekMessage(out _, 0, 0x0012, 0x0012, 1);

            if (_failure is not null)
            {
                ExceptionDispatchInfo.Capture(_failure).Throw();
            }
        }
        finally
        {
            // Normal shutdown releases COM before WM_DESTROY. Do not abandon a pending callback on a timeout.
            NativeMethods.UnregisterClass(className, instance);
        }
    }

    private void StartEnvironment(string profile)
    {
        var callback = new EnvironmentCompleted((error, pointer) => Guard(() => EnvironmentCreated(error, pointer, profile)));
        _pendingCreations++;
        try
        {
            ComReferences.WithCallback(callback, typeof(IEnvironmentCompleted).GUID, pointer =>
            {
                Check(NativeMethods.CreateCoreWebView2EnvironmentWithOptions(_options.BrowserRuntimeFolder, profile, 0, pointer),
                    "Create WebView2 environment. Install the Microsoft Edge WebView2 Evergreen Runtime.");
            });
        }
        catch
        {
            _pendingCreations--;
            throw;
        }
    }

    private void EnvironmentCreated(int error, nint pointer, string leasedProfile)
    {
        _pendingCreations--;
        Check(error, "Create WebView2 environment. Check the runtime installation and browser profile access.");
        _environment = ComReferences.Borrow<IWebViewEnvironment>(pointer);
        if (_closeRequested)
        {
            ScheduleFinish();
            return;
        }

        ConfirmProfile(leasedProfile);

        Check(_environment.GetBrowserVersionString(out var version), "Read browser version");
        try
        {
            BrowserVersion = Marshal.PtrToStringUni(version);
        }
        finally
        {
            Marshal.FreeCoTaskMem(version);
        }

        var callback = new ControllerCompleted((status, controller) => Guard(() => ControllerCreated(status, controller)));
        _pendingCreations++;
        try
        {
            ComReferences.WithCallback(callback, typeof(IControllerCompleted).GUID, handler =>
            {
                Check(_environment.CreateController(_handle, handler), "Create WebView2 controller");
            });
        }
        catch
        {
            _pendingCreations--;
            throw;
        }
    }

    private void ControllerCreated(int error, nint pointer)
    {
        _pendingCreations--;
        Check(error, "Create WebView2 controller");
        _controller = ComReferences.Borrow<IWebViewController>(pointer);
        if (_closeRequested)
        {
            ScheduleFinish();
            return;
        }

        Check(_controller.GetWebView(out var webView), "Get WebView2");
        _webView = ComReferences.Take<IWebView>(webView);
        Resize();
        Check(_controller.SetIsVisible(1), "Show WebView2");
        var callback = new NavigationCompleted(args => Guard(() => NavigationFinished(args)));
        ComReferences.WithCallback(callback, typeof(INavigationCompleted).GUID, handler =>
        {
            Check(_webView.AddNavigationCompleted(handler, out _navigationToken), "Subscribe navigation");
            _subscribed = true;
        });

        fixed (char* html = _options.Html)
        {
            Check(_webView.NavigateToString((nint)html), "Load local HTML");
        }
    }

    private void ConfirmProfile(string leasedProfile)
    {
        if (_environment is not IWebViewEnvironmentProfile profileEnvironment)
        {
            throw new InvalidOperationException("Update the WebView2 Runtime: Spar requires profile-directory confirmation.");
        }

        Check(profileEnvironment.GetUserDataFolder(out var folder), "Read actual WebView2 profile directory");
        try
        {
            var actualProfile = Marshal.PtrToStringUni(folder)
                ?? throw new InvalidOperationException("WebView2 returned no browser profile directory.");
            if (!string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(actualProfile)),
                Path.TrimEndingDirectorySeparator(leasedProfile), StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "WebView2 redirected the browser profile outside Spar's lease. Remove the user-data-folder override.");
            }

            UserDataFolder = actualProfile;
        }
        finally
        {
            Marshal.FreeCoTaskMem(folder);
        }
    }

    private void NavigationFinished(nint pointer)
    {
        var args = ComReferences.Borrow<INavigationCompletedArgs>(pointer);
        try
        {
            if (_closeRequested)
            {
                return;
            }

            Check(args.GetIsSuccess(out var success), "Read navigation status");
            if (success == 0)
            {
                Check(args.GetWebErrorStatus(out var status), "Read navigation error");
                throw new InvalidOperationException($"WebView2 navigation failed (WebErrorStatus {status}).");
            }

            _options.DocumentLoaded?.Invoke(this);
        }
        finally
        {
            ComReferences.Release(args);
        }
    }

    private void Resize()
    {
        if (_controller is not null && !_closeRequested)
        {
            if (!NativeMethods.GetClientRect(_handle, out var bounds))
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }

            Check(_controller.SetBounds(bounds), "Resize WebView2");
        }
    }

    private void RequestClose()
    {
        lock (_posted)
        {
            _closeRequested = true;
            _posted.Clear();
        }

        ScheduleFinish();
    }

    private void ScheduleFinish()
    {
        if (_closeRequested && _pendingCreations == 0 && !_closed)
        {
            // Release controllers and subscriptions outside the WebView2 callback stack.
            if (!NativeMethods.PostMessage(_handle, FinishMessage, 0, 0))
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }
        }
    }

    private void Finish()
    {
        if (_closed || _pendingCreations != 0)
        {
            return;
        }

        if (_subscribed)
        {
            Cleanup(() => Check(_webView!.RemoveNavigationCompleted(_navigationToken), "Unsubscribe navigation"));
            _subscribed = false;
        }

        if (_controller is not null)
        {
            Cleanup(() => Check(_controller.Close(), "Close WebView2 controller"));
        }

        Cleanup(() => ComReferences.Release(_webView));
        Cleanup(() => ComReferences.Release(_controller));
        Cleanup(() => ComReferences.Release(_environment));
        _webView = null;
        _controller = null;
        _environment = null;
        lock (_posted)
        {
            if (!NativeMethods.DestroyWindow(_handle))
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }

            _closed = true;
            _handle = 0;
        }
    }

    private void Cleanup(Action action)
    {
        try
        {
            action();
        }
        catch (Exception error)
        {
            _failure = _failure is null ? error : new AggregateException(_failure, error);
        }
    }

    private void Guard(Action action)
    {
        try
        {
            if (NativeMethods.GetCurrentThreadId() != _threadId)
            {
                throw new InvalidOperationException("WebView2 callback arrived outside the owning UI thread.");
            }

            action();
        }
        catch (Exception error)
        {
            _failure ??= error;
            RequestClose();
        }
    }

    private static void Check(int result, string operation)
    {
        if (result < 0)
        {
            throw new InvalidOperationException($"{operation} (HRESULT 0x{result:X8}).", Marshal.GetExceptionForHR(result));
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static nint WindowProcedure(nint window, uint message, nuint wParam, nint lParam)
    {
        var current = _current;
        if (current is null || (current._handle != 0 && current._handle != window))
        {
            return NativeMethods.DefWindowProc(window, message, wParam, lParam);
        }

        switch (message)
        {
            case CloseMessage:
                current.Guard(current.RequestClose);
                return 0;
            case SizeMessage:
                current.Guard(current.Resize);
                break;
            case DispatchMessage:
                if (!current._closeRequested && current._posted.TryDequeue(out var action))
                {
                    current.Guard(action);
                }

                return 0;
            case FinishMessage:
                current.Guard(current.Finish);
                return 0;
            case DestroyMessage:
                NativeMethods.PostQuitMessage(0);
                return 0;
        }

        return NativeMethods.DefWindowProc(window, message, wParam, lParam);
    }
}
