// ABI projections from Microsoft.Web.WebView2 1.0.4258.31, build/native/include/WebView2.h.
// Copyright (C) Microsoft Corporation. All rights reserved.
// See third-party/webview2/LICENSE.txt and docs/09-native-host.md for provenance.
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Sotsera.Spar.Windows.Interop;

// Prefix projections preserve every slot preceding the last method used here.
// Native pointers are kept explicit: callbacks lend references; getters transfer references.
[GeneratedComInterface(Options = ComInterfaceOptions.ComObjectWrapper)]
[Guid("b96d755e-0319-4e92-a296-23436f46a1fc")]
internal partial interface IWebViewEnvironment
{
    [PreserveSig] int CreateController(nint parentWindow, nint handler);
    [PreserveSig] int CreateWebResourceResponse(nint content, int statusCode, nint reason, nint headers, out nint response);
    [PreserveSig] int GetBrowserVersionString(out nint version);
    [PreserveSig] int AddNewBrowserVersionAvailable(nint handler, out long token);
    [PreserveSig] int RemoveNewBrowserVersionAvailable(long token);
}

// Environment7 includes all Environment2–6 methods before its profile getter.
[GeneratedComInterface(Options = ComInterfaceOptions.ComObjectWrapper)]
[Guid("43c22296-3bbd-43a4-9c00-5c0df6dd29a2")]
internal partial interface IWebViewEnvironmentProfile : IWebViewEnvironment
{
    [PreserveSig] int CreateWebResourceRequest(nint uri, nint method, nint data, nint headers, out nint request);
    [PreserveSig] int CreateCompositionController(nint parent, nint handler);
    [PreserveSig] int CreatePointerInfo(out nint value);
    [PreserveSig] int GetAutomationProviderForWindow(nint window, out nint value);
    [PreserveSig] int AddBrowserProcessExited(nint handler, out long token);
    [PreserveSig] int RemoveBrowserProcessExited(long token);
    [PreserveSig] int CreatePrintSettings(out nint value);
    [PreserveSig] int GetUserDataFolder(out nint value);
}

[GeneratedComInterface(Options = ComInterfaceOptions.ComObjectWrapper)]
[Guid("4d00c0d1-9434-4eb6-8078-8697a560334f")]
internal partial interface IWebViewController
{
    [PreserveSig] int GetIsVisible(out int visible);
    [PreserveSig] int SetIsVisible(int visible);
    [PreserveSig] int GetBounds(out NativeRect bounds);
    [PreserveSig] int SetBounds(NativeRect bounds);
    [PreserveSig] int GetZoomFactor(out double zoom);
    [PreserveSig] int SetZoomFactor(double zoom);
    [PreserveSig] int AddZoomFactorChanged(nint handler, out long token);
    [PreserveSig] int RemoveZoomFactorChanged(long token);
    [PreserveSig] int SetBoundsAndZoomFactor(NativeRect bounds, double zoom);
    [PreserveSig] int MoveFocus(int reason);
    [PreserveSig] int AddMoveFocusRequested(nint handler, out long token);
    [PreserveSig] int RemoveMoveFocusRequested(long token);
    [PreserveSig] int AddGotFocus(nint handler, out long token);
    [PreserveSig] int RemoveGotFocus(long token);
    [PreserveSig] int AddLostFocus(nint handler, out long token);
    [PreserveSig] int RemoveLostFocus(long token);
    [PreserveSig] int AddAcceleratorKeyPressed(nint handler, out long token);
    [PreserveSig] int RemoveAcceleratorKeyPressed(long token);
    [PreserveSig] int GetParentWindow(out nint window);
    [PreserveSig] int SetParentWindow(nint window);
    [PreserveSig] int NotifyParentWindowPositionChanged();
    [PreserveSig] int Close();
    [PreserveSig] int GetWebView(out nint webView);
}

[GeneratedComInterface(Options = ComInterfaceOptions.ComObjectWrapper)]
[Guid("76eceacb-0462-4d94-ac83-423a6793775e")]
internal partial interface IWebView
{
    [PreserveSig] int GetSettings(out nint settings);
    [PreserveSig] int GetSource(out nint uri);
    [PreserveSig] int Navigate(nint uri);
    [PreserveSig] int NavigateToString(nint html);
    [PreserveSig] int AddNavigationStarting(nint handler, out long token);
    [PreserveSig] int RemoveNavigationStarting(long token);
    [PreserveSig] int AddContentLoading(nint handler, out long token);
    [PreserveSig] int RemoveContentLoading(long token);
    [PreserveSig] int AddSourceChanged(nint handler, out long token);
    [PreserveSig] int RemoveSourceChanged(long token);
    [PreserveSig] int AddHistoryChanged(nint handler, out long token);
    [PreserveSig] int RemoveHistoryChanged(long token);
    [PreserveSig] int AddNavigationCompleted(nint handler, out long token);
    [PreserveSig] int RemoveNavigationCompleted(long token);
    [PreserveSig] int AddFrameNavigationStarting(nint handler, out long token);
    [PreserveSig] int RemoveFrameNavigationStarting(long token);
    [PreserveSig] int AddFrameNavigationCompleted(nint handler, out long token);
    [PreserveSig] int RemoveFrameNavigationCompleted(long token);
    [PreserveSig] int AddScriptDialogOpening(nint handler, out long token);
    [PreserveSig] int RemoveScriptDialogOpening(long token);
    [PreserveSig] int AddPermissionRequested(nint handler, out long token);
    [PreserveSig] int RemovePermissionRequested(long token);
    [PreserveSig] int AddProcessFailed(nint handler, out long token);
    [PreserveSig] int RemoveProcessFailed(long token);
    [PreserveSig] int AddScriptToExecuteOnDocumentCreated(nint script, nint handler);
    [PreserveSig] int RemoveScriptToExecuteOnDocumentCreated(nint id);
    [PreserveSig] int ExecuteScript(nint script, nint handler);
    [PreserveSig] int CapturePreview(int format, nint stream, nint handler);
    [PreserveSig] int Reload();
    [PreserveSig] int PostWebMessageAsJson(nint message);
    [PreserveSig] int PostWebMessageAsString(nint message);
    [PreserveSig] int AddWebMessageReceived(nint handler, out long token);
    [PreserveSig] int RemoveWebMessageReceived(long token);
    [PreserveSig] int CallDevToolsProtocolMethod(nint method, nint parameters, nint handler);
    [PreserveSig] int GetBrowserProcessId(out uint id);
    [PreserveSig] int GetCanGoBack(out int value);
    [PreserveSig] int GetCanGoForward(out int value);
    [PreserveSig] int GoBack();
    [PreserveSig] int GoForward();
    [PreserveSig] int GetDevToolsProtocolEventReceiver(nint name, out nint receiver);
    [PreserveSig] int Stop();
    [PreserveSig] int AddNewWindowRequested(nint handler, out long token);
    [PreserveSig] int RemoveNewWindowRequested(long token);
    [PreserveSig] int AddDocumentTitleChanged(nint handler, out long token);
    [PreserveSig] int RemoveDocumentTitleChanged(long token);
    [PreserveSig] int GetDocumentTitle(out nint title);
    [PreserveSig] int AddHostObjectToScript(nint name, nint variant);
    [PreserveSig] int RemoveHostObjectFromScript(nint name);
    [PreserveSig] int OpenDevToolsWindow();
    [PreserveSig] int AddContainsFullScreenElementChanged(nint handler, out long token);
    [PreserveSig] int RemoveContainsFullScreenElementChanged(long token);
    [PreserveSig] int GetContainsFullScreenElement(out int value);
    [PreserveSig] int AddWebResourceRequested(nint handler, out long token);
    [PreserveSig] int RemoveWebResourceRequested(long token);
    [PreserveSig] int AddWebResourceRequestedFilter(nint uri, int context);
    [PreserveSig] int RemoveWebResourceRequestedFilter(nint uri, int context);
}

[GeneratedComInterface(Options = ComInterfaceOptions.ComObjectWrapper)]
[Guid("30d68b7d-20d9-4752-a9ca-ec8448fbb5c1")]
internal partial interface INavigationCompletedArgs
{
    [PreserveSig] int GetIsSuccess(out int success);
    [PreserveSig] int GetWebErrorStatus(out int status);
    [PreserveSig] int GetNavigationId(out ulong id);
}

[GeneratedComInterface(Options = ComInterfaceOptions.ManagedObjectWrapper)]
[Guid("4e8a3389-c9d8-4bd2-b6b5-124fee6cc14d")]
internal partial interface IEnvironmentCompleted
{
    [PreserveSig] int Invoke(int errorCode, nint environment);
}

[GeneratedComInterface(Options = ComInterfaceOptions.ManagedObjectWrapper)]
[Guid("6c4819f3-c9b7-4260-8127-c9f5bde7f68c")]
internal partial interface IControllerCompleted
{
    [PreserveSig] int Invoke(int errorCode, nint controller);
}

[GeneratedComInterface(Options = ComInterfaceOptions.ManagedObjectWrapper)]
[Guid("d33a35bf-1c49-4f98-93ab-006e0533fe1c")]
internal partial interface INavigationCompleted
{
    [PreserveSig] int Invoke(nint sender, nint args);
}

[GeneratedComClass]
internal sealed partial class EnvironmentCompleted(Action<int, nint> callback) : IEnvironmentCompleted
{
    public int Invoke(int errorCode, nint environment)
    {
        callback(errorCode, environment);
        return 0;
    }
}

[GeneratedComClass]
internal sealed partial class ControllerCompleted(Action<int, nint> callback) : IControllerCompleted
{
    public int Invoke(int errorCode, nint controller)
    {
        callback(errorCode, controller);
        return 0;
    }
}

[GeneratedComClass]
internal sealed partial class NavigationCompleted(Action<nint> callback) : INavigationCompleted
{
    public int Invoke(nint sender, nint args)
    {
        callback(args);
        return 0;
    }
}
