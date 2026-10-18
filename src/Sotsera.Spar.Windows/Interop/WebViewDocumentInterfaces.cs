// ABI projections from Microsoft.Web.WebView2 1.0.4258.31, build/native/include/WebView2.h.
// Copyright (C) Microsoft Corporation. All rights reserved. See third-party/webview2/LICENSE.txt.
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Sotsera.Spar.Windows.Interop;

[GeneratedComInterface(Options = ComInterfaceOptions.ComObjectWrapper)]
[Guid("5b495469-e119-438a-9b18-7604f25f2e49")]
internal partial interface INavigationStartingArgs
{
    [PreserveSig] int GetUri(out nint uri);
    [PreserveSig] int GetIsUserInitiated(out int value);
    [PreserveSig] int GetIsRedirected(out int value);
    [PreserveSig] int GetRequestHeaders(out nint value);
    [PreserveSig] int GetCancel(out int value);
    [PreserveSig] int SetCancel(int value);
    [PreserveSig] int GetNavigationId(out ulong value);
}

[GeneratedComInterface(Options = ComInterfaceOptions.ComObjectWrapper)]
[Guid("0c8a1275-9b6b-4901-87ad-70df25bafa6e")]
internal partial interface IContentLoadingArgs
{
    [PreserveSig] int GetIsErrorPage(out int value);
    [PreserveSig] int GetNavigationId(out ulong value);
}

[GeneratedComInterface(Options = ComInterfaceOptions.ComObjectWrapper)]
[Guid("0f99a40c-e962-4207-9e92-e3d542eff849")]
internal partial interface IWebMessageArgs
{
    [PreserveSig] int GetSource(out nint value);
    [PreserveSig] int GetWebMessageAsJson(out nint value);
    [PreserveSig] int TryGetWebMessageAsString(out nint value);
}

[GeneratedComInterface(Options = ComInterfaceOptions.ComObjectWrapper)]
[Guid("453e667f-12c7-49d4-be6d-ddbe7956f57a")]
internal partial interface IWebResourceArgs
{
    [PreserveSig] int GetRequest(out nint request);
    [PreserveSig] int GetResponse(out nint response);
    [PreserveSig] int SetResponse(nint response);
}

[GeneratedComInterface(Options = ComInterfaceOptions.ComObjectWrapper)]
[Guid("97055cd4-512c-4264-8b5f-e3f446cea6a5")]
internal partial interface IWebResourceRequest
{
    [PreserveSig] int GetUri(out nint value);
    [PreserveSig] int SetUri(nint value);
    [PreserveSig] int GetMethod(out nint value);
}

[GeneratedComInterface(Options = ComInterfaceOptions.ComObjectWrapper)]
[Guid("34acb11c-fc37-4418-9132-f9c21d1eafb9")]
internal partial interface INewWindowArgs
{
    [PreserveSig] int GetUri(out nint value);
    [PreserveSig] int SetNewWindow(nint value);
    [PreserveSig] int GetNewWindow(out nint value);
    [PreserveSig] int SetHandled(int value);
}

[GeneratedComInterface(Options = ComInterfaceOptions.ComObjectWrapper)]
[Guid("973ae2ef-ff18-4894-8fb2-3c758f046810")]
internal partial interface IPermissionArgs
{
    [PreserveSig] int GetUri(out nint value);
    [PreserveSig] int GetPermissionKind(out int value);
    [PreserveSig] int GetIsUserInitiated(out int value);
    [PreserveSig] int GetState(out int value);
    [PreserveSig] int SetState(int value);
}

[GeneratedComInterface(Options = ComInterfaceOptions.ComObjectWrapper)]
[Guid("e562e4f0-d7fa-43ac-8d71-c05150499f00")]
internal partial interface IWebViewSettings
{
    [PreserveSig] int GetIsScriptEnabled(out int value);
    [PreserveSig] int SetIsScriptEnabled(int value);
    [PreserveSig] int GetIsWebMessageEnabled(out int value);
    [PreserveSig] int SetIsWebMessageEnabled(int value);
    [PreserveSig] int GetAreDefaultScriptDialogsEnabled(out int value);
    [PreserveSig] int SetAreDefaultScriptDialogsEnabled(int value);
    [PreserveSig] int GetIsStatusBarEnabled(out int value);
    [PreserveSig] int SetIsStatusBarEnabled(int value);
    [PreserveSig] int GetAreDevToolsEnabled(out int value);
    [PreserveSig] int SetAreDevToolsEnabled(int value);
    [PreserveSig] int GetAreDefaultContextMenusEnabled(out int value);
    [PreserveSig] int SetAreDefaultContextMenusEnabled(int value);
    [PreserveSig] int GetAreHostObjectsAllowed(out int value);
    [PreserveSig] int SetAreHostObjectsAllowed(int value);
}

[GeneratedComInterface(Options = ComInterfaceOptions.ManagedObjectWrapper)]
[Guid("9adbe429-f36d-432b-9ddc-f8881fbd76e3")]
internal partial interface INavigationStarting
{
    [PreserveSig] int Invoke(nint sender, nint args);
}

[GeneratedComInterface(Options = ComInterfaceOptions.ManagedObjectWrapper)]
[Guid("364471e7-f2be-4910-bdba-d72077d51c4b")]
internal partial interface IContentLoading
{
    [PreserveSig] int Invoke(nint sender, nint args);
}

[GeneratedComInterface(Options = ComInterfaceOptions.ManagedObjectWrapper)]
[Guid("57213f19-00e6-49fa-8e07-898ea01ecbd2")]
internal partial interface IWebMessageReceived
{
    [PreserveSig] int Invoke(nint sender, nint args);
}

[GeneratedComInterface(Options = ComInterfaceOptions.ManagedObjectWrapper)]
[Guid("ab00b74c-15f1-4646-80e8-e76341d25d71")]
internal partial interface IWebResourceRequested
{
    [PreserveSig] int Invoke(nint sender, nint args);
}

[GeneratedComInterface(Options = ComInterfaceOptions.ManagedObjectWrapper)]
[Guid("d4c185fe-c81c-4989-97af-2d3fa7ab5651")]
internal partial interface INewWindowRequested
{
    [PreserveSig] int Invoke(nint sender, nint args);
}

[GeneratedComInterface(Options = ComInterfaceOptions.ManagedObjectWrapper)]
[Guid("15e1c6a3-c72a-4df3-91d7-d097fbec6bfd")]
internal partial interface IPermissionRequested
{
    [PreserveSig] int Invoke(nint sender, nint args);
}

[GeneratedComClass]
internal sealed partial class NavigationStarting(Action<nint> callback) : INavigationStarting
{
    public int Invoke(nint sender, nint args)
    {
        callback(args);
        return 0;
    }
}

[GeneratedComClass]
internal sealed partial class ContentLoading(Action<nint> callback) : IContentLoading
{
    public int Invoke(nint sender, nint args)
    {
        callback(args);
        return 0;
    }
}

[GeneratedComClass]
internal sealed partial class WebMessageReceived(Action<nint> callback) : IWebMessageReceived
{
    public int Invoke(nint sender, nint args)
    {
        callback(args);
        return 0;
    }
}

[GeneratedComClass]
internal sealed partial class WebResourceRequested(Action<nint> callback) : IWebResourceRequested
{
    public int Invoke(nint sender, nint args)
    {
        callback(args);
        return 0;
    }
}

[GeneratedComClass]
internal sealed partial class NewWindowRequested(Action<nint> callback) : INewWindowRequested
{
    public int Invoke(nint sender, nint args)
    {
        callback(args);
        return 0;
    }
}

[GeneratedComClass]
internal sealed partial class PermissionRequested(Action<nint> callback) : IPermissionRequested
{
    public int Invoke(nint sender, nint args)
    {
        callback(args);
        return 0;
    }
}
