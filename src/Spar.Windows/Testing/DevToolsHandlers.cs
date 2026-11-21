using System.Runtime.InteropServices.Marshalling;
using Spar.Hosting;
using Spar.Windows.Interop;

namespace Spar.Windows.Testing;

/// <summary>
/// One DevTools Protocol call of a test's page driver: completes with the result object as JSON, or fails with the
/// protocol's own error (WebView2 reports a refused call with a failure code and the error object as its JSON).
/// </summary>
[GeneratedComClass]
internal sealed partial class DevToolsCall(string method) : ICoreWebView2CallDevToolsProtocolMethodCompletedHandler
{
    private readonly TaskCompletionSource<string> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task<string> Task => _completion.Task;

    public void Invoke(int errorCode, string? returnObjectAsJson)
    {
        if (errorCode < 0)
        {
            _completion.TrySetException(new DevToolsException(method, errorCode, returnObjectAsJson));
            return;
        }

        _completion.TrySetResult(returnObjectAsJson ?? "{}");
    }
}

/// <summary>A DevTools Protocol event of a test's page, handed to the driver's callback as JSON on the UI thread.</summary>
[GeneratedComClass]
internal sealed partial class DevToolsEvents(Action<string> onEvent, Action<Exception> report) :
    ICoreWebView2DevToolsProtocolEventReceivedEventHandler
{
    public void Invoke(nint sender, nint args)
    {
        ICoreWebView2DevToolsProtocolEventReceivedEventArgs? received = null;
        try
        {
            received = Com.Borrow<ICoreWebView2DevToolsProtocolEventReceivedEventArgs>(args);
            onEvent(received.GetParameterObjectAsJson());
        }
        catch (Exception exception)
        {
            report(exception);
        }
        finally
        {
            Com.Release(received);
        }
    }
}

/// <summary>
/// A DevTools Protocol call the page's session refused: the method, WebView2's failure code, and the protocol's error
/// object as JSON when WebView2 passed one (such as <c>{"code":-32000,"message":"Cannot find context with specified id"}</c>).
/// </summary>
internal sealed class DevToolsException(string method, int code, string? error)
    : Exception($"{method} failed with 0x{code:X8}{(string.IsNullOrEmpty(error) ? "" : ": " + error)}")
{
    public string Method { get; } = method;

    public int Code { get; } = code;

    public string? Error { get; } = error;
}
