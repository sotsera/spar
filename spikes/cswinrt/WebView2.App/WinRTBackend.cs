#if !SPIKE_COM
using Microsoft.Web.WebView2.Core;
using Windows.Foundation;
using Windows.Storage.Streams;

namespace Spike;

/// <summary>WebView2 through the CsWinRT 3.0 projection of Microsoft.Web.WebView2.Core.winmd. No hand-written interop.</summary>
internal static class Backend
{
    public const string Name = "WinRT";

    private static Host? s_host;
    private static CoreWebView2Environment? s_environment;
    private static CoreWebView2Controller? s_controller;
    private static CoreWebView2? s_webView;
    private static TaskCompletionSource<bool>? s_navigated;

    public static async Task StartAsync(Host host)
    {
        s_host = host;
        var options = new CoreWebView2EnvironmentOptions();
        s_environment = await CoreWebView2Environment.CreateWithOptionsAsync(string.Empty, host.ProfileDirectory(), options);
        host.Mark("environment");

        var window = CoreWebView2ControllerWindowReference.CreateFromWindowHandle((ulong)host.WindowHandle);
        s_controller = await s_environment.CreateCoreWebView2ControllerAsync(window);
        host.Mark("controller");

        s_webView = s_controller.CoreWebView2;
        host.BrowserProcessId = s_webView.BrowserProcessId;
        Resize();

        s_webView.AddWebResourceRequestedFilter(Host.Origin + "/*", CoreWebView2WebResourceContext.All);
        s_webView.WebResourceRequested += OnWebResourceRequested;
        s_webView.WebMessageReceived += OnWebMessageReceived;
        s_webView.NavigationCompleted += OnNavigationCompleted;

        s_navigated = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        s_webView.Navigate(Host.Origin + "/");
        var success = await s_navigated.Task;
        host.Mark("navigation-completed");
        if (!success)
        {
            throw new InvalidOperationException("Navigation failed.");
        }
    }

    public static async Task<string> CallDevToolsAsync(string method, string parametersAsJson) =>
        await s_webView!.CallDevToolsProtocolMethodAsync(method, parametersAsJson);

    public static void Resize()
    {
        if (s_controller is not null && s_host is not null)
        {
            var (width, height) = s_host.ClientSize();
            s_controller.Bounds = new Rect { X = 0, Y = 0, Width = width, Height = height };
        }
    }

    public static void Shutdown()
    {
        if (s_webView is not null)
        {
            s_webView.WebResourceRequested -= OnWebResourceRequested;
            s_webView.WebMessageReceived -= OnWebMessageReceived;
            s_webView.NavigationCompleted -= OnNavigationCompleted;
        }

        s_controller?.Close();
        s_controller = null;
        s_webView = null;
        s_environment = null;
    }

    private static void OnNavigationCompleted(CoreWebView2 sender, CoreWebView2NavigationCompletedEventArgs args) =>
        s_navigated?.TrySetResult(args.IsSuccess);

    private static void OnWebMessageReceived(CoreWebView2 sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        string? message;
        try
        {
            message = args.TryGetWebMessageAsString();
        }
        catch (ArgumentException)
        {
            return; // Not a string message.
        }

        var reply = s_host!.OnPageMessage(args.Source, message);
        if (reply is not null)
        {
            sender.PostWebMessageAsString(reply);
        }
    }

    private static void OnWebResourceRequested(CoreWebView2 sender, CoreWebView2WebResourceRequestedEventArgs args)
    {
        var request = args.Request;
        var body = ReadAll(request.Content);
        if (request.Method != "POST")
        {
            args.Response = Respond(Host.Serve(request.Method, request.Uri, body));
            return;
        }

        // POSTs complete asynchronously through a deferral, as a backend call would.
        var deferral = args.GetDeferral();
        _ = CompleteAsync(args, deferral, request.Method, request.Uri, body);
    }

    private static async Task CompleteAsync(CoreWebView2WebResourceRequestedEventArgs args, Deferral deferral, string method, string uri, byte[] body)
    {
        await Task.Yield();
        args.Response = Respond(Host.Serve(method, uri, body));
        deferral.Complete();
    }

    private static CoreWebView2WebResourceResponse Respond((int Status, string Reason, string ContentType, byte[] Body) response) =>
        s_environment!.CreateWebResourceResponse(
            new MemoryStream(response.Body, writable: false).AsRandomAccessStream(),
            response.Status,
            response.Reason,
            $"Content-Type: {response.ContentType}\r\nCache-Control: no-store");

    private static byte[] ReadAll(IRandomAccessStream? content)
    {
        if (content is null)
        {
            return [];
        }

        using var stream = content.AsStreamForRead();
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }
}
#endif
