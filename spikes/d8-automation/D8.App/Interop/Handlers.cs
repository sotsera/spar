using System.Runtime.InteropServices.Marshalling;

namespace D8.App.Interop;

// Managed implementations of the WebView2 completion handlers. Each one completes a task;
// continuations run asynchronously so that no managed work runs inside the native callback.

[GeneratedComClass]
internal sealed partial class EnvironmentCompletedHandler : ICoreWebView2CreateCoreWebView2EnvironmentCompletedHandler
{
    private readonly TaskCompletionSource<ICoreWebView2Environment> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

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
    private readonly TaskCompletionSource<ICoreWebView2Controller> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

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
internal sealed partial class NavigationCompletedHandler : ICoreWebView2NavigationCompletedEventHandler
{
    private readonly Action _onCompleted;

    public NavigationCompletedHandler(Action onCompleted)
    {
        _onCompleted = onCompleted;
    }

    public void Invoke(nint sender, nint args) => _onCompleted();
}

#if SPAR_DEVTOOLS
[GeneratedComClass]
internal sealed partial class DevToolsCallHandler : ICoreWebView2CallDevToolsProtocolMethodCompletedHandler
{
    private readonly string _method;
    private readonly TaskCompletionSource<string> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public DevToolsCallHandler(string method)
    {
        _method = method;
    }

    public Task<string> Task => _completion.Task;

    public void Invoke(int errorCode, string? returnObjectAsJson)
    {
        if (errorCode < 0)
        {
            _completion.TrySetException(new CdpException(_method, errorCode, returnObjectAsJson));
            return;
        }

        _completion.TrySetResult(returnObjectAsJson ?? "{}");
    }
}

internal sealed class CdpException : Exception
{
    public CdpException(string method, int hresult, string? payload)
        : base($"{method} failed: 0x{hresult:X8} {payload}")
    {
        HResult = hresult;
        Payload = payload;
    }

    public string? Payload { get; }
}
#endif

internal sealed class ComFailureException : Exception
{
    public ComFailureException(string operation, int hresult)
        : base($"{operation} failed: 0x{hresult:X8}")
    {
        HResult = hresult;
    }
}
