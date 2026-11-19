using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Spar.Testing;
using Spar.Testing.BlackBox.Interop;
using Windows.Win32;
using Windows.Win32.System.Com;

namespace InvokeDriver;

/// <summary>
/// A UI Automation client thread on the hidden desktop, like the fixture's AutomationThread, with the spike's knobs: MTA
/// without a message pump, or STA pumping window messages between and during calls; IUIAutomation2's AutoSetFocus and
/// timeouts set right after creation. IUIAutomation2 and LegacyIAccessible are called through their vtable slots
/// (UIAutomationClient.h, 10.0.26100.0): IUIAutomation2 slots 59 put_AutoSetFocus, 61 put_ConnectionTimeout, 63
/// put_TransactionTimeout; IUIAutomationLegacyIAccessiblePattern slot 4 DoDefaultAction.
/// </summary>
[SupportedOSPlatform("windows10.0.19045")]
internal sealed unsafe partial class UiaThread : IDisposable
{
    private static readonly Guid AutomationClass = new("e22ad333-b25f-460c-83d0-0581107395c9");
    private static readonly Guid AutomationInterface = new("30cbe57d-d9d0-452a-ab13-7ac5ac4825ee");
    private static readonly Guid Automation2Interface = new("34723aff-0c9d-49d0-9896-7ab52df8cd8a");

    private readonly BlockingCollection<Action> _work = [];
    private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly AutoResetEvent _wake = new(false);
    private SparkUia? _uia;

    private UiaThread(HiddenDesktop desktop, Variant variant)
    {
        var thread = new Thread(() => Run(desktop, variant)) { IsBackground = true, Name = "spike UIA" };
        // An STA thread gets its apartment from CoInitializeEx below, after SetThreadDesktop: a thread the runtime made STA
        // already has COM's hidden window on the old desktop, and SetThreadDesktop then fails with ERROR_BUSY (170).
        if (!variant.Sta)
        {
            thread.SetApartmentState(ApartmentState.MTA);
        }

        thread.Start();
    }

    public static UiaThread Start(HiddenDesktop desktop, Variant variant)
    {
        var thread = new UiaThread(desktop, variant);
        thread._started.Task.GetAwaiter().GetResult();
        return thread;
    }

    public Task<T> RunAsync<T>(Func<SparkUia, T> work)
    {
        var result = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _work.Add(() =>
        {
            try
            {
                result.TrySetResult(work(_uia!));
            }
            catch (Exception exception)
            {
                result.TrySetException(exception);
            }
            finally
            {
                _uia!.Uia.ReleaseFound();
            }
        });
        _wake.Set();
        return result.Task;
    }

    public void Dispose()
    {
        _work.CompleteAdding();
        _wake.Set();
    }

    private void Run(HiddenDesktop desktop, Variant variant)
    {
        try
        {
            if (variant.Sta)
            {
                // The runtime made this thread MTA when it started (no window); undo that, so it can be STA on the hidden desktop.
                CoUninitialize();
            }

            if (!PInvoke.SetThreadDesktop(desktop.Handle))
            {
                throw new InvalidOperationException($"SetThreadDesktop failed ({Marshal.GetLastPInvokeError()}).");
            }

            PInvoke.CoInitializeEx(null, variant.Sta ? COINIT.COINIT_APARTMENTTHREADED : COINIT.COINIT_MULTITHREADED).ThrowOnFailure();
            var clsid = AutomationClass;
            var iid = AutomationInterface;
            void* pointer;
            PInvoke.CoCreateInstance(&clsid, null, CLSCTX.CLSCTX_INPROC_SERVER, &iid, &pointer).ThrowOnFailure();
            Configure((nint)pointer, variant);
            _uia = new SparkUia(new Uia(Uia.Take<IUIAutomation>((nint)pointer)));
            _started.TrySetResult();
        }
        catch (Exception exception)
        {
            _started.TrySetException(exception);
            return;
        }

        if (!variant.Sta)
        {
            foreach (var work in _work.GetConsumingEnumerable())
            {
                work();
            }

            return;
        }

        // STA: run queued work, and pump this thread's window messages whenever there is none.
        var wake = _wake.SafeWaitHandle.DangerousGetHandle();
        while (!_work.IsCompleted)
        {
            while (_work.TryTake(out var work))
            {
                work();
            }

            _ = MsgWaitForMultipleObjectsEx(1, &wake, 50, 0x04FF /* QS_ALLINPUT */, 0x0004 /* MWMO_INPUTAVAILABLE */);
            Msg message;
            while (PeekMessageW(&message, 0, 0, 0, 1 /* PM_REMOVE */) != 0)
            {
                _ = TranslateMessage(&message);
                _ = DispatchMessageW(&message);
            }
        }
    }

    private static void Configure(nint automation, Variant variant)
    {
        if (variant.AutoSetFocus is null && variant.ConnectionTimeout is null && variant.TransactionTimeout is null)
        {
            return;
        }

        var iid = Automation2Interface;
        Marshal.ThrowExceptionForHR(Marshal.QueryInterface(automation, in iid, out var second));
        try
        {
            var table = *(nint**)second;
            if (variant.AutoSetFocus is { } focus)
            {
                Marshal.ThrowExceptionForHR(((delegate* unmanaged[Stdcall]<nint, int, int>)table[59])(second, focus ? 1 : 0));
            }

            if (variant.ConnectionTimeout is { } connection)
            {
                Marshal.ThrowExceptionForHR(((delegate* unmanaged[Stdcall]<nint, uint, int>)table[61])(second, connection));
            }

            if (variant.TransactionTimeout is { } transaction)
            {
                Marshal.ThrowExceptionForHR(((delegate* unmanaged[Stdcall]<nint, uint, int>)table[63])(second, transaction));
            }

            int current;
            Marshal.ThrowExceptionForHR(((delegate* unmanaged[Stdcall]<nint, int*, int>)table[58])(second, &current));
            uint connectionNow, transactionNow;
            Marshal.ThrowExceptionForHR(((delegate* unmanaged[Stdcall]<nint, uint*, int>)table[60])(second, &connectionNow));
            Marshal.ThrowExceptionForHR(((delegate* unmanaged[Stdcall]<nint, uint*, int>)table[62])(second, &transactionNow));
            Console.Error.WriteLine($"[{variant.Name}] AutoSetFocus={current} ConnectionTimeout={connectionNow} TransactionTimeout={transactionNow}");
        }
        finally
        {
            Marshal.Release(second);
        }
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

    [LibraryImport("ole32")]
    private static partial void CoUninitialize();

    [LibraryImport("user32")]
    private static partial uint MsgWaitForMultipleObjectsEx(uint count, nint* handles, uint milliseconds, uint wakeMask, uint flags);

    [LibraryImport("user32")]
    private static partial int PeekMessageW(Msg* message, nint window, uint first, uint last, uint remove);

    [LibraryImport("user32")]
    private static partial int TranslateMessage(Msg* message);

    [LibraryImport("user32")]
    private static partial nint DispatchMessageW(Msg* message);
}

/// <summary>The spike's operations over the fixture's <see cref="Uia"/>.</summary>
[SupportedOSPlatform("windows10.0.19045")]
internal sealed unsafe class SparkUia(Uia uia)
{
    private const int LegacyIAccessiblePattern = 10018;

    public Uia Uia { get; } = uia;

    public UiaElement? Window(int processId) =>
        TopWindows.Of(processId).Where(w => !w.IsDialog && w.Owner == 0).Select(w => Uia.FromHandle(w.Handle)).FirstOrDefault(e => e?.ControlType == UiaElement.WindowType);

    public UiaElement? Button(int processId, string name) =>
        Window(processId) is { } window
            ? Uia.FindFirst(window, Uia.ScopeDescendants, Uia.And(Uia.Equal(Uia.ControlTypeProperty, UiaElement.ButtonType), Uia.Equal(Uia.NameProperty, name)))
            : null;

    public bool Act(int processId, string name, string action)
    {
        var button = Button(processId, name) ?? throw new InvalidOperationException($"no button '{name}'");
        switch (action)
        {
            case "invoke":
                Uia.Pattern<IUIAutomationInvokePattern>(button, Uia.InvokePattern)!.Invoke();
                break;
            case "setfocus":
                button.Com.SetFocus();
                break;
            case "foreground":
                Foreground(processId);
                break;
            case "legacy":
                var pattern = button.Com.GetCurrentPattern(LegacyIAccessiblePattern);
                if (pattern == 0)
                {
                    throw new InvalidOperationException("no LegacyIAccessible pattern");
                }

                try
                {
                    var table = *(nint**)pattern;
                    Marshal.ThrowExceptionForHR(((delegate* unmanaged[Stdcall]<nint, int>)table[4])(pattern));
                }
                finally
                {
                    Marshal.Release(pattern);
                }

                break;
            default:
                throw new ArgumentException(action);
        }

        return true;
    }

    /// <summary>SetForegroundWindow on the app's window from this thread on the hidden desktop, and what GetForegroundWindow says for 2.5 s.</summary>
    private static void Foreground(int processId)
    {
        var window = TopWindows.Of(processId).First(w => !w.IsDialog && w.Owner == 0).Handle;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var before = (nint)PInvoke.GetForegroundWindow().Value;
        var done = PInvoke.SetForegroundWindow((Windows.Win32.Foundation.HWND)window);
        var log = new System.Text.StringBuilder(FormattableString.Invariant($"[foreground] window {window:x}; before {before:x}; SetForegroundWindow {(bool)done} in {clock.Elapsed.TotalMilliseconds:0.0} ms"));
        var last = (nint)(-1);
        while (clock.ElapsedMilliseconds < 2500)
        {
            var now = (nint)PInvoke.GetForegroundWindow().Value;
            if (now != last)
            {
                log.Append(FormattableString.Invariant($"; at {clock.Elapsed.TotalMilliseconds:0.0} ms {now:x}"));
                last = now;
            }

            Thread.Sleep(5);
        }

        Console.Error.WriteLine(log);
    }

    public bool Close(int processId)
    {
        var window = Window(processId) ?? throw new InvalidOperationException("no window");
        Uia.Pattern<IUIAutomationWindowPattern>(window, Uia.WindowPattern)!.Close();
        return true;
    }
}

/// <summary>Samples the input desktop's foreground window every 10 ms: hits are samples owned by one of the app's processes.</summary>
[SupportedOSPlatform("windows10.0.19045")]
internal sealed unsafe class ForegroundWatch : IDisposable
{
    private readonly CancellationTokenSource _stop = new();
    private readonly Thread _thread;
    private int _hits;

    public ForegroundWatch(AppProcess process)
    {
        _thread = new Thread(() =>
        {
            while (!_stop.IsCancellationRequested)
            {
                uint owner;
                PInvoke.GetWindowThreadProcessId(PInvoke.GetForegroundWindow(), &owner);
                if (process.ProcessIds.Contains((int)owner))
                {
                    Interlocked.Increment(ref _hits);
                }

                Thread.Sleep(10);
            }
        })
        { IsBackground = true };
        _thread.Start();
    }

    public int Hits => Volatile.Read(ref _hits);

    public void Dispose()
    {
        _stop.Cancel();
        _thread.Join();
    }
}

/// <summary>The "settings" verb: two CUIAutomation8 objects on one MTA thread, settings written on the first, read on both.</summary>
[SupportedOSPlatform("windows10.0.19045")]
internal static unsafe partial class SettingsProbe
{
    private static readonly Guid AutomationClass = new("e22ad333-b25f-460c-83d0-0581107395c9");
    private static readonly Guid Automation2Interface = new("34723aff-0c9d-49d0-9896-7ab52df8cd8a");

    public static void Run()
    {
        PInvoke.CoInitializeEx(null, COINIT.COINIT_MULTITHREADED).ThrowOnFailure();
        var first = Create();
        var second = Create();
        Console.WriteLine($"before: first {Read(first)}; second {Read(second)}");
        var table = *(nint**)first;
        Marshal.ThrowExceptionForHR(((delegate* unmanaged[Stdcall]<nint, int, int>)table[59])(first, 0));
        Marshal.ThrowExceptionForHR(((delegate* unmanaged[Stdcall]<nint, uint, int>)table[61])(first, 300));
        Marshal.ThrowExceptionForHR(((delegate* unmanaged[Stdcall]<nint, uint, int>)table[63])(first, 3000));
        Console.WriteLine($"after setting the first: first {Read(first)}; second {Read(second)}");
        var third = Create();
        Console.WriteLine($"a third, created afterwards: {Read(third)}");
        Marshal.Release(first);
        Marshal.Release(second);
        Marshal.Release(third);
    }

    private static nint Create()
    {
        var clsid = AutomationClass;
        var iid = Automation2Interface;
        void* pointer;
        PInvoke.CoCreateInstance(&clsid, null, CLSCTX.CLSCTX_INPROC_SERVER, &iid, &pointer).ThrowOnFailure();
        return (nint)pointer;
    }

    private static string Read(nint automation)
    {
        var table = *(nint**)automation;
        int focus;
        uint connection, transaction;
        Marshal.ThrowExceptionForHR(((delegate* unmanaged[Stdcall]<nint, int*, int>)table[58])(automation, &focus));
        Marshal.ThrowExceptionForHR(((delegate* unmanaged[Stdcall]<nint, uint*, int>)table[60])(automation, &connection));
        Marshal.ThrowExceptionForHR(((delegate* unmanaged[Stdcall]<nint, uint*, int>)table[62])(automation, &transaction));
        return $"AutoSetFocus={focus} ConnectionTimeout={connection} TransactionTimeout={transaction}";
    }
}
