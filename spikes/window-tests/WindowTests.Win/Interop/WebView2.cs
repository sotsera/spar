using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Spar.WindowTests.Interop;

// Hand-written from WebView2.h (Microsoft.Web.WebView2 1.0.4314.35, C vtable structs). Environment, controller,
// profile and resource declarations are adapted from spikes/webview-profiles; the flat ICoreWebView2_22 below is
// printed by tools/gen-webview.mjs (every slot from 3 to 123, IUnknown included in the count). Slots the spike never
// calls are [PreserveSig] int placeholders that keep the offsets of the used methods.

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

internal static partial class NativeLoader
{
    [LibraryImport("WebView2Loader", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial int CreateCoreWebView2EnvironmentWithOptions(
        string? browserExecutableFolder,
        string? userDataFolder,
        ICoreWebView2EnvironmentOptions? environmentOptions,
        ICoreWebView2CreateCoreWebView2EnvironmentCompletedHandler environmentCreatedHandler);

    // Returns an IStream* with one reference owned by the caller, or null on failure.
    [LibraryImport("shlwapi")]
    internal static unsafe partial nint SHCreateMemStream(byte* initial, uint size);
}

[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("2fde08a8-1e9a-4766-8c05-95a9ceb9d1c5")]
internal partial interface ICoreWebView2EnvironmentOptions
{
    string GetAdditionalBrowserArguments();
    void PutAdditionalBrowserArguments(string value);
    string GetLanguage();
    void PutLanguage(string value);
    string GetTargetCompatibleBrowserVersion();
    void PutTargetCompatibleBrowserVersion(string value);
    int GetAllowSingleSignOnUsingOSPrimaryAccount();
    void PutAllowSingleSignOnUsingOSPrimaryAccount(int allow);
}

[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("4e8a3389-c9d8-4bd2-b6b5-124fee6cc14d")]
internal partial interface ICoreWebView2CreateCoreWebView2EnvironmentCompletedHandler
{
    void Invoke(int errorCode, ICoreWebView2Environment10? createdEnvironment);
}

[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("6c4819f3-c9b7-4260-8127-c9f5bde7f68c")]
internal partial interface ICoreWebView2CreateCoreWebView2ControllerCompletedHandler
{
    void Invoke(int errorCode, ICoreWebView2Controller? createdController);
}

[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("d33a35bf-1c49-4f98-93ab-006e0533fe1c")]
internal partial interface ICoreWebView2NavigationCompletedEventHandler
{
    void Invoke(nint sender, nint args);
}

[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("ab00b74c-15f1-4646-80e8-e76341d25d71")]
internal partial interface ICoreWebView2WebResourceRequestedEventHandler
{
    void Invoke(nint sender, ICoreWebView2WebResourceRequestedEventArgs args);
}

[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("fa504257-a216-4911-a860-fe8825712861")]
internal partial interface ICoreWebView2BrowserProcessExitedEventHandler
{
    void Invoke(nint sender, nint args);
}

[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("e9710a06-1d1d-49b2-8234-226f35846ae5")]
internal partial interface ICoreWebView2ClearBrowsingDataCompletedHandler
{
    void Invoke(int errorCode);
}

[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("df35055d-772e-4dbe-b743-5fbf74a2b258")]
internal partial interface ICoreWebView2ProfileDeletedEventHandler
{
    void Invoke(nint sender, nint args);
}

[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("38274481-a15c-4563-94cf-990edc9aeb95")]
internal partial interface ICoreWebView2GetNonDefaultPermissionSettingsCompletedHandler
{
    void Invoke(int errorCode, ICoreWebView2PermissionSettingCollectionView? result);
}

[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("f5596f62-3de5-47b1-91e8-a4104b596b96")]
internal partial interface ICoreWebView2PermissionSettingCollectionView
{
    ICoreWebView2PermissionSetting GetValueAtIndex(uint index);
    uint GetCount();
}

[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("792b6eca-5576-421c-9119-74ebb3a4ffb3")]
internal partial interface ICoreWebView2PermissionSetting
{
    int GetPermissionKind();
    string GetPermissionOrigin();
    int GetPermissionState();
}

[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("5c4889f0-5ef6-4c5a-952c-d8f1b92d0574")]
internal partial interface ICoreWebView2CallDevToolsProtocolMethodCompletedHandler
{
    void Invoke(int errorCode, string? returnObjectAsJson);
}

[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("b32ca51a-8371-45e9-9317-af021d080367")]
internal partial interface ICoreWebView2DevToolsProtocolEventReceiver
{
    EventRegistrationToken AddDevToolsProtocolEventReceived(ICoreWebView2DevToolsProtocolEventReceivedEventHandler eventHandler);
    void RemoveDevToolsProtocolEventReceived(EventRegistrationToken token);
}

[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("e2fda4be-5456-406c-a261-3d452138362c")]
internal partial interface ICoreWebView2DevToolsProtocolEventReceivedEventHandler
{
    void Invoke(nint sender, ICoreWebView2DevToolsProtocolEventReceivedEventArgs2 args);
}

[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("2dc4959d-1494-4393-95ba-bea4cb9ebd1b")]
internal partial interface ICoreWebView2DevToolsProtocolEventReceivedEventArgs2
{
    string GetParameterObjectAsJson();
    string GetSessionId();
}

[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("15e1c6a3-c72a-4df3-91d7-d097fbec6bfd")]
internal partial interface ICoreWebView2PermissionRequestedEventHandler
{
    void Invoke(nint sender, ICoreWebView2PermissionRequestedEventArgs3 args);
}

[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("e61670bc-3dce-4177-86d2-c629ae3cb6ac")]
internal partial interface ICoreWebView2PermissionRequestedEventArgs3
{
    string GetUri();
    int GetPermissionKind();
    int GetIsUserInitiated();
    int GetState();
    void PutState(int state);
    [PreserveSig] int Slot08GetDeferral();
    int GetHandled();
    void PutHandled(int value);
    int GetSavesInProfile();
    void PutSavesInProfile(int value);
}

[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("d4c185fe-c81c-4989-97af-2d3fa7ab5651")]
internal partial interface ICoreWebView2NewWindowRequestedEventHandler
{
    void Invoke(nint sender, ICoreWebView2NewWindowRequestedEventArgs args);
}

[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("34acb11c-fc37-4418-9132-f9c21d1eafb9")]
internal partial interface ICoreWebView2NewWindowRequestedEventArgs
{
    string GetUri();
    [PreserveSig] int Slot04PutNewWindow();
    [PreserveSig] int Slot05GetNewWindow();
    void PutHandled(int handled);
    int GetHandled();
    int GetIsUserInitiated();
}

// ICoreWebView2Environment10, flat: Environment 3-7, _2 8, _3 9-10, _4 11, _5 12-13, _6 14, _7 15, _8 16-18, _9 19,
// _10 20-22.
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("ee0eb9df-6f12-46ce-b53f-3f47b9c928e0")]
internal partial interface ICoreWebView2Environment10
{
    void CreateCoreWebView2Controller(nint parentWindow, ICoreWebView2CreateCoreWebView2ControllerCompletedHandler handler);

    // content is an IStream* (may be null); the response takes its own reference.
    ICoreWebView2WebResourceResponse CreateWebResourceResponse(nint content, int statusCode, string reasonPhrase, string headers);

    string GetBrowserVersionString();
    [PreserveSig] int Slot06AddNewBrowserVersionAvailable();
    [PreserveSig] int Slot07RemoveNewBrowserVersionAvailable();
    [PreserveSig] int Slot08CreateWebResourceRequest();
    [PreserveSig] int Slot09CreateCompositionController();
    [PreserveSig] int Slot10CreatePointerInfo();
    [PreserveSig] int Slot11GetAutomationProviderForWindow();
    EventRegistrationToken AddBrowserProcessExited(ICoreWebView2BrowserProcessExitedEventHandler eventHandler);
    void RemoveBrowserProcessExited(EventRegistrationToken token);
    [PreserveSig] int Slot14CreatePrintSettings();
    string GetUserDataFolder();
    [PreserveSig] int Slot16AddProcessInfosChanged();
    [PreserveSig] int Slot17RemoveProcessInfosChanged();
    [PreserveSig] int Slot18GetProcessInfos();
    [PreserveSig] int Slot19CreateContextMenuItem();
    ICoreWebView2ControllerOptions CreateCoreWebView2ControllerOptions();
    void CreateCoreWebView2ControllerWithOptions(nint parentWindow, ICoreWebView2ControllerOptions options, ICoreWebView2CreateCoreWebView2ControllerCompletedHandler handler);
}

[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("12aae616-8ccb-44ec-bcb3-eb1831881635")]
internal partial interface ICoreWebView2ControllerOptions
{
    string GetProfileName();
    void PutProfileName(string value);
    int GetIsInPrivateModeEnabled();
    void PutIsInPrivateModeEnabled(int value);
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
    [PreserveSig] int Close();
    ICoreWebView2_22 GetCoreWebView2();
}

// ICoreWebView2Settings8, flat: Settings 3-20, _2 21-22, _3 23-24, _4 25-28, _5 29-30, _6 31-32, _7 33-34, _8 35-36.
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("9e6b0e8f-86ad-4e81-8147-a9b5edb68650")]
internal partial interface ICoreWebView2Settings8
{
    [PreserveSig] int Slot03();
    [PreserveSig] int Slot04();
    [PreserveSig] int Slot05();
    [PreserveSig] int Slot06();
    [PreserveSig] int Slot07();
    void PutAreDefaultScriptDialogsEnabled(int enabled);
    [PreserveSig] int Slot09();
    [PreserveSig] int Slot10();
    int GetAreDevToolsEnabled();
    void PutAreDevToolsEnabled(int enabled);
    [PreserveSig] int Slot13();
    [PreserveSig] int Slot14();
    [PreserveSig] int Slot15();
    [PreserveSig] int Slot16();
    [PreserveSig] int Slot17();
    [PreserveSig] int Slot18();
    [PreserveSig] int Slot19();
    [PreserveSig] int Slot20();
    [PreserveSig] int Slot21();
    [PreserveSig] int Slot22();
    [PreserveSig] int Slot23();
    [PreserveSig] int Slot24();
    [PreserveSig] int Slot25();
    [PreserveSig] int Slot26();
    [PreserveSig] int Slot27();
    [PreserveSig] int Slot28();
    [PreserveSig] int Slot29();
    [PreserveSig] int Slot30();
    [PreserveSig] int Slot31();
    [PreserveSig] int Slot32();
    [PreserveSig] int Slot33();
    [PreserveSig] int Slot34();
    int GetIsReputationCheckingRequired();
    void PutIsReputationCheckingRequired(int value);
}

// ICoreWebView2_22, flat (tools/gen-webview.mjs).
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("db75dfc7-a857-4632-a398-6969dde26c0a")]
internal partial interface ICoreWebView2_22
{
    // 3 ICoreWebView2::get_Settings
    ICoreWebView2Settings8 GetSettings(); [PreserveSig] int S004GetSource();
    // 5 ICoreWebView2::Navigate
    void Navigate(string uri); [PreserveSig] int S006NavigateToString(); [PreserveSig] int S007AddNavigationStarting(); [PreserveSig] int S008RemoveNavigationStarting(); [PreserveSig] int S009AddContentLoading(); [PreserveSig] int S010RemoveContentLoading(); [PreserveSig] int S011AddSourceChanged(); [PreserveSig] int S012RemoveSourceChanged(); [PreserveSig] int S013AddHistoryChanged(); [PreserveSig] int S014RemoveHistoryChanged();
    // 15 ICoreWebView2::add_NavigationCompleted
    EventRegistrationToken AddNavigationCompleted(ICoreWebView2NavigationCompletedEventHandler eventHandler);
    // 16 ICoreWebView2::remove_NavigationCompleted
    void RemoveNavigationCompleted(EventRegistrationToken token); [PreserveSig] int S017AddFrameNavigationStarting(); [PreserveSig] int S018RemoveFrameNavigationStarting(); [PreserveSig] int S019AddFrameNavigationCompleted(); [PreserveSig] int S020RemoveFrameNavigationCompleted(); [PreserveSig] int S021AddScriptDialogOpening(); [PreserveSig] int S022RemoveScriptDialogOpening();
    // 23 ICoreWebView2::add_PermissionRequested
    EventRegistrationToken AddPermissionRequested(ICoreWebView2PermissionRequestedEventHandler eventHandler); [PreserveSig] int S024RemovePermissionRequested(); [PreserveSig] int S025AddProcessFailed(); [PreserveSig] int S026RemoveProcessFailed(); [PreserveSig] int S027AddScriptToExecuteOnDocumentCreated(); [PreserveSig] int S028RemoveScriptToExecuteOnDocumentCreated(); [PreserveSig] int S029ExecuteScript(); [PreserveSig] int S030CapturePreview();
    // 31 ICoreWebView2::Reload
    void Reload(); [PreserveSig] int S032PostWebMessageAsJson(); [PreserveSig] int S033PostWebMessageAsString(); [PreserveSig] int S034AddWebMessageReceived(); [PreserveSig] int S035RemoveWebMessageReceived();
    // 36 ICoreWebView2::CallDevToolsProtocolMethod
    void CallDevToolsProtocolMethod(string methodName, string parametersAsJson, ICoreWebView2CallDevToolsProtocolMethodCompletedHandler handler);
    // 37 ICoreWebView2::get_BrowserProcessId
    uint GetBrowserProcessId(); [PreserveSig] int S038GetCanGoBack(); [PreserveSig] int S039GetCanGoForward(); [PreserveSig] int S040GoBack(); [PreserveSig] int S041GoForward();
    // 42 ICoreWebView2::GetDevToolsProtocolEventReceiver
    ICoreWebView2DevToolsProtocolEventReceiver GetDevToolsProtocolEventReceiver(string eventName); [PreserveSig] int S043Stop();
    // 44 ICoreWebView2::add_NewWindowRequested
    EventRegistrationToken AddNewWindowRequested(ICoreWebView2NewWindowRequestedEventHandler eventHandler); [PreserveSig] int S045RemoveNewWindowRequested(); [PreserveSig] int S046AddDocumentTitleChanged(); [PreserveSig] int S047RemoveDocumentTitleChanged(); [PreserveSig] int S048GetDocumentTitle(); [PreserveSig] int S049AddHostObjectToScript(); [PreserveSig] int S050RemoveHostObjectFromScript(); [PreserveSig] int S051OpenDevToolsWindow(); [PreserveSig] int S052AddContainsFullScreenElementChanged(); [PreserveSig] int S053RemoveContainsFullScreenElementChanged(); [PreserveSig] int S054GetContainsFullScreenElement();
    // 55 ICoreWebView2::add_WebResourceRequested
    EventRegistrationToken AddWebResourceRequested(ICoreWebView2WebResourceRequestedEventHandler eventHandler);
    // 56 ICoreWebView2::remove_WebResourceRequested
    void RemoveWebResourceRequested(EventRegistrationToken token); [PreserveSig] int S057AddWebResourceRequestedFilter(); [PreserveSig] int S058RemoveWebResourceRequestedFilter(); [PreserveSig] int S059AddWindowCloseRequested(); [PreserveSig] int S060RemoveWindowCloseRequested(); [PreserveSig] int S061AddWebResourceResponseReceived(); [PreserveSig] int S062RemoveWebResourceResponseReceived(); [PreserveSig] int S063NavigateWithWebResourceRequest(); [PreserveSig] int S064AddDOMContentLoaded(); [PreserveSig] int S065RemoveDOMContentLoaded(); [PreserveSig] int S066GetCookieManager(); [PreserveSig] int S067GetEnvironment(); [PreserveSig] int S068TrySuspend(); [PreserveSig] int S069Resume(); [PreserveSig] int S070GetIsSuspended(); [PreserveSig] int S071SetVirtualHostNameToFolderMapping(); [PreserveSig] int S072ClearVirtualHostNameToFolderMapping(); [PreserveSig] int S073AddFrameCreated(); [PreserveSig] int S074RemoveFrameCreated(); [PreserveSig] int S075AddDownloadStarting(); [PreserveSig] int S076RemoveDownloadStarting(); [PreserveSig] int S077AddClientCertificateRequested(); [PreserveSig] int S078RemoveClientCertificateRequested(); [PreserveSig] int S079OpenTaskManagerWindow(); [PreserveSig] int S080PrintToPdf(); [PreserveSig] int S081AddIsMutedChanged(); [PreserveSig] int S082RemoveIsMutedChanged(); [PreserveSig] int S083GetIsMuted(); [PreserveSig] int S084PutIsMuted(); [PreserveSig] int S085AddIsDocumentPlayingAudioChanged(); [PreserveSig] int S086RemoveIsDocumentPlayingAudioChanged(); [PreserveSig] int S087GetIsDocumentPlayingAudio(); [PreserveSig] int S088AddIsDefaultDownloadDialogOpenChanged(); [PreserveSig] int S089RemoveIsDefaultDownloadDialogOpenChanged(); [PreserveSig] int S090GetIsDefaultDownloadDialogOpen(); [PreserveSig] int S091OpenDefaultDownloadDialog(); [PreserveSig] int S092CloseDefaultDownloadDialog(); [PreserveSig] int S093GetDefaultDownloadDialogCornerAlignment(); [PreserveSig] int S094PutDefaultDownloadDialogCornerAlignment(); [PreserveSig] int S095GetDefaultDownloadDialogMargin(); [PreserveSig] int S096PutDefaultDownloadDialogMargin(); [PreserveSig] int S097AddBasicAuthenticationRequested(); [PreserveSig] int S098RemoveBasicAuthenticationRequested();
    // 99 ICoreWebView2_11::CallDevToolsProtocolMethodForSession
    void CallDevToolsProtocolMethodForSession(string sessionId, string methodName, string parametersAsJson, ICoreWebView2CallDevToolsProtocolMethodCompletedHandler handler); [PreserveSig] int S100AddContextMenuRequested(); [PreserveSig] int S101RemoveContextMenuRequested(); [PreserveSig] int S102AddStatusBarTextChanged(); [PreserveSig] int S103RemoveStatusBarTextChanged(); [PreserveSig] int S104GetStatusBarText();
    // 105 ICoreWebView2_13::get_Profile
    ICoreWebView2Profile8 GetProfile(); [PreserveSig] int S106AddServerCertificateErrorDetected(); [PreserveSig] int S107RemoveServerCertificateErrorDetected(); [PreserveSig] int S108ClearServerCertificateErrorActions(); [PreserveSig] int S109AddFaviconChanged(); [PreserveSig] int S110RemoveFaviconChanged(); [PreserveSig] int S111GetFaviconUri(); [PreserveSig] int S112GetFavicon(); [PreserveSig] int S113Print(); [PreserveSig] int S114ShowPrintUI(); [PreserveSig] int S115PrintToPdfStream(); [PreserveSig] int S116PostSharedBufferToScript(); [PreserveSig] int S117AddLaunchingExternalUriScheme(); [PreserveSig] int S118RemoveLaunchingExternalUriScheme(); [PreserveSig] int S119GetMemoryUsageTargetLevel(); [PreserveSig] int S120PutMemoryUsageTargetLevel(); [PreserveSig] int S121GetFrameId(); [PreserveSig] int S122ExecuteScriptWithResult();
    // 123 ICoreWebView2_22::AddWebResourceRequestedFilterWithRequestSourceKinds
    void AddWebResourceRequestedFilterWithRequestSourceKinds(string uri, int resourceContext, int requestSourceKinds); [PreserveSig] int S124RemoveWebResourceRequestedFilterWithRequestSourceKinds();
}

// ICoreWebView2Profile8, flat: Profile 3-9, _2 10-12, _3 13-14, _4 15-16, _5 17, _6 18-21, _7 22-23, _8 24-26.
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("fbf70c2f-eb1f-4383-85a0-163e92044011")]
internal partial interface ICoreWebView2Profile8
{
    string GetProfileName();
    int GetIsInPrivateModeEnabled();
    string GetProfilePath();
    [PreserveSig] int Slot06GetDefaultDownloadFolderPath();
    [PreserveSig] int Slot07PutDefaultDownloadFolderPath();
    [PreserveSig] int Slot08GetPreferredColorScheme();
    [PreserveSig] int Slot09PutPreferredColorScheme();
    [PreserveSig] int Slot10ClearBrowsingData();
    [PreserveSig] int Slot11ClearBrowsingDataInTimeRange();
    void ClearBrowsingDataAll(ICoreWebView2ClearBrowsingDataCompletedHandler handler);
    [PreserveSig] int Slot13GetPreferredTrackingPreventionLevel();
    [PreserveSig] int Slot14PutPreferredTrackingPreventionLevel();
    [PreserveSig] int Slot15SetPermissionState();
    void GetNonDefaultPermissionSettings(ICoreWebView2GetNonDefaultPermissionSettingsCompletedHandler handler);
    [PreserveSig] int Slot17GetCookieManager();
    [PreserveSig] int Slot18GetIsPasswordAutosaveEnabled();
    [PreserveSig] int Slot19PutIsPasswordAutosaveEnabled();
    [PreserveSig] int Slot20GetIsGeneralAutofillEnabled();
    [PreserveSig] int Slot21PutIsGeneralAutofillEnabled();
    [PreserveSig] int Slot22AddBrowserExtension();
    [PreserveSig] int Slot23GetBrowserExtensions();
    void Delete();
    EventRegistrationToken AddDeleted(ICoreWebView2ProfileDeletedEventHandler eventHandler);
    void RemoveDeleted(EventRegistrationToken token);
}

[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("453e667f-12c7-49d4-be6d-ddbe7956f57a")]
internal partial interface ICoreWebView2WebResourceRequestedEventArgs
{
    ICoreWebView2WebResourceRequest GetRequest();
    [PreserveSig] int Slot04GetResponse();
    void PutResponse(ICoreWebView2WebResourceResponse response);
    [PreserveSig] int Slot06GetDeferral();
    int GetResourceContext();
}

[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("97055cd4-512c-4264-8b5f-e3f446cea6a5")]
internal partial interface ICoreWebView2WebResourceRequest
{
    string GetUri();
}

[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("aafcc94f-fa27-48fd-97df-830ef75aaec9")]
internal partial interface ICoreWebView2WebResourceResponse;
