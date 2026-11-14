using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Spar.DesktopOrigin.Interop;

// Hand-written from WebView2.h (Microsoft.Web.WebView2 1.0.4314.35, C vtable structs). Copied from spikes/asset-packing.
// Slots this spike never calls are [PreserveSig] int placeholders that keep the offsets of the used methods.
// Placeholders are never invoked.

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
        nint environmentOptions,
        ICoreWebView2CreateCoreWebView2EnvironmentCompletedHandler environmentCreatedHandler);

    // Returns an IStream* with one reference owned by the caller, or null on failure.
    [LibraryImport("shlwapi")]
    internal static unsafe partial nint SHCreateMemStream(byte* initial, uint size);
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
    void Invoke(nint sender, nint args);
}

[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("ab00b74c-15f1-4646-80e8-e76341d25d71")]
internal partial interface ICoreWebView2WebResourceRequestedEventHandler
{
    void Invoke(nint sender, ICoreWebView2WebResourceRequestedEventArgs args);
}

[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("57213f19-00e6-49fa-8e07-898ea01ecbd2")]
internal partial interface ICoreWebView2WebMessageReceivedEventHandler
{
    void Invoke(nint sender, ICoreWebView2WebMessageReceivedEventArgs args);
}

[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("b96d755e-0319-4e92-a296-23436f46a1fc")]
internal partial interface ICoreWebView2Environment
{
    void CreateCoreWebView2Controller(nint parentWindow, ICoreWebView2CreateCoreWebView2ControllerCompletedHandler handler);

    // content is an IStream* (may be null); the response takes its own reference.
    ICoreWebView2WebResourceResponse CreateWebResourceResponse(nint content, int statusCode, string reasonPhrase, string headers);

    string GetBrowserVersionString();
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
    void PutAreDefaultScriptDialogsEnabled(int enabled);
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
    void Navigate(string uri);
    [PreserveSig] int Slot06NavigateToString();
    EventRegistrationToken AddNavigationStarting(ICoreWebView2NavigationStartingEventHandler eventHandler);
    void RemoveNavigationStarting(EventRegistrationToken token);
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
    EventRegistrationToken AddPermissionRequested(ICoreWebView2PermissionRequestedEventHandler eventHandler);
    void RemovePermissionRequested(EventRegistrationToken token);
    [PreserveSig] int Slot25AddProcessFailed();
    [PreserveSig] int Slot26RemoveProcessFailed();
    [PreserveSig] int Slot27AddScriptToExecuteOnDocumentCreated();
    [PreserveSig] int Slot28RemoveScriptToExecuteOnDocumentCreated();
    [PreserveSig] int Slot29ExecuteScript();
    [PreserveSig] int Slot30CapturePreview();
    void Reload();
    [PreserveSig] int Slot32PostWebMessageAsJson();
    void PostWebMessageAsString(string webMessageAsString);
    EventRegistrationToken AddWebMessageReceived(ICoreWebView2WebMessageReceivedEventHandler handler);
    void RemoveWebMessageReceived(EventRegistrationToken token);
    void CallDevToolsProtocolMethod(string methodName, string parametersAsJson, ICoreWebView2CallDevToolsProtocolMethodCompletedHandler handler);
    uint GetBrowserProcessId();
    [PreserveSig] int Slot38GetCanGoBack();
    [PreserveSig] int Slot39GetCanGoForward();
    [PreserveSig] int Slot40GoBack();
    [PreserveSig] int Slot41GoForward();
    [PreserveSig] int Slot42GetDevToolsProtocolEventReceiver();
    [PreserveSig] int Slot43Stop();
    [PreserveSig] int Slot44AddNewWindowRequested();
    [PreserveSig] int Slot45RemoveNewWindowRequested();
    [PreserveSig] int Slot46AddDocumentTitleChanged();
    [PreserveSig] int Slot47RemoveDocumentTitleChanged();
    [PreserveSig] int Slot48GetDocumentTitle();
    [PreserveSig] int Slot49AddHostObjectToScript();
    [PreserveSig] int Slot50RemoveHostObjectFromScript();
    [PreserveSig] int Slot51OpenDevToolsWindow();
    [PreserveSig] int Slot52AddContainsFullScreenElementChanged();
    [PreserveSig] int Slot53RemoveContainsFullScreenElementChanged();
    [PreserveSig] int Slot54GetContainsFullScreenElement();
    EventRegistrationToken AddWebResourceRequested(ICoreWebView2WebResourceRequestedEventHandler eventHandler);
    void RemoveWebResourceRequested(EventRegistrationToken token);
    void AddWebResourceRequestedFilter(string uri, int resourceContext);
}

[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("453e667f-12c7-49d4-be6d-ddbe7956f57a")]
internal partial interface ICoreWebView2WebResourceRequestedEventArgs
{
    ICoreWebView2WebResourceRequest GetRequest();
    [PreserveSig] int Slot04GetResponse();
    void PutResponse(ICoreWebView2WebResourceResponse response);
    ICoreWebView2Deferral GetDeferral();
    int GetResourceContext();
}

[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("97055cd4-512c-4264-8b5f-e3f446cea6a5")]
internal partial interface ICoreWebView2WebResourceRequest
{
    string GetUri();
    [PreserveSig] int Slot04PutUri();
    string GetMethod();
    [PreserveSig] int Slot06PutMethod();
    ISequentialStream? GetContent();
    [PreserveSig] int Slot08PutContent();
    ICoreWebView2HttpRequestHeaders GetHeaders();
}

[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("e86cac0e-5523-465c-b536-8fb9fc8c8c60")]
internal partial interface ICoreWebView2HttpRequestHeaders
{
    string GetHeader(string name);
    [PreserveSig] int Slot04GetHeaders();
    int Contains(string name);
}

[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("aafcc94f-fa27-48fd-97df-830ef75aaec9")]
internal partial interface ICoreWebView2WebResourceResponse;

[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("c10e7f7b-b585-46f0-a623-8befbf3e4ee0")]
internal partial interface ICoreWebView2Deferral
{
    void Complete();
}

[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("0f99a40c-e962-4207-9e92-e3d542eff849")]
internal partial interface ICoreWebView2WebMessageReceivedEventArgs
{
    string GetSource();
    [PreserveSig] int Slot04GetWebMessageAsJson();
    [PreserveSig] int TryGetWebMessageAsString(out string? message);
}

[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("9adbe429-f36d-432b-9ddc-f8881fbd76e3")]
internal partial interface ICoreWebView2NavigationStartingEventHandler
{
    void Invoke(nint sender, nint args);
}

// objidl.h. ISequentialStream reads a request body; IStream is implemented by the host to stream a response.
// Pointer out-parameters stay nint: callers may pass null for pcbRead / pcbWritten / plibNewPosition.
[GeneratedComInterface]
[Guid("0c733a30-2a1c-11ce-ade5-00aa0044773d")]
internal partial interface ISequentialStream
{
    [PreserveSig] int Read(nint buffer, uint size, nint read);
    [PreserveSig] int Write(nint buffer, uint size, nint written);
}

[GeneratedComInterface]
[Guid("0000000c-0000-0000-c000-000000000046")]
internal partial interface IStream : ISequentialStream
{
    [PreserveSig] int Seek(long move, uint origin, nint newPosition);
    [PreserveSig] int SetSize(ulong size);
    [PreserveSig] int CopyTo(nint stream, ulong count, nint read, nint written);
    [PreserveSig] int Commit(uint flags);
    [PreserveSig] int Revert();
    [PreserveSig] int LockRegion(ulong offset, ulong count, uint type);
    [PreserveSig] int UnlockRegion(ulong offset, ulong count, uint type);
    [PreserveSig] int Stat(nint statstg, uint flags);
    [PreserveSig] int Clone(nint stream);
}

// ICoreWebView2_22, declared flat: the C vtable (ICoreWebView2 through ICoreWebView2_21, then _22) puts
// AddWebResourceRequestedFilterWithRequestSourceKinds at slot 123 (0-based, IUnknown included). Slots 3-122 are placeholders.
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("db75dfc7-a857-4632-a398-6969dde26c0a")]
internal partial interface ICoreWebView2_22
{
    [PreserveSig] int Slot003();
    [PreserveSig] int Slot004();
    [PreserveSig] int Slot005();
    [PreserveSig] int Slot006();
    [PreserveSig] int Slot007();
    [PreserveSig] int Slot008();
    [PreserveSig] int Slot009();
    [PreserveSig] int Slot010();
    [PreserveSig] int Slot011();
    [PreserveSig] int Slot012();
    [PreserveSig] int Slot013();
    [PreserveSig] int Slot014();
    [PreserveSig] int Slot015();
    [PreserveSig] int Slot016();
    [PreserveSig] int Slot017();
    [PreserveSig] int Slot018();
    [PreserveSig] int Slot019();
    [PreserveSig] int Slot020();
    [PreserveSig] int Slot021();
    [PreserveSig] int Slot022();
    [PreserveSig] int Slot023();
    [PreserveSig] int Slot024();
    [PreserveSig] int Slot025();
    [PreserveSig] int Slot026();
    [PreserveSig] int Slot027();
    [PreserveSig] int Slot028();
    [PreserveSig] int Slot029();
    [PreserveSig] int Slot030();
    [PreserveSig] int Slot031();
    [PreserveSig] int Slot032();
    [PreserveSig] int Slot033();
    [PreserveSig] int Slot034();
    [PreserveSig] int Slot035();
    [PreserveSig] int Slot036();
    [PreserveSig] int Slot037();
    [PreserveSig] int Slot038();
    [PreserveSig] int Slot039();
    [PreserveSig] int Slot040();
    [PreserveSig] int Slot041();
    [PreserveSig] int Slot042();
    [PreserveSig] int Slot043();
    [PreserveSig] int Slot044();
    [PreserveSig] int Slot045();
    [PreserveSig] int Slot046();
    [PreserveSig] int Slot047();
    [PreserveSig] int Slot048();
    [PreserveSig] int Slot049();
    [PreserveSig] int Slot050();
    [PreserveSig] int Slot051();
    [PreserveSig] int Slot052();
    [PreserveSig] int Slot053();
    [PreserveSig] int Slot054();
    [PreserveSig] int Slot055();
    [PreserveSig] int Slot056();
    [PreserveSig] int Slot057();
    [PreserveSig] int Slot058();
    [PreserveSig] int Slot059();
    [PreserveSig] int Slot060();
    [PreserveSig] int Slot061();
    [PreserveSig] int Slot062();
    [PreserveSig] int Slot063();
    [PreserveSig] int Slot064();
    [PreserveSig] int Slot065();
    [PreserveSig] int Slot066();
    [PreserveSig] int Slot067();
    [PreserveSig] int Slot068();
    [PreserveSig] int Slot069();
    [PreserveSig] int Slot070();
    [PreserveSig] int Slot071();
    [PreserveSig] int Slot072();
    [PreserveSig] int Slot073();
    [PreserveSig] int Slot074();
    [PreserveSig] int Slot075();
    [PreserveSig] int Slot076();
    [PreserveSig] int Slot077();
    [PreserveSig] int Slot078();
    [PreserveSig] int Slot079();
    [PreserveSig] int Slot080();
    [PreserveSig] int Slot081();
    [PreserveSig] int Slot082();
    [PreserveSig] int Slot083();
    [PreserveSig] int Slot084();
    [PreserveSig] int Slot085();
    [PreserveSig] int Slot086();
    [PreserveSig] int Slot087();
    [PreserveSig] int Slot088();
    [PreserveSig] int Slot089();
    [PreserveSig] int Slot090();
    [PreserveSig] int Slot091();
    [PreserveSig] int Slot092();
    [PreserveSig] int Slot093();
    [PreserveSig] int Slot094();
    [PreserveSig] int Slot095();
    [PreserveSig] int Slot096();
    [PreserveSig] int Slot097();
    [PreserveSig] int Slot098();
    [PreserveSig] int Slot099();
    [PreserveSig] int Slot100();
    [PreserveSig] int Slot101();
    [PreserveSig] int Slot102();
    [PreserveSig] int Slot103();
    [PreserveSig] int Slot104();
    [PreserveSig] int Slot105();
    [PreserveSig] int Slot106();
    [PreserveSig] int Slot107();
    [PreserveSig] int Slot108();
    [PreserveSig] int Slot109();
    [PreserveSig] int Slot110();
    [PreserveSig] int Slot111();
    [PreserveSig] int Slot112();
    [PreserveSig] int Slot113();
    [PreserveSig] int Slot114();
    [PreserveSig] int Slot115();
    [PreserveSig] int Slot116();
    [PreserveSig] int Slot117();
    [PreserveSig] int Slot118();
    [PreserveSig] int Slot119();
    [PreserveSig] int Slot120();
    [PreserveSig] int Slot121();
    [PreserveSig] int Slot122();
    void AddWebResourceRequestedFilterWithRequestSourceKinds(string uri, int resourceContext, int requestSourceKinds);
}

[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("9c562c24-b219-4d7f-92f6-b187fbbadd56")]
internal partial interface ICoreWebView2WebResourceRequestedEventArgs2
{
    ICoreWebView2WebResourceRequest GetRequest();
    [PreserveSig] int Slot04GetResponse();
    [PreserveSig] int Slot05PutResponse();
    [PreserveSig] int Slot06GetDeferral();
    int GetResourceContext();
    int GetRequestedSourceKind();
}

[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("15e1c6a3-c72a-4df3-91d7-d097fbec6bfd")]
internal partial interface ICoreWebView2PermissionRequestedEventHandler
{
    void Invoke(nint sender, ICoreWebView2PermissionRequestedEventArgs3 args);
}

// ICoreWebView2PermissionRequestedEventArgs3, flat: base slots 3-8, _2 slots 9-10, _3 slots 11-12.
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("e61670bc-3dce-4177-86d2-c629ae3cb6ac")]
internal partial interface ICoreWebView2PermissionRequestedEventArgs3
{
    string GetUri();
    int GetPermissionKind();
    int GetIsUserInitiated();
    int GetState();
    void PutState(int state);
    ICoreWebView2Deferral GetDeferral();
    int GetHandled();
    void PutHandled(int handled);
    int GetSavesInProfile();
    void PutSavesInProfile(int saves);
}
