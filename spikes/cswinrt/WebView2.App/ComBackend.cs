#if SPIKE_COM
using System.Runtime.InteropServices;
using Spike.Interop;

namespace Spike;

/// <summary>WebView2 through the hand-written [GeneratedComInterface] declarations in Interop/ (copied from spikes/react).</summary>
internal static class Backend
{
    public const string Name = "Com";

    private static Host? s_host;
    private static ICoreWebView2Environment? s_environment;
    private static ICoreWebView2Controller? s_controller;
    private static ICoreWebView2? s_webView;
    private static NavigationCompletedHandler? s_navigationHandler;
    private static WebResourceRequestedHandler? s_resourceHandler;
    private static WebMessageReceivedHandler? s_messageHandler;
    private static EventRegistrationToken s_navigationToken;
    private static EventRegistrationToken s_resourceToken;
    private static EventRegistrationToken s_messageToken;
    private static TaskCompletionSource? s_navigated;

    public static async Task StartAsync(Host host)
    {
        s_host = host;
        var environmentHandler = new EnvironmentCompletedHandler();
        Marshal.ThrowExceptionForHR(NativeLoader.CreateCoreWebView2EnvironmentWithOptions(null, host.ProfileDirectory(), 0, environmentHandler));
        s_environment = await environmentHandler.Task;
        host.Mark("environment");

        var controllerHandler = new ControllerCompletedHandler();
        s_environment.CreateCoreWebView2Controller(host.WindowHandle, controllerHandler);
        s_controller = await controllerHandler.Task;
        host.Mark("controller");

        s_webView = s_controller.GetCoreWebView2();
        host.BrowserProcessId = s_webView.GetBrowserProcessId();
        Resize();

        s_resourceHandler = new WebResourceRequestedHandler(OnWebResourceRequested);
        s_resourceToken = s_webView.AddWebResourceRequested(s_resourceHandler);
        s_webView.AddWebResourceRequestedFilter(Host.Origin + "/*", 0); // COREWEBVIEW2_WEB_RESOURCE_CONTEXT_ALL
        s_messageHandler = new WebMessageReceivedHandler(OnWebMessageReceived);
        s_messageToken = s_webView.AddWebMessageReceived(s_messageHandler);
        s_navigated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        s_navigationHandler = new NavigationCompletedHandler(() => s_navigated.TrySetResult());
        s_navigationToken = s_webView.AddNavigationCompleted(s_navigationHandler);

        s_webView.Navigate(Host.Origin + "/");
        await s_navigated.Task;
        host.Mark("navigation-completed");
    }

    public static Task<string> CallDevToolsAsync(string method, string parametersAsJson)
    {
        var handler = new DevToolsCallHandler(method);
        s_webView!.CallDevToolsProtocolMethod(method, parametersAsJson, handler);
        return handler.Task;
    }

    public static void Resize()
    {
        if (s_controller is not null && s_host is not null)
        {
            var (width, height) = s_host.ClientSize();
            s_controller.PutBounds(new Rect { Left = 0, Top = 0, Right = width, Bottom = height });
        }
    }

    public static void Shutdown()
    {
        if (s_webView is not null)
        {
            if (s_navigationHandler is not null) s_webView.RemoveNavigationCompleted(s_navigationToken);
            if (s_resourceHandler is not null) s_webView.RemoveWebResourceRequested(s_resourceToken);
            if (s_messageHandler is not null) s_webView.RemoveWebMessageReceived(s_messageToken);
        }

        s_navigationHandler = null;
        s_resourceHandler = null;
        s_messageHandler = null;
        s_controller?.Close();
        s_controller = null;
        s_webView = null;
        s_environment = null;
    }

    private static void OnWebMessageReceived(ICoreWebView2WebMessageReceivedEventArgs args)
    {
        if (args.TryGetWebMessageAsString(out var message) < 0)
        {
            return;
        }

        var reply = s_host!.OnPageMessage(args.GetSource(), message);
        if (reply is not null)
        {
            s_webView?.PostWebMessageAsString(reply);
        }
    }

    private static void OnWebResourceRequested(ICoreWebView2WebResourceRequestedEventArgs args)
    {
        var request = args.GetRequest();
        var method = request.GetMethod();
        var uri = request.GetUri();
        var body = ReadAll(request.GetContent());
        if (method != "POST")
        {
            args.PutResponse(Respond(Host.Serve(method, uri, body)));
            return;
        }

        var deferral = args.GetDeferral();
        _ = CompleteAsync(args, deferral, method, uri, body);
    }

    private static async Task CompleteAsync(ICoreWebView2WebResourceRequestedEventArgs args, ICoreWebView2Deferral deferral, string method, string uri, byte[] body)
    {
        await Task.Yield();
        args.PutResponse(Respond(Host.Serve(method, uri, body)));
        deferral.Complete();
    }

    private static unsafe ICoreWebView2WebResourceResponse Respond((int Status, string Reason, string ContentType, byte[] Body) response)
    {
        nint stream;
        fixed (byte* data = response.Body)
        {
            stream = NativeLoader.SHCreateMemStream(data, (uint)response.Body.Length);
        }

        if (stream == 0)
        {
            throw new OutOfMemoryException("SHCreateMemStream failed.");
        }

        try
        {
            return s_environment!.CreateWebResourceResponse(stream, response.Status, response.Reason, $"Content-Type: {response.ContentType}\r\nCache-Control: no-store");
        }
        finally
        {
            Marshal.Release(stream);
        }
    }

    private static unsafe byte[] ReadAll(ISequentialStream? stream)
    {
        if (stream is null)
        {
            return [];
        }

        using var result = new MemoryStream();
        var chunk = new byte[64 * 1024];
        while (true)
        {
            uint read;
            fixed (byte* data = chunk)
            {
                Marshal.ThrowExceptionForHR(stream.Read((nint)data, (uint)chunk.Length, (nint)(&read)));
            }

            if (read == 0)
            {
                return result.ToArray();
            }

            result.Write(chunk, 0, (int)read);
        }
    }
}
#endif
