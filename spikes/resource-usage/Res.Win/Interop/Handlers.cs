using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Spar.ResourceUsage.Interop;

// Completion and event handlers implemented by the host. Shapes follow spikes/webview-profiles.

[GeneratedComClass]
internal sealed partial class EnvironmentCompletedHandler : ICoreWebView2CreateCoreWebView2EnvironmentCompletedHandler
{
    private static readonly StrategyBasedComWrappers s_wrappers = new();

    private readonly TaskCompletionSource<object> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task<object> Task => _completion.Task;

    public void Invoke(int errorCode, nint createdEnvironment)
    {
        if (errorCode < 0 || createdEnvironment == 0)
        {
            _completion.TrySetException(new ComFailureException("CreateCoreWebView2Environment", errorCode));
            return;
        }

        // Borrowed pointer: the wrapper takes its own reference.
        _completion.TrySetResult(s_wrappers.GetOrCreateObjectForComInstance(createdEnvironment, CreateObjectFlags.UniqueInstance));
    }
}

[GeneratedComClass]
internal sealed partial class ControllerCompletedHandler : ICoreWebView2CreateCoreWebView2ControllerCompletedHandler
{
    private readonly TaskCompletionSource<ICoreWebView2Controller> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task<ICoreWebView2Controller> Task => _completion.Task;

    public void Invoke(int errorCode, ICoreWebView2Controller? createdController)
    {
        if (errorCode < 0 || createdController is null)
        {
            _completion.TrySetException(new ComFailureException("CreateCoreWebView2Controller", errorCode));
            return;
        }

        _completion.TrySetResult(createdController);
    }
}

[GeneratedComClass]
internal sealed partial class WebResourceRequestedHandler(Action<ICoreWebView2WebResourceRequestedEventArgs> onRequested)
    : ICoreWebView2WebResourceRequestedEventHandler
{
    public void Invoke(nint sender, ICoreWebView2WebResourceRequestedEventArgs args) => onRequested(args);
}

[GeneratedComClass]
internal sealed partial class WebMessageReceivedHandler(Action<string> onMessage) : ICoreWebView2WebMessageReceivedEventHandler
{
    public void Invoke(nint sender, ICoreWebView2WebMessageReceivedEventArgs args) => onMessage(args.TryGetWebMessageAsString());
}

[GeneratedComClass]
internal sealed partial class BrowserProcessExitedHandler(Action onExited) : ICoreWebView2BrowserProcessExitedEventHandler
{
    public void Invoke(nint sender, nint args) => onExited();
}

[GeneratedComClass]
internal sealed partial class ProcessInfosChangedHandler(Action onChanged) : ICoreWebView2ProcessInfosChangedEventHandler
{
    public void Invoke(nint sender, nint args) => onChanged();
}

[GeneratedComClass]
internal sealed partial class ProcessExtendedInfosHandler : ICoreWebView2GetProcessExtendedInfosCompletedHandler
{
    private readonly TaskCompletionSource<(int Hr, ICoreWebView2ProcessExtendedInfoCollection? Result)> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task<(int Hr, ICoreWebView2ProcessExtendedInfoCollection? Result)> Task => _completion.Task;

    public void Invoke(int errorCode, ICoreWebView2ProcessExtendedInfoCollection? result) => _completion.TrySetResult((errorCode, result));
}

internal sealed class ComFailureException : Exception
{
    public ComFailureException(string operation, int hresult)
        : base($"{operation} failed: 0x{hresult:X8}")
    {
        HResult = hresult;
    }
}
