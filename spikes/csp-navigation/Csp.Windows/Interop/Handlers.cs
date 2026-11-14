using System.Runtime.InteropServices.Marshalling;

namespace Csp.Windows.Interop;

// Managed implementations of the WebView2 callbacks (copied from spikes/browser-telemetry and extended). Completion
// handlers complete a task; event handlers forward to the host on the STA thread that raised them.

[GeneratedComClass]
internal sealed partial class EnvironmentCompletedHandler : ICoreWebView2CreateCoreWebView2EnvironmentCompletedHandler
{
    private readonly TaskCompletionSource<ICoreWebView2Environment> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task<ICoreWebView2Environment> Task => _completion.Task;

    public void Invoke(int errorCode, ICoreWebView2Environment? createdEnvironment)
    {
        if (errorCode < 0 || createdEnvironment is null)
        {
            _completion.TrySetException(new ComFailureException("CreateCoreWebView2Environment", errorCode));
            return;
        }

        _completion.TrySetResult(createdEnvironment);
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
internal sealed partial class ScriptAddedHandler : ICoreWebView2AddScriptToExecuteOnDocumentCreatedCompletedHandler
{
    private readonly TaskCompletionSource<string> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task<string> Task => _completion.Task;

    public void Invoke(int errorCode, string? id)
    {
        if (errorCode < 0)
        {
            _completion.TrySetException(new ComFailureException("AddScriptToExecuteOnDocumentCreated", errorCode));
            return;
        }

        _completion.TrySetResult(id ?? "");
    }
}

[GeneratedComClass]
internal sealed partial class NavigationCompletedHandler(Action<ICoreWebView2NavigationCompletedEventArgs> onCompleted)
    : ICoreWebView2NavigationCompletedEventHandler
{
    public void Invoke(nint sender, ICoreWebView2NavigationCompletedEventArgs args) => onCompleted(args);
}

[GeneratedComClass]
internal sealed partial class NavigationStartingHandler(Action<ICoreWebView2NavigationStartingEventArgs> onStarting)
    : ICoreWebView2NavigationStartingEventHandler
{
    public void Invoke(nint sender, ICoreWebView2NavigationStartingEventArgs args) => onStarting(args);
}

[GeneratedComClass]
internal sealed partial class NewWindowRequestedHandler(Action<ICoreWebView2NewWindowRequestedEventArgs> onRequested)
    : ICoreWebView2NewWindowRequestedEventHandler
{
    public void Invoke(nint sender, ICoreWebView2NewWindowRequestedEventArgs args) => onRequested(args);
}

[GeneratedComClass]
internal sealed partial class PermissionRequestedHandler(Action<ICoreWebView2PermissionRequestedEventArgs> onRequested)
    : ICoreWebView2PermissionRequestedEventHandler
{
    public void Invoke(nint sender, ICoreWebView2PermissionRequestedEventArgs args) => onRequested(args);
}

[GeneratedComClass]
internal sealed partial class WebResourceRequestedHandler(Action<ICoreWebView2WebResourceRequestedEventArgs> onRequested)
    : ICoreWebView2WebResourceRequestedEventHandler
{
    public void Invoke(nint sender, ICoreWebView2WebResourceRequestedEventArgs args) => onRequested(args);
}

[GeneratedComClass]
internal sealed partial class WebMessageReceivedHandler(Action<ICoreWebView2WebMessageReceivedEventArgs> onReceived)
    : ICoreWebView2WebMessageReceivedEventHandler
{
    public void Invoke(nint sender, ICoreWebView2WebMessageReceivedEventArgs args) => onReceived(args);
}

[GeneratedComClass]
internal sealed partial class DevToolsCallHandler(string method) : ICoreWebView2CallDevToolsProtocolMethodCompletedHandler
{
    private readonly TaskCompletionSource<string> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task<string> Task => _completion.Task;

    public void Invoke(int errorCode, string? returnObjectAsJson)
    {
        if (errorCode < 0)
        {
            _completion.TrySetException(new ComFailureException($"{method} ({returnObjectAsJson})", errorCode));
            return;
        }

        _completion.TrySetResult(returnObjectAsJson ?? "{}");
    }
}

internal sealed class ComFailureException : Exception
{
    public ComFailureException(string operation, int hresult)
        : base($"{operation} failed: 0x{hresult:X8}")
    {
        HResult = hresult;
    }
}
