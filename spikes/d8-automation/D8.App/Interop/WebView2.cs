using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace D8.App.Interop;

// Hand-written from WebView2.h (Microsoft.Web.WebView2 1.0.4314.35, C vtable structs).
// Slots this spike never calls are declared as [PreserveSig] int placeholders so that
// the vtable offsets of the used methods stay correct. Placeholders are never invoked.

[StructLayout(LayoutKind.Sequential)]
internal struct Rect
{
    public int Left;
    public int Top;
    public int Right;
    public int Bottom;
}

[StructLayout(LayoutKind.Sequential)]
internal struct EventRegistrationToken
{
    public long Value;
}

internal static partial class WebView2Loader
{
    // STDAPI CreateCoreWebView2EnvironmentWithOptions(PCWSTR browserExecutableFolder, PCWSTR userDataFolder,
    //     ICoreWebView2EnvironmentOptions* environmentOptions,
    //     ICoreWebView2CreateCoreWebView2EnvironmentCompletedHandler* environmentCreatedHandler);
    [LibraryImport("WebView2Loader", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial int CreateCoreWebView2EnvironmentWithOptions(
        string? browserExecutableFolder,
        string? userDataFolder,
        nint environmentOptions,
        ICoreWebView2CreateCoreWebView2EnvironmentCompletedHandler environmentCreatedHandler);
}

[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("4e8a3389-c9d8-4bd2-b6b5-124fee6cc14d")]
internal partial interface ICoreWebView2CreateCoreWebView2EnvironmentCompletedHandler
{
    void Invoke(int errorCode, ICoreWebView2Environment? createdEnvironment);
}

[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("6c4819f3-c9b7-4260-8127-c9f5bde7f68c")]
internal partial interface ICoreWebView2CreateCoreWebView2ControllerCompletedHandler
{
    void Invoke(int errorCode, ICoreWebView2Controller? createdController);
}

[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("5c4889f0-5ef6-4c5a-952c-d8f1b92d0574")]
internal partial interface ICoreWebView2CallDevToolsProtocolMethodCompletedHandler
{
    void Invoke(int errorCode, string? returnObjectAsJson);
}

[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("d33a35bf-1c49-4f98-93ab-006e0533fe1c")]
internal partial interface ICoreWebView2NavigationCompletedEventHandler
{
    // Arguments are borrowed for the duration of the call; the spike does not read them.
    void Invoke(nint sender, nint args);
}

[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("b96d755e-0319-4e92-a296-23436f46a1fc")]
internal partial interface ICoreWebView2Environment
{
    void CreateCoreWebView2Controller(nint parentWindow, ICoreWebView2CreateCoreWebView2ControllerCompletedHandler handler);
}

[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("4d00c0d1-9434-4eb6-8078-8697a560334f")]
internal partial interface ICoreWebView2Controller
{
    [PreserveSig] int GetIsVisible(out int isVisible);
    void PutIsVisible(int isVisible);
    [PreserveSig] int GetBounds(out Rect bounds);
    void PutBounds(Rect bounds);
    [PreserveSig] int Slot07GetZoomFactor();
    [PreserveSig] int Slot08PutZoomFactor();
    [PreserveSig] int Slot09AddZoomFactorChanged();
    [PreserveSig] int Slot10RemoveZoomFactorChanged();
    [PreserveSig] int Slot11SetBoundsAndZoomFactor();
    [PreserveSig] int Slot12MoveFocus();
    [PreserveSig] int Slot13AddMoveFocusRequested();
    [PreserveSig] int Slot14RemoveMoveFocusRequested();
    [PreserveSig] int Slot15AddGotFocus();
    [PreserveSig] int Slot16RemoveGotFocus();
    [PreserveSig] int Slot17AddLostFocus();
    [PreserveSig] int Slot18RemoveLostFocus();
    [PreserveSig] int Slot19AddAcceleratorKeyPressed();
    [PreserveSig] int Slot20RemoveAcceleratorKeyPressed();
    [PreserveSig] int Slot21GetParentWindow();
    [PreserveSig] int Slot22PutParentWindow();
    [PreserveSig] int Slot23NotifyParentWindowPositionChanged();
    void Close();
    ICoreWebView2 GetCoreWebView2();
}

[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("e562e4f0-d7fa-43ac-8d71-c05150499f00")]
internal partial interface ICoreWebView2Settings
{
    [PreserveSig] int Slot03GetIsScriptEnabled();
    [PreserveSig] int Slot04PutIsScriptEnabled();
    [PreserveSig] int Slot05GetIsWebMessageEnabled();
    [PreserveSig] int Slot06PutIsWebMessageEnabled();
    [PreserveSig] int Slot07GetAreDefaultScriptDialogsEnabled();
    [PreserveSig] int Slot08PutAreDefaultScriptDialogsEnabled();
    [PreserveSig] int Slot09GetIsStatusBarEnabled();
    [PreserveSig] int Slot10PutIsStatusBarEnabled();
    [PreserveSig] int GetAreDevToolsEnabled(out int enabled);
    void PutAreDevToolsEnabled(int enabled);
}

[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("76eceacb-0462-4d94-ac83-423a6793775e")]
internal partial interface ICoreWebView2
{
    ICoreWebView2Settings GetSettings();
    [PreserveSig] int Slot04GetSource();
    [PreserveSig] int Slot05Navigate();
    void NavigateToString(string htmlContent);
    [PreserveSig] int Slot07AddNavigationStarting();
    [PreserveSig] int Slot08RemoveNavigationStarting();
    [PreserveSig] int Slot09AddContentLoading();
    [PreserveSig] int Slot10RemoveContentLoading();
    [PreserveSig] int Slot11AddSourceChanged();
    [PreserveSig] int Slot12RemoveSourceChanged();
    [PreserveSig] int Slot13AddHistoryChanged();
    [PreserveSig] int Slot14RemoveHistoryChanged();
    EventRegistrationToken AddNavigationCompleted(ICoreWebView2NavigationCompletedEventHandler eventHandler);
    void RemoveNavigationCompleted(EventRegistrationToken token);
    [PreserveSig] int Slot17AddFrameNavigationStarting();
    [PreserveSig] int Slot18RemoveFrameNavigationStarting();
    [PreserveSig] int Slot19AddFrameNavigationCompleted();
    [PreserveSig] int Slot20RemoveFrameNavigationCompleted();
    [PreserveSig] int Slot21AddScriptDialogOpening();
    [PreserveSig] int Slot22RemoveScriptDialogOpening();
    [PreserveSig] int Slot23AddPermissionRequested();
    [PreserveSig] int Slot24RemovePermissionRequested();
    [PreserveSig] int Slot25AddProcessFailed();
    [PreserveSig] int Slot26RemoveProcessFailed();
    [PreserveSig] int Slot27AddScriptToExecuteOnDocumentCreated();
    [PreserveSig] int Slot28RemoveScriptToExecuteOnDocumentCreated();
    [PreserveSig] int Slot29ExecuteScript();
    [PreserveSig] int Slot30CapturePreview();
    [PreserveSig] int Slot31Reload();
    [PreserveSig] int Slot32PostWebMessageAsJson();
    [PreserveSig] int Slot33PostWebMessageAsString();
    [PreserveSig] int Slot34AddWebMessageReceived();
    [PreserveSig] int Slot35RemoveWebMessageReceived();
    void CallDevToolsProtocolMethod(
        string methodName,
        string parametersAsJson,
        ICoreWebView2CallDevToolsProtocolMethodCompletedHandler handler);
    uint GetBrowserProcessId();
}
