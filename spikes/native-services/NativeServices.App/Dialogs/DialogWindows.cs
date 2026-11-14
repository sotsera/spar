#if PART_DIALOGS || PART_CSWIN32_DIALOGS
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace NativeServices.App.Dialogs;

/// <summary>What the automation does once the dialog window exists.</summary>
internal enum DialogAction
{
    None,
    Accept,
    Cancel,
    CloseWithHResult,
}

/// <summary>Window facts seen by the automation. The watchdog thread writes its fields before it is joined.</summary>
internal sealed class DialogObservation
{
    public nint Window { get; set; }
    public uint WindowThread { get; set; }
    public string Title { get; set; } = "";
    public string ClassName { get; set; } = "";
    public bool OwnerMatches { get; set; }
    public bool VisibleWhenFound { get; set; }
    public RECT InitialRect { get; set; }
    public double FoundAfterMs { get; set; }
    public bool WatchdogFired { get; set; }
    public List<string> Events { get; } = [];
    public List<string> SeenOnScreen { get; } = []; // windows that were visible inside the desktop before being moved

    public void AddEvent(string name)
    {
        lock (Events)
        {
            Events.Add(name);
        }
    }

    public override string ToString() =>
        $"class={ClassName} title=\"{Title}\" ownerMatches={OwnerMatches} visibleWhenFound={VisibleWhenFound} " +
        $"rect=({InitialRect.left},{InitialRect.top})-({InitialRect.right},{InitialRect.bottom}) " +
        $"foundAfter={FoundAfterMs:F1}ms watchdog={WatchdogFired} events=[{string.Join(",", Events)}] " +
        $"seenOnScreen=[{string.Join(",", SeenOnScreen)}]";
}

/// <summary>Win32 helpers for the dialog checks: an STA thread with an owner window, observation and dismissal.</summary>
internal static unsafe class DialogWindows
{
    public const int IdOk = 1;     // WinUser.h
    public const int IdCancel = 2; // WinUser.h
    public const int IdNo = 7;     // WinUser.h
    public const string DialogClass = "#32770";
    public const int Offscreen = -32000;
    public const uint TdmClickButton = 0x0400 + 102; // WM_USER + 102, CommCtrl.h

    [ThreadStatic]
    private static List<nint>? t_collected;

    private static int s_hiddenAtCreation;

    /// <summary>Test-only: a thread-local WH_CBT hook makes every dialog transparent as it is created.</summary>
    public static bool HideNewDialogs { get; set; } = Environment.GetEnvironmentVariable("SPIKE_NO_HIDE_HOOK") != "1";

    public static int HiddenAtCreation => Volatile.Read(ref s_hiddenAtCreation);

    /// <summary>Runs <paramref name="body"/> on a new STA thread that owns an unshown owner window.</summary>
    public static T RunOnSta<T>(Func<nint, uint, T> body)
    {
        T result = default!;
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var owner = CreateOwner();
            var hook = HideNewDialogs ? PInvoke.SetWindowsHookEx(WINDOWS_HOOK_ID.WH_CBT, &HideOnCreate, HINSTANCE.Null,
                PInvoke.GetCurrentThreadId()) : HHOOK.Null;
            try
            {
                result = body(owner, PInvoke.GetCurrentThreadId());
            }
            catch (Exception e)
            {
                failure = e;
            }
            finally
            {
                if (!hook.IsNull)
                {
                    PInvoke.UnhookWindowsHookEx(hook);
                }

                PInvoke.DestroyWindow((HWND)owner);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
        {
            throw new InvalidOperationException("STA body failed", failure);
        }

        return result;
    }

    /// <summary>Records the dialog window and moves it offscreen without activating it.</summary>
    public static void Observe(nint window, nint owner, DialogObservation observation, long startTimestamp)
    {
        var hwnd = (HWND)window;
        observation.Window = window;
        observation.WindowThread = ThreadOf(window);
        observation.FoundAfterMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;
        observation.Title = WindowText(hwnd);
        observation.ClassName = ClassName(hwnd);
        observation.OwnerMatches = PInvoke.GetWindow(hwnd, GET_WINDOW_CMD.GW_OWNER) == (HWND)owner;
        observation.VisibleWhenFound = PInvoke.IsWindowVisible(hwnd);
        PInvoke.GetWindowRect(hwnd, out var rect);
        observation.InitialRect = rect;
        Hide(window);
    }

    /// <summary>Test-only: makes a not-yet-shown dialog fully transparent and moves it offscreen.</summary>
    /// <remarks>The dialog repositions itself onto the desktop when it is shown, so moving alone is not enough.</remarks>
    public static void Hide(nint window)
    {
        var hwnd = (HWND)window;
        var exStyle = PInvoke.GetWindowLongPtr(hwnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);
        PInvoke.SetWindowLongPtr(hwnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE, exStyle | (nint)WINDOW_EX_STYLE.WS_EX_LAYERED);
        PInvoke.SetLayeredWindowAttributes(hwnd, default, 0, LAYERED_WINDOW_ATTRIBUTES_FLAGS.LWA_ALPHA);
        MoveOffscreen(window);
    }

    public static void MoveOffscreen(nint window) =>
        PInvoke.SetWindowPos((HWND)window, HWND.Null, Offscreen, Offscreen, 0, 0,
            SET_WINDOW_POS_FLAGS.SWP_NOSIZE | SET_WINDOW_POS_FLAGS.SWP_NOZORDER | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE);

    public static void PostCommand(nint window, int command) =>
        PInvoke.PostMessage((HWND)window, PInvoke.WM_COMMAND, (nuint)command, 0);

    public static nint FindDialog(string title) => PInvoke.FindWindow(DialogClass, title);

    public static uint ThreadOf(nint window) => PInvoke.GetWindowThreadProcessId((HWND)window, null);

    /// <summary>Dialog-class top-level windows of a thread (the file dialog and any message box it raised).</summary>
    public static List<nint> ThreadDialogs(uint threadId)
    {
        var collected = t_collected ??= [];
        collected.Clear();
        PInvoke.EnumThreadWindows(threadId, &Collect, 0);
        return [.. collected];
    }

    /// <summary>Moves any dialog of the thread that is visible on the desktop offscreen, recording its title.</summary>
    public static void Quarantine(uint threadId, DialogObservation observation)
    {
        foreach (var window in ThreadDialogs(threadId))
        {
            var hwnd = (HWND)window;
            var layered = (PInvoke.GetWindowLongPtr(hwnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE) &
                (nint)WINDOW_EX_STYLE.WS_EX_LAYERED) != 0; // only windows hidden by Hide are layered here
            if (!layered && PInvoke.IsWindowVisible(hwnd) && PInvoke.GetWindowRect(hwnd, out var rect) &&
                rect.left > Offscreen / 2)
            {
                observation.SeenOnScreen.Add($"{WindowText(hwnd)}@({rect.left},{rect.top})");
                Hide(window);
            }
        }
    }

    /// <summary>
    /// Dismisses every dialog of the thread: IDCANCEL for the file dialog and OK-only message boxes, and IDNO as well
    /// for secondary Yes/No confirmations (the overwrite prompt has no Cancel button and ignores IDCANCEL).
    /// </summary>
    public static void DismissAll(uint threadId, string mainTitle, DialogObservation observation)
    {
        foreach (var window in ThreadDialogs(threadId))
        {
            var text = WindowText((HWND)window);
            observation.AddEvent($"dismiss:\"{text}\"");
            if (text != mainTitle || window != observation.Window)
            {
                // A task dialog (the overwrite prompt) ignores WM_COMMAND; TDM_CLICK_BUTTON presses its No button.
                PInvoke.PostMessage((HWND)window, TdmClickButton, (nuint)IdNo, 0);
                PostCommand(window, IdNo);
            }

            PostCommand(window, IdCancel);
        }
    }

    private static nint CreateOwner()
    {
        // A built-in class needs no window procedure. The owner is never shown.
        var owner = PInvoke.CreateWindowEx(0, "STATIC", "Spar native-services spike owner",
            WINDOW_STYLE.WS_OVERLAPPED | WINDOW_STYLE.WS_CAPTION, Offscreen, Offscreen, 400, 300,
            HWND.Null, null, null, null);
        if (owner.IsNull)
        {
            throw new InvalidOperationException($"CreateWindowEx failed: {Marshal.GetLastPInvokeError()}");
        }

        return owner;
    }

    private static string WindowText(HWND hwnd)
    {
        Span<char> buffer = stackalloc char[256];
        var length = PInvoke.GetWindowText(hwnd, buffer);
        return new string(buffer[..length]);
    }

    private static string ClassName(HWND hwnd)
    {
        Span<char> buffer = stackalloc char[256];
        var length = PInvoke.GetClassName(hwnd, buffer);
        return new string(buffer[..length]);
    }

    // WH_CBT on the dialog thread only (no DLL, no other process). HCBT_CREATEWND runs before the window is shown.
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static LRESULT HideOnCreate(int code, WPARAM wParam, LPARAM lParam)
    {
        try
        {
            // Message boxes and task dialogs reset their extended style while initialising, so hide again on activation.
            var hwnd = (HWND)(nint)wParam.Value;
            var creatingOrActivating = code == (int)PInvoke.HCBT_CREATEWND || code == (int)PInvoke.HCBT_ACTIVATE;
            if (creatingOrActivating && ClassName(hwnd) == DialogClass &&
                (PInvoke.GetWindowLongPtr(hwnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE) & (nint)WINDOW_EX_STYLE.WS_EX_LAYERED) == 0)
            {
                Hide(hwnd);
                Interlocked.Increment(ref s_hiddenAtCreation);
            }
        }
        catch
        {
            // Never let an exception cross the native callback.
        }

        return PInvoke.CallNextHookEx(HHOOK.Null, code, wParam, lParam);
    }

    // Runs synchronously on the enumerating thread, so the thread-static list is safe.
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static BOOL Collect(HWND hwnd, LPARAM unused)
    {
        try
        {
            if (ClassName(hwnd) == DialogClass)
            {
                t_collected?.Add(hwnd);
            }
        }
        catch
        {
            // Never let an exception cross the native callback.
        }

        return true;
    }
}

/// <summary>
/// A background thread that, every 5 ms: moves any on-screen dialog of the dialog thread offscreen (recording it);
/// optionally finds the dialog by its unique title and applies an action (for callers without an event sink); and
/// dismisses everything once the deadline passes, repeating every second until stopped.
/// </summary>
internal sealed class DialogWatchdog : IDisposable
{
    private readonly ManualResetEventSlim _stop = new();
    private readonly Thread _thread;

    public DialogWatchdog(string title, nint owner, uint threadId, DialogAction action, TimeSpan deadline,
        DialogObservation observation, long startTimestamp)
    {
        _thread = new Thread(() => Watch(title, owner, threadId, action, deadline, observation, startTimestamp))
        {
            IsBackground = true,
            Name = "dialog watchdog",
        };
        _thread.Start();
    }

    public void Dispose()
    {
        _stop.Set();
        _thread.Join();
        _stop.Dispose();
    }

    private void Watch(string title, nint owner, uint threadId, DialogAction action, TimeSpan deadline,
        DialogObservation observation, long startTimestamp)
    {
        var acted = action == DialogAction.None;
        var limit = Stopwatch.GetTimestamp() + (long)(deadline.TotalSeconds * Stopwatch.Frequency);
        while (!_stop.Wait(5))
        {
            if (threadId == 0)
            {
                var byTitle = DialogWindows.FindDialog(title);
                threadId = byTitle == 0 ? 0 : DialogWindows.ThreadOf(byTitle); // the COM host STA for an MTA caller
            }

            if (!acted)
            {
                var window = DialogWindows.FindDialog(title);
                if (window != 0 && observation.Window == 0)
                {
                    DialogWindows.Observe(window, owner, observation, startTimestamp); // hides it before it is shown
                }

                // Commands posted before the dialog finishes initialising (about 30 ms after creation, before it is
                // shown) are ignored, so wait until it is visible.
                if (window != 0 && PInvoke.IsWindowVisible((HWND)window))
                {
                    DialogWindows.PostCommand(window,
                        action == DialogAction.Accept ? DialogWindows.IdOk : DialogWindows.IdCancel);
                    acted = true;
                }
            }

            if (threadId != 0)
            {
                DialogWindows.Quarantine(threadId, observation);
            }

            if (Stopwatch.GetTimestamp() > limit)
            {
                observation.WatchdogFired = true;
                if (threadId != 0)
                {
                    DialogWindows.DismissAll(threadId, title, observation);
                }

                limit = Stopwatch.GetTimestamp() + Stopwatch.Frequency;
            }
        }
    }
}
#endif
