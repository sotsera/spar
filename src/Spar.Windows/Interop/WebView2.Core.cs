using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Spar.Windows.Interop;

// The WebView itself, hand-declared from WebView2.h as one flat interface (see WebView2.Environment.cs for the rules).
// ICoreWebView2_22 carries the source-kinds request filter (slot 123), which also sees shared and service workers.

/// <summary><c>ICoreWebView2_22</c>, flat: every slot of ICoreWebView2 and _2 to _22, in header order.</summary>
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("db75dfc7-a857-4632-a398-6969dde26c0a")]
internal partial interface ICoreWebView2_22
{
    // ICoreWebView2
    [PreserveSig] int Slot003_GetSettings();
    [PreserveSig] int Slot004_GetSource();
    void Navigate(string uri);
    [PreserveSig] int Slot006_NavigateToString();
    EventRegistrationToken AddNavigationStarting(ICoreWebView2NavigationStartingEventHandler eventHandler);
    void RemoveNavigationStarting(EventRegistrationToken token);
    EventRegistrationToken AddContentLoading(ICoreWebView2ContentLoadingEventHandler eventHandler);
    void RemoveContentLoading(EventRegistrationToken token);
    [PreserveSig] int Slot011_AddSourceChanged();
    [PreserveSig] int Slot012_RemoveSourceChanged();
    [PreserveSig] int Slot013_AddHistoryChanged();
    [PreserveSig] int Slot014_RemoveHistoryChanged();
    EventRegistrationToken AddNavigationCompleted(ICoreWebView2NavigationCompletedEventHandler eventHandler);
    void RemoveNavigationCompleted(EventRegistrationToken token);
    [PreserveSig] int Slot017_AddFrameNavigationStarting();
    [PreserveSig] int Slot018_RemoveFrameNavigationStarting();
    [PreserveSig] int Slot019_AddFrameNavigationCompleted();
    [PreserveSig] int Slot020_RemoveFrameNavigationCompleted();
    [PreserveSig] int Slot021_AddScriptDialogOpening();
    [PreserveSig] int Slot022_RemoveScriptDialogOpening();
    [PreserveSig] int Slot023_AddPermissionRequested();
    [PreserveSig] int Slot024_RemovePermissionRequested();
    EventRegistrationToken AddProcessFailed(ICoreWebView2ProcessFailedEventHandler eventHandler);
    void RemoveProcessFailed(EventRegistrationToken token);
    [PreserveSig] int Slot027_AddScriptToExecuteOnDocumentCreated();
    [PreserveSig] int Slot028_RemoveScriptToExecuteOnDocumentCreated();
    [PreserveSig] int Slot029_ExecuteScript();
    [PreserveSig] int Slot030_CapturePreview();
    void Reload();
    [PreserveSig] int Slot032_PostWebMessageAsJson();
    void PostWebMessageAsString(string webMessageAsString);
    EventRegistrationToken AddWebMessageReceived(ICoreWebView2WebMessageReceivedEventHandler handler);
    void RemoveWebMessageReceived(EventRegistrationToken token);
    [PreserveSig] int Slot036_CallDevToolsProtocolMethod();
    uint GetBrowserProcessId();
    [PreserveSig] int Slot038_GetCanGoBack();
    [PreserveSig] int Slot039_GetCanGoForward();
    [PreserveSig] int Slot040_GoBack();
    [PreserveSig] int Slot041_GoForward();
    [PreserveSig] int Slot042_GetDevToolsProtocolEventReceiver();
    [PreserveSig] int Slot043_Stop();
    [PreserveSig] int Slot044_AddNewWindowRequested();
    [PreserveSig] int Slot045_RemoveNewWindowRequested();
    [PreserveSig] int Slot046_AddDocumentTitleChanged();
    [PreserveSig] int Slot047_RemoveDocumentTitleChanged();
    [PreserveSig] int Slot048_GetDocumentTitle();
    [PreserveSig] int Slot049_AddHostObjectToScript();
    [PreserveSig] int Slot050_RemoveHostObjectFromScript();
    [PreserveSig] int Slot051_OpenDevToolsWindow();
    [PreserveSig] int Slot052_AddContainsFullScreenElementChanged();
    [PreserveSig] int Slot053_RemoveContainsFullScreenElementChanged();
    [PreserveSig] int Slot054_GetContainsFullScreenElement();
    EventRegistrationToken AddWebResourceRequested(ICoreWebView2WebResourceRequestedEventHandler eventHandler);
    void RemoveWebResourceRequested(EventRegistrationToken token);
    [PreserveSig] int Slot057_AddWebResourceRequestedFilter();
    [PreserveSig] int Slot058_RemoveWebResourceRequestedFilter();
    [PreserveSig] int Slot059_AddWindowCloseRequested();
    [PreserveSig] int Slot060_RemoveWindowCloseRequested();
    // ICoreWebView2_2
    [PreserveSig] int Slot061_AddWebResourceResponseReceived();
    [PreserveSig] int Slot062_RemoveWebResourceResponseReceived();
    [PreserveSig] int Slot063_NavigateWithWebResourceRequest();
    [PreserveSig] int Slot064_AddDOMContentLoaded();
    [PreserveSig] int Slot065_RemoveDOMContentLoaded();
    [PreserveSig] int Slot066_GetCookieManager();
    [PreserveSig] int Slot067_GetEnvironment();
    // ICoreWebView2_3
    [PreserveSig] int Slot068_TrySuspend();
    [PreserveSig] int Slot069_Resume();
    [PreserveSig] int Slot070_GetIsSuspended();
    [PreserveSig] int Slot071_SetVirtualHostNameToFolderMapping();
    [PreserveSig] int Slot072_ClearVirtualHostNameToFolderMapping();
    // ICoreWebView2_4
    [PreserveSig] int Slot073_AddFrameCreated();
    [PreserveSig] int Slot074_RemoveFrameCreated();
    [PreserveSig] int Slot075_AddDownloadStarting();
    [PreserveSig] int Slot076_RemoveDownloadStarting();
    // ICoreWebView2_5
    [PreserveSig] int Slot077_AddClientCertificateRequested();
    [PreserveSig] int Slot078_RemoveClientCertificateRequested();
    // ICoreWebView2_6
    [PreserveSig] int Slot079_OpenTaskManagerWindow();
    // ICoreWebView2_7
    [PreserveSig] int Slot080_PrintToPdf();
    // ICoreWebView2_8
    [PreserveSig] int Slot081_AddIsMutedChanged();
    [PreserveSig] int Slot082_RemoveIsMutedChanged();
    [PreserveSig] int Slot083_GetIsMuted();
    [PreserveSig] int Slot084_PutIsMuted();
    [PreserveSig] int Slot085_AddIsDocumentPlayingAudioChanged();
    [PreserveSig] int Slot086_RemoveIsDocumentPlayingAudioChanged();
    [PreserveSig] int Slot087_GetIsDocumentPlayingAudio();
    // ICoreWebView2_9
    [PreserveSig] int Slot088_AddIsDefaultDownloadDialogOpenChanged();
    [PreserveSig] int Slot089_RemoveIsDefaultDownloadDialogOpenChanged();
    [PreserveSig] int Slot090_GetIsDefaultDownloadDialogOpen();
    [PreserveSig] int Slot091_OpenDefaultDownloadDialog();
    [PreserveSig] int Slot092_CloseDefaultDownloadDialog();
    [PreserveSig] int Slot093_GetDefaultDownloadDialogCornerAlignment();
    [PreserveSig] int Slot094_PutDefaultDownloadDialogCornerAlignment();
    [PreserveSig] int Slot095_GetDefaultDownloadDialogMargin();
    [PreserveSig] int Slot096_PutDefaultDownloadDialogMargin();
    // ICoreWebView2_10
    [PreserveSig] int Slot097_AddBasicAuthenticationRequested();
    [PreserveSig] int Slot098_RemoveBasicAuthenticationRequested();
    // ICoreWebView2_11
    [PreserveSig] int Slot099_CallDevToolsProtocolMethodForSession();
    [PreserveSig] int Slot100_AddContextMenuRequested();
    [PreserveSig] int Slot101_RemoveContextMenuRequested();
    // ICoreWebView2_12
    [PreserveSig] int Slot102_AddStatusBarTextChanged();
    [PreserveSig] int Slot103_RemoveStatusBarTextChanged();
    [PreserveSig] int Slot104_GetStatusBarText();
    // ICoreWebView2_13
    nint GetProfile();
    // ICoreWebView2_14
    [PreserveSig] int Slot106_AddServerCertificateErrorDetected();
    [PreserveSig] int Slot107_RemoveServerCertificateErrorDetected();
    [PreserveSig] int Slot108_ClearServerCertificateErrorActions();
    // ICoreWebView2_15
    [PreserveSig] int Slot109_AddFaviconChanged();
    [PreserveSig] int Slot110_RemoveFaviconChanged();
    [PreserveSig] int Slot111_GetFaviconUri();
    [PreserveSig] int Slot112_GetFavicon();
    // ICoreWebView2_16
    [PreserveSig] int Slot113_Print();
    [PreserveSig] int Slot114_ShowPrintUI();
    [PreserveSig] int Slot115_PrintToPdfStream();
    // ICoreWebView2_17
    [PreserveSig] int Slot116_PostSharedBufferToScript();
    // ICoreWebView2_18
    [PreserveSig] int Slot117_AddLaunchingExternalUriScheme();
    [PreserveSig] int Slot118_RemoveLaunchingExternalUriScheme();
    // ICoreWebView2_19
    [PreserveSig] int Slot119_GetMemoryUsageTargetLevel();
    [PreserveSig] int Slot120_PutMemoryUsageTargetLevel();
    // ICoreWebView2_20
    [PreserveSig] int Slot121_GetFrameId();
    // ICoreWebView2_21
    [PreserveSig] int Slot122_ExecuteScriptWithResult();
    // ICoreWebView2_22
    void AddWebResourceRequestedFilterWithRequestSourceKinds(string uri, int resourceContext, int requestSourceKinds);
    [PreserveSig] int Slot124_RemoveWebResourceRequestedFilterWithRequestSourceKinds();
}
