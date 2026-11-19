using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Spar.Testing.BlackBox.Interop;
using Windows.Win32;
using Windows.Win32.System.Com;

namespace Spar.Testing;

/// <summary>
/// The fixture's UI Automation client: one MTA thread of its own, placed on the desktop the apps run on
/// (<c>SetThreadDesktop</c>, before it makes any window), with one <c>CUIAutomation8</c> object. Every call runs there,
/// one at a time; the elements a call finds are released when it returns, so nothing of the app's tree is kept between
/// calls and every query reads the app as it is now.
/// </summary>
[SupportedOSPlatform("windows10.0.17763")]
internal sealed class AutomationThread : IDisposable
{
    private static readonly Guid AutomationClass = new("e22ad333-b25f-460c-83d0-0581107395c9");
    private static readonly Guid AutomationInterface = new("30cbe57d-d9d0-452a-ab13-7ac5ac4825ee");

    private readonly BlockingCollection<Action> _work = [];
    private readonly Thread _thread;
    private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Uia? _uia;

    private AutomationThread(HiddenDesktop? desktop)
    {
        _thread = new Thread(() => Run(desktop)) { IsBackground = true, Name = "Spar UI Automation" };
        _thread.SetApartmentState(ApartmentState.MTA);
        _thread.Start();
    }

    /// <summary>Starts the thread on <paramref name="desktop"/>, or on the test process's own desktop when null.</summary>
    public static AutomationThread Start(HiddenDesktop? desktop)
    {
        var thread = new AutomationThread(desktop);
        thread._started.Task.GetAwaiter().GetResult();
        return thread;
    }

    /// <summary>Runs <paramref name="work"/> on the thread; the elements it found are released afterwards.</summary>
    public Task<T> RunAsync<T>(Func<Uia, T> work)
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
                _uia!.ReleaseFound();
            }
        });
        return result.Task;
    }

    public void Dispose() => _work.CompleteAdding();

    private unsafe void Run(HiddenDesktop? desktop)
    {
        try
        {
            if (desktop is not null && !PInvoke.SetThreadDesktop(desktop.Handle))
            {
                throw new InvalidOperationException($"SetThreadDesktop failed ({Marshal.GetLastPInvokeError()}).");
            }

            PInvoke.CoInitializeEx(null, COINIT.COINIT_MULTITHREADED).ThrowOnFailure();
            var clsid = AutomationClass;
            var iid = AutomationInterface;
            void* pointer;
            PInvoke.CoCreateInstance(&clsid, null, CLSCTX.CLSCTX_INPROC_SERVER, &iid, &pointer).ThrowOnFailure();
            _uia = new Uia(Uia.Take<IUIAutomation>((nint)pointer));
            _started.TrySetResult();
        }
        catch (Exception exception)
        {
            _started.TrySetException(exception);
            return;
        }

        foreach (var work in _work.GetConsumingEnumerable())
        {
            work();
        }
    }
}
