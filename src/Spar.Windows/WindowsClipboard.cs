using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Memory;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Spar.Windows;

/// <summary>
/// The backend's clipboard on Windows (decision "M3c's decide-first questions", item 5): a thread of its own with a
/// message-only window, never the window's UI thread, because another program that holds the clipboard, or renders
/// it late, can keep a read waiting. The thread pumps messages, so programs that message the clipboard's owner are
/// answered. <c>OpenClipboard</c> is tried three times over about 100 ms, then the call is <see cref="ClipboardError.Busy"/>.
/// One thread serves the process, started at the first use.
/// </summary>
internal sealed unsafe class WindowsClipboard : IClipboard
{
    private const uint WorkMessage = PInvoke.WM_APP + 1;
    private const uint UnicodeText = 13;
    private const int Attempts = 3;
    private const int AttemptMilliseconds = 33;
    private const string ClassName = "Spar.Clipboard";

    private static readonly Lazy<ClipboardThread> Thread = new(() => new ClipboardThread(), LazyThreadSafetyMode.ExecutionAndPublication);

    public Task<Result<ClipboardError>> SetTextAsync(string text, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (Encoding.UTF8.GetByteCount(text) > IClipboard.MaxTextBytes)
        {
            return Task.FromResult<Result<ClipboardError>>(new ClipboardError.TooLarge(IClipboard.MaxTextBytes));
        }

        return Thread.Value.Run(window => Set(window, text), cancellationToken);
    }

    public Task<Result<string, ClipboardError>> GetTextAsync(CancellationToken cancellationToken) =>
        Thread.Value.Run(Get, cancellationToken);

    private static Result<ClipboardError> Set(HWND window, string text)
    {
        if (Open(window) is { } refused)
        {
            return refused;
        }

        try
        {
            if (!PInvoke.EmptyClipboard())
            {
                return Unavailable("EmptyClipboard");
            }

            var bytes = (nuint)((text.Length + 1) * sizeof(char));
            var memory = PInvoke.GlobalAlloc(GLOBAL_ALLOC_FLAGS.GMEM_MOVEABLE, bytes);
            if (memory.IsNull)
            {
                return Unavailable("GlobalAlloc");
            }

            var target = (char*)PInvoke.GlobalLock(memory);
            text.AsSpan().CopyTo(new Span<char>(target, text.Length));
            target[text.Length] = '\0';
            PInvoke.GlobalUnlock(memory);

            // The clipboard owns the memory once SetClipboardData succeeds; it is freed here only when it fails.
            if (PInvoke.SetClipboardData(UnicodeText, new HANDLE(memory.Value)).IsNull)
            {
                PInvoke.GlobalFree(memory);
                return Unavailable("SetClipboardData");
            }

            return Result.Success;
        }
        finally
        {
            PInvoke.CloseClipboard();
        }
    }

    private static Result<string, ClipboardError> Get(HWND window)
    {
        if (Open(window) is { } refused)
        {
            return refused;
        }

        try
        {
            var data = PInvoke.GetClipboardData(UnicodeText);
            if (data.IsNull)
            {
                return "";
            }

            var memory = new HGLOBAL(data.Value);
            var text = (char*)PInvoke.GlobalLock(memory);
            if (text is null)
            {
                return "";
            }

            try
            {
                return new string(text);
            }
            finally
            {
                PInvoke.GlobalUnlock(memory);
            }
        }
        finally
        {
            PInvoke.CloseClipboard();
        }
    }

    /// <summary>Opens the clipboard for this thread's window; null when it opened, <see cref="ClipboardError.Busy"/> when another program kept it.</summary>
    private static ClipboardError? Open(HWND window)
    {
        for (var attempt = 0; attempt < Attempts; attempt++)
        {
            if (PInvoke.OpenClipboard(window))
            {
                return null;
            }

            System.Threading.Thread.Sleep(AttemptMilliseconds);
        }

        return new ClipboardError.Busy();
    }

    private static ClipboardError.Unavailable Unavailable(string call) =>
        new($"{call} failed with error {Marshal.GetLastPInvokeError()}.");

    /// <summary>The thread: its message-only window, its message loop, and the work posted to it.</summary>
    private sealed class ClipboardThread
    {
        private readonly ConcurrentQueue<Action<HWND>> _work = new();
        private readonly ManualResetEventSlim _ready = new();
        private uint _threadId;
        private HWND _window;

        public ClipboardThread()
        {
            var thread = new System.Threading.Thread(Loop) { Name = "Spar clipboard", IsBackground = true };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            _ready.Wait();
        }

        public Task<T> Run<T>(Func<HWND, T> work, CancellationToken cancellationToken)
        {
            if (_window.IsNull)
            {
                throw new InvalidOperationException("The clipboard's window could not be created.");
            }

            var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            _work.Enqueue(window =>
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    done.TrySetCanceled(cancellationToken);
                    return;
                }

                try
                {
                    done.TrySetResult(work(window));
                }
                catch (Exception exception)
                {
                    done.TrySetException(exception);
                }
            });
            PInvoke.PostThreadMessage(_threadId, WorkMessage, default, default);
            return done.Task;
        }

        private void Loop()
        {
            _threadId = PInvoke.GetCurrentThreadId();
            try
            {
                _window = CreateWindow();

                // The thread's message queue exists from its first message call, so a post before it is never lost.
                PInvoke.PeekMessage(out _, HWND.Null, 0, 0, PEEK_MESSAGE_REMOVE_TYPE.PM_NOREMOVE);
            }
            finally
            {
                _ready.Set();
            }

            while (PInvoke.GetMessage(out var message, HWND.Null, 0, 0) > 0)
            {
                if (message.hwnd.IsNull && message.message == WorkMessage)
                {
                    while (_work.TryDequeue(out var work))
                    {
                        work(_window);
                    }

                    continue;
                }

                PInvoke.TranslateMessage(message);
                PInvoke.DispatchMessage(message);
            }
        }

        private static HWND CreateWindow()
        {
            var instance = PInvoke.GetModuleHandle((char*)null);
            fixed (char* className = ClassName)
            {
                var windowClass = new WNDCLASSEXW
                {
                    cbSize = (uint)sizeof(WNDCLASSEXW),
                    lpfnWndProc = &WindowProcedure,
                    hInstance = new HINSTANCE(instance.Value),
                    lpszClassName = className,
                };
                PInvoke.RegisterClassEx(windowClass);

                // HWND_MESSAGE: a window that only receives messages, never shown.
                return PInvoke.CreateWindowEx(0, className, null, 0, 0, 0, 0, 0, new HWND(-3), HMENU.Null, new HINSTANCE(instance.Value), null);
            }
        }

        [UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvStdcall)])]
        private static LRESULT WindowProcedure(HWND window, uint message, WPARAM wParam, LPARAM lParam) =>
            PInvoke.DefWindowProc(window, message, wParam, lParam);
    }
}
