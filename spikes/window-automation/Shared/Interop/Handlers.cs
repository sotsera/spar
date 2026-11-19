using System.Runtime.InteropServices.Marshalling;

namespace Spar.WindowAutomation.Interop;

// Completion and event handlers, adapted from spikes/window-tests. Every one is invoked on the UI thread; the tasks
// they complete continue elsewhere (RunContinuationsAsynchronously).

[GeneratedComClass]
internal sealed partial class EnvironmentOptions : ICoreWebView2EnvironmentOptions
{
    // The SDK's default for TargetCompatibleBrowserVersion (WebView2EnvironmentOptions.h, 1.0.4314.35).
    private string _target = "156.0.4314.35";

    public string AdditionalBrowserArguments { get; set; } = "";

    public string GetAdditionalBrowserArguments() => AdditionalBrowserArguments;

    public void PutAdditionalBrowserArguments(string value) => AdditionalBrowserArguments = value;

    public string GetLanguage() => "";

    public void PutLanguage(string value)
    {
    }

    public string GetTargetCompatibleBrowserVersion() => _target;

    public void PutTargetCompatibleBrowserVersion(string value) => _target = value;

    public int GetAllowSingleSignOnUsingOSPrimaryAccount() => 0;

    public void PutAllowSingleSignOnUsingOSPrimaryAccount(int allow)
    {
    }
}

[GeneratedComClass]
internal sealed partial class EnvironmentCompletedHandler : ICoreWebView2CreateCoreWebView2EnvironmentCompletedHandler
{
    private readonly TaskCompletionSource<ICoreWebView2Environment10> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task<ICoreWebView2Environment10> Task => _completion.Task;

    public void Invoke(int errorCode, ICoreWebView2Environment10? createdEnvironment)
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
internal sealed partial class NavigationCompletedHandler(Action onCompleted) : ICoreWebView2NavigationCompletedEventHandler
{
    public void Invoke(nint sender, nint args) => onCompleted();
}

[GeneratedComClass]
internal sealed partial class WebResourceRequestedHandler(Action<ICoreWebView2WebResourceRequestedEventArgs> onRequested)
    : ICoreWebView2WebResourceRequestedEventHandler
{
    public void Invoke(nint sender, ICoreWebView2WebResourceRequestedEventArgs args) => onRequested(args);
}

[GeneratedComClass]
internal sealed partial class BrowserProcessExitedHandler(Action onExited) : ICoreWebView2BrowserProcessExitedEventHandler
{
    public void Invoke(nint sender, nint args) => onExited();
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
            _completion.TrySetException(new CdpException(method, errorCode, returnObjectAsJson));
            return;
        }

        _completion.TrySetResult(returnObjectAsJson ?? "{}");
    }
}

[GeneratedComClass]
internal sealed partial class DevToolsEventHandler(Action<string> onEvent) : ICoreWebView2DevToolsProtocolEventReceivedEventHandler
{
    public void Invoke(nint sender, ICoreWebView2DevToolsProtocolEventReceivedEventArgs2 args) => onEvent(args.GetParameterObjectAsJson());
}

[GeneratedComClass]
internal sealed partial class NewWindowRequestedHandler(Action<ICoreWebView2NewWindowRequestedEventArgs> onRequested)
    : ICoreWebView2NewWindowRequestedEventHandler
{
    public void Invoke(nint sender, ICoreWebView2NewWindowRequestedEventArgs args) => onRequested(args);
}

internal sealed class ComFailureException : Exception
{
    public ComFailureException(string operation, int hresult)
        : base($"{operation} failed: 0x{hresult:X8}")
    {
        HResult = hresult;
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
