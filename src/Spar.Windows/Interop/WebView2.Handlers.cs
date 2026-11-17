using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Spar.Windows.Interop;

// Completion and event handler interfaces, hand-declared from WebView2.h, and the host's implementations. Every
// handler forwards borrowed pointers to a delegate and never lets an exception reach WebView2: the host's own callbacks
// catch and log theirs, and anything that still escapes is reported here.

[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("4e8a3389-c9d8-4bd2-b6b5-124fee6cc14d")]
internal partial interface ICoreWebView2CreateCoreWebView2EnvironmentCompletedHandler
{
    void Invoke(int errorCode, nint createdEnvironment);
}

[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("6c4819f3-c9b7-4260-8127-c9f5bde7f68c")]
internal partial interface ICoreWebView2CreateCoreWebView2ControllerCompletedHandler
{
    void Invoke(int errorCode, nint createdController);
}

[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("9adbe429-f36d-432b-9ddc-f8881fbd76e3")]
internal partial interface ICoreWebView2NavigationStartingEventHandler
{
    void Invoke(nint sender, nint args);
}

[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("364471e7-f2be-4910-bdba-d72077d51c4b")]
internal partial interface ICoreWebView2ContentLoadingEventHandler
{
    void Invoke(nint sender, nint args);
}

[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("d33a35bf-1c49-4f98-93ab-006e0533fe1c")]
internal partial interface ICoreWebView2NavigationCompletedEventHandler
{
    void Invoke(nint sender, nint args);
}

[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("79e0aea4-990b-42d9-aa1d-0fcc2e5bc7f1")]
internal partial interface ICoreWebView2ProcessFailedEventHandler
{
    void Invoke(nint sender, nint args);
}

[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("57213f19-00e6-49fa-8e07-898ea01ecbd2")]
internal partial interface ICoreWebView2WebMessageReceivedEventHandler
{
    void Invoke(nint sender, nint args);
}

[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("ab00b74c-15f1-4646-80e8-e76341d25d71")]
internal partial interface ICoreWebView2WebResourceRequestedEventHandler
{
    void Invoke(nint sender, nint args);
}

[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("fa504257-a216-4911-a860-fe8825712861")]
internal partial interface ICoreWebView2BrowserProcessExitedEventHandler
{
    void Invoke(nint sender, nint args);
}

/// <summary>Completes when the environment exists; the result holds one reference the host owns.</summary>
[GeneratedComClass]
internal sealed partial class EnvironmentCompleted : ICoreWebView2CreateCoreWebView2EnvironmentCompletedHandler
{
    private readonly TaskCompletionSource<nint> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task<nint> Task => _completion.Task;

    public void Invoke(int errorCode, nint createdEnvironment) =>
        Complete(_completion, "CreateCoreWebView2Environment", errorCode, createdEnvironment);

    /// <summary>The created object is borrowed for the call: take a reference of the host's own before completing.</summary>
    internal static void Complete(TaskCompletionSource<nint> completion, string what, int errorCode, nint created)
    {
        if (errorCode < 0 || created == 0)
        {
            completion.TrySetException(new WebView2Exception(what, errorCode < 0 ? errorCode : unchecked((int)0x80004005)));
            return;
        }

        Marshal.AddRef(created);
        completion.TrySetResult(created);
    }
}

/// <summary>Completes when the controller exists; the result holds one reference the host owns.</summary>
[GeneratedComClass]
internal sealed partial class ControllerCompleted : ICoreWebView2CreateCoreWebView2ControllerCompletedHandler
{
    private readonly TaskCompletionSource<nint> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task<nint> Task => _completion.Task;

    public void Invoke(int errorCode, nint createdController) =>
        EnvironmentCompleted.Complete(_completion, "CreateCoreWebView2Controller", errorCode, createdController);
}

/// <summary>One handler class for every <c>(sender, args)</c> event: the host's delegate gets the args pointer, borrowed.</summary>
[GeneratedComClass]
internal sealed partial class WebView2EventHandler(Action<nint> handle, Action<Exception> report) :
    ICoreWebView2NavigationStartingEventHandler,
    ICoreWebView2ContentLoadingEventHandler,
    ICoreWebView2NavigationCompletedEventHandler,
    ICoreWebView2ProcessFailedEventHandler,
    ICoreWebView2WebMessageReceivedEventHandler,
    ICoreWebView2WebResourceRequestedEventHandler,
    ICoreWebView2BrowserProcessExitedEventHandler
{
    public void Invoke(nint sender, nint args)
    {
        try
        {
            handle(args);
        }
        catch (Exception exception)
        {
            report(exception);
        }
    }
}

/// <summary>A failed WebView2 call, with its HRESULT.</summary>
internal sealed class WebView2Exception(string what, int hresult) : Exception($"{what} failed with 0x{hresult:X8}.")
{
    public int Code { get; } = hresult;
}
