using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Spar.PageHeartbeat.Interop;

// Completion and event handlers implemented by the host. Shapes follow spikes/resource-usage.

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
internal sealed partial class NavigationStartingHandler(Action<ICoreWebView2NavigationStartingEventArgs> onStarting) : ICoreWebView2NavigationStartingEventHandler
{
    public void Invoke(nint sender, ICoreWebView2NavigationStartingEventArgs args) => onStarting(args);
}

[GeneratedComClass]
internal sealed partial class ContentLoadingHandler(Action<ICoreWebView2ContentLoadingEventArgs> onLoading) : ICoreWebView2ContentLoadingEventHandler
{
    public void Invoke(nint sender, ICoreWebView2ContentLoadingEventArgs args) => onLoading(args);
}

[GeneratedComClass]
internal sealed partial class NavigationCompletedHandler(Action<ICoreWebView2NavigationCompletedEventArgs> onCompleted) : ICoreWebView2NavigationCompletedEventHandler
{
    public void Invoke(nint sender, ICoreWebView2NavigationCompletedEventArgs args) => onCompleted(args);
}

[GeneratedComClass]
internal sealed partial class ProcessFailedHandler(Action<ICoreWebView2ProcessFailedEventArgs2> onFailed) : ICoreWebView2ProcessFailedEventHandler
{
    private static readonly StrategyBasedComWrappers s_wrappers = new();

    // Borrowed pointer: the wrapper takes its own reference; the cast is a QueryInterface for Args2.
    public void Invoke(nint sender, nint args) =>
        onFailed((ICoreWebView2ProcessFailedEventArgs2)s_wrappers.GetOrCreateObjectForComInstance(args, CreateObjectFlags.UniqueInstance));
}

[GeneratedComClass]
internal sealed partial class ScriptAddedHandler : ICoreWebView2AddScriptToExecuteOnDocumentCreatedCompletedHandler
{
    private readonly TaskCompletionSource<int> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task<int> Task => _completion.Task;

    public void Invoke(int errorCode, nint id) => _completion.TrySetResult(errorCode);
}

[GeneratedComClass]
internal sealed partial class ExecuteScriptHandler : ICoreWebView2ExecuteScriptCompletedHandler
{
    private readonly TaskCompletionSource<(int Hr, string? Result)> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task<(int Hr, string? Result)> Task => _completion.Task;

    public void Invoke(int errorCode, nint result) => _completion.TrySetResult((errorCode, Marshal.PtrToStringUni(result)));
}

[GeneratedComClass]
internal sealed partial class TrySuspendHandler : ICoreWebView2TrySuspendCompletedHandler
{
    private readonly TaskCompletionSource<(int Hr, int Result)> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task<(int Hr, int Result)> Task => _completion.Task;

    public void Invoke(int errorCode, int result) => _completion.TrySetResult((errorCode, result));
}

internal sealed class ComFailureException : Exception
{
    public ComFailureException(string operation, int hresult)
        : base($"{operation} failed: 0x{hresult:X8}")
    {
        HResult = hresult;
    }
}
