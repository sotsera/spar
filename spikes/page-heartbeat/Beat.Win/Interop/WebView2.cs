using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Spar.PageHeartbeat.Interop;

// Hand-written from WebView2.h (Microsoft.Web.WebView2 1.0.4314.35, C vtable structs), slots counted with
// tools/vt.mjs, IUnknown included. Controller, settings, the resource-request interfaces, Environment8 and the
// process-info interfaces are copied from spikes/resource-usage; ICoreWebView2 is extended here with the navigation,
// ProcessFailed, script and message slots (7, 9, 15, 25, 27, 29, 31, 32, 33, 43). Slots never called are
// [PreserveSig] int placeholders that keep the offsets of the used methods. ICoreWebView2_3 (TrySuspend 68, Resume 69,
// IsSuspended 70) and ICoreWebView2_19 (MemoryUsageTargetLevel 119/120) are called through raw vtables (WebView2Raw).

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
    void Invoke(int errorCode, nint createdEnvironment);
}

[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("6c4819f3-c9b7-4260-8127-c9f5bde7f68c")]
internal partial interface ICoreWebView2CreateCoreWebView2ControllerCompletedHandler
{
    void Invoke(int errorCode, ICoreWebView2Controller? createdController);
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
[Guid("0f99a40c-e962-4207-9e92-e3d542eff849")]
internal partial interface ICoreWebView2WebMessageReceivedEventArgs
{
    string GetSource();
    string GetWebMessageAsJson();
    string TryGetWebMessageAsString();
}

[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("fa504257-a216-4911-a860-fe8825712861")]
internal partial interface ICoreWebView2BrowserProcessExitedEventHandler
{
    void Invoke(nint sender, nint args);
}

[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("f4af0c39-44b9-40e9-8b11-0484cfb9e0a1")]
internal partial interface ICoreWebView2ProcessInfosChangedEventHandler
{
    void Invoke(nint sender, nint args);
}

[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("9adbe429-f36d-432b-9ddc-f8881fbd76e3")]
internal partial interface ICoreWebView2NavigationStartingEventHandler
{
    void Invoke(nint sender, ICoreWebView2NavigationStartingEventArgs args);
}

[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("5b495469-e119-438a-9b18-7604f25f2e49")]
internal partial interface ICoreWebView2NavigationStartingEventArgs
{
    string GetUri();
    [PreserveSig] int Slot04GetIsUserInitiated();
    [PreserveSig] int Slot05GetIsRedirected();
    [PreserveSig] int Slot06GetRequestHeaders();
    [PreserveSig] int Slot07GetCancel();
    [PreserveSig] int Slot08PutCancel();
    ulong GetNavigationId();
}

[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("364471e7-f2be-4910-bdba-d72077d51c4b")]
internal partial interface ICoreWebView2ContentLoadingEventHandler
{
    void Invoke(nint sender, ICoreWebView2ContentLoadingEventArgs args);
}

[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("0c8a1275-9b6b-4901-87ad-70df25bafa6e")]
internal partial interface ICoreWebView2ContentLoadingEventArgs
{
    int GetIsErrorPage();
    ulong GetNavigationId();
}

[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("d33a35bf-1c49-4f98-93ab-006e0533fe1c")]
internal partial interface ICoreWebView2NavigationCompletedEventHandler
{
    void Invoke(nint sender, ICoreWebView2NavigationCompletedEventArgs args);
}

[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("30d68b7d-20d9-4752-a9ca-ec8448fbb5c1")]
internal partial interface ICoreWebView2NavigationCompletedEventArgs
{
    int GetIsSuccess();
    int GetWebErrorStatus();
    ulong GetNavigationId();
}

// The args arrive as ICoreWebView2ProcessFailedEventArgs*; the host wraps the borrowed pointer and casts to Args2.
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("79e0aea4-990b-42d9-aa1d-0fcc2e5bc7f1")]
internal partial interface ICoreWebView2ProcessFailedEventHandler
{
    void Invoke(nint sender, nint args);
}

[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("4dab9422-46fa-4c3e-a5d2-41d2071d3680")]
internal partial interface ICoreWebView2ProcessFailedEventArgs2
{
    int GetProcessFailedKind();
    int GetReason();
    int GetExitCode();
    string GetProcessDescription();
}

[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("b99369f3-9b11-47b5-bc6f-8e7895fcea17")]
internal partial interface ICoreWebView2AddScriptToExecuteOnDocumentCreatedCompletedHandler
{
    void Invoke(int errorCode, nint id);
}

[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("49511172-cc67-4bca-9923-137112f4c4cc")]
internal partial interface ICoreWebView2ExecuteScriptCompletedHandler
{
    void Invoke(int errorCode, nint result);
}

[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("00f206a7-9d17-4605-91f6-4e8e4de192e3")]
internal partial interface ICoreWebView2TrySuspendCompletedHandler
{
    void Invoke(int errorCode, int result);
}

[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("402b99cd-a0cc-4fa5-b7a5-51d86a1d2339")]
internal partial interface ICoreWebView2ProcessInfoCollection
{
    [PreserveSig] int GetCount(out uint count);
    [PreserveSig] int GetValueAtIndex(uint index, out ICoreWebView2ProcessInfo? value);
}

[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("84fa7612-3f3d-4fbf-889d-fad000492d72")]
internal partial interface ICoreWebView2ProcessInfo
{
    [PreserveSig] int GetProcessId(out int id);
    [PreserveSig] int GetKind(out int kind);
}

// ICoreWebView2Environment8, flat: Environment 3-7, _2 8, _3 9-10, _4 11, _5 12-13, _6 14, _7 15, _8 16-18.
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("d6eb91dd-c3d2-45e5-bd29-6dc2bc4de9cf")]
internal partial interface ICoreWebView2Environment8
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
    EventRegistrationToken AddProcessInfosChanged(ICoreWebView2ProcessInfosChangedEventHandler eventHandler);
    void RemoveProcessInfosChanged(EventRegistrationToken token);
    [PreserveSig] int GetProcessInfos(out ICoreWebView2ProcessInfoCollection? value);
}

[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("4d00c0d1-9434-4eb6-8078-8697a560334f")]
internal partial interface ICoreWebView2Controller
{
    [PreserveSig] int GetIsVisible(out int isVisible);
    [PreserveSig] int PutIsVisible(int isVisible);
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
    [PreserveSig] int NotifyParentWindowPositionChanged();
    [PreserveSig] int Close();
    ICoreWebView2 GetCoreWebView2();
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
    [PreserveSig] int Slot11();
    [PreserveSig] int Slot12();
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

[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("76eceacb-0462-4d94-ac83-423a6793775e")]
internal partial interface ICoreWebView2
{
    ICoreWebView2Settings8 GetSettings();
    [PreserveSig] int Slot04GetSource();
    [PreserveSig] int Navigate(string uri);
    [PreserveSig] int Slot06NavigateToString();
    EventRegistrationToken AddNavigationStarting(ICoreWebView2NavigationStartingEventHandler handler);
    [PreserveSig] int Slot08RemoveNavigationStarting();
    EventRegistrationToken AddContentLoading(ICoreWebView2ContentLoadingEventHandler handler);
    [PreserveSig] int Slot10RemoveContentLoading();
    [PreserveSig] int Slot11AddSourceChanged();
    [PreserveSig] int Slot12RemoveSourceChanged();
    [PreserveSig] int Slot13AddHistoryChanged();
    [PreserveSig] int Slot14RemoveHistoryChanged();
    EventRegistrationToken AddNavigationCompleted(ICoreWebView2NavigationCompletedEventHandler handler);
    [PreserveSig] int Slot16RemoveNavigationCompleted();
    [PreserveSig] int Slot17AddFrameNavigationStarting();
    [PreserveSig] int Slot18RemoveFrameNavigationStarting();
    [PreserveSig] int Slot19AddFrameNavigationCompleted();
    [PreserveSig] int Slot20RemoveFrameNavigationCompleted();
    [PreserveSig] int Slot21AddScriptDialogOpening();
    [PreserveSig] int Slot22RemoveScriptDialogOpening();
    [PreserveSig] int Slot23AddPermissionRequested();
    [PreserveSig] int Slot24RemovePermissionRequested();
    EventRegistrationToken AddProcessFailed(ICoreWebView2ProcessFailedEventHandler handler);
    [PreserveSig] int Slot26RemoveProcessFailed();
    void AddScriptToExecuteOnDocumentCreated(string javaScript, ICoreWebView2AddScriptToExecuteOnDocumentCreatedCompletedHandler handler);
    [PreserveSig] int Slot28RemoveScriptToExecuteOnDocumentCreated();
    [PreserveSig] int ExecuteScript(string javaScript, ICoreWebView2ExecuteScriptCompletedHandler handler);
    [PreserveSig] int Slot30CapturePreview();
    [PreserveSig] int Reload();
    [PreserveSig] int PostWebMessageAsJson(string webMessageAsJson);
    [PreserveSig] int PostWebMessageAsString(string webMessageAsString);
    EventRegistrationToken AddWebMessageReceived(ICoreWebView2WebMessageReceivedEventHandler handler);
    void RemoveWebMessageReceived(EventRegistrationToken token);
    [PreserveSig] int Slot36CallDevToolsProtocolMethod();
    uint GetBrowserProcessId();
    [PreserveSig] int Slot38GetCanGoBack();
    [PreserveSig] int Slot39GetCanGoForward();
    [PreserveSig] int Slot40GoBack();
    [PreserveSig] int Slot41GoForward();
    [PreserveSig] int Slot42GetDevToolsProtocolEventReceiver();
    [PreserveSig] int Stop();
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
    [PreserveSig] int Slot06GetDeferral();
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
}

[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("aafcc94f-fa27-48fd-97df-830ef75aaec9")]
internal partial interface ICoreWebView2WebResourceResponse;

// ICoreWebView2_22, declared flat: AddWebResourceRequestedFilterWithRequestSourceKinds is slot 123.
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("db75dfc7-a857-4632-a398-6969dde26c0a")]
internal partial interface ICoreWebView2_22
{
    [PreserveSig] int S003(); [PreserveSig] int S004(); [PreserveSig] int S005(); [PreserveSig] int S006(); [PreserveSig] int S007();
    [PreserveSig] int S008(); [PreserveSig] int S009(); [PreserveSig] int S010(); [PreserveSig] int S011(); [PreserveSig] int S012();
    [PreserveSig] int S013(); [PreserveSig] int S014(); [PreserveSig] int S015(); [PreserveSig] int S016(); [PreserveSig] int S017();
    [PreserveSig] int S018(); [PreserveSig] int S019(); [PreserveSig] int S020(); [PreserveSig] int S021(); [PreserveSig] int S022();
    [PreserveSig] int S023(); [PreserveSig] int S024(); [PreserveSig] int S025(); [PreserveSig] int S026(); [PreserveSig] int S027();
    [PreserveSig] int S028(); [PreserveSig] int S029(); [PreserveSig] int S030(); [PreserveSig] int S031(); [PreserveSig] int S032();
    [PreserveSig] int S033(); [PreserveSig] int S034(); [PreserveSig] int S035(); [PreserveSig] int S036(); [PreserveSig] int S037();
    [PreserveSig] int S038(); [PreserveSig] int S039(); [PreserveSig] int S040(); [PreserveSig] int S041(); [PreserveSig] int S042();
    [PreserveSig] int S043(); [PreserveSig] int S044(); [PreserveSig] int S045(); [PreserveSig] int S046(); [PreserveSig] int S047();
    [PreserveSig] int S048(); [PreserveSig] int S049(); [PreserveSig] int S050(); [PreserveSig] int S051(); [PreserveSig] int S052();
    [PreserveSig] int S053(); [PreserveSig] int S054(); [PreserveSig] int S055(); [PreserveSig] int S056(); [PreserveSig] int S057();
    [PreserveSig] int S058(); [PreserveSig] int S059(); [PreserveSig] int S060(); [PreserveSig] int S061(); [PreserveSig] int S062();
    [PreserveSig] int S063(); [PreserveSig] int S064(); [PreserveSig] int S065(); [PreserveSig] int S066(); [PreserveSig] int S067();
    [PreserveSig] int S068(); [PreserveSig] int S069(); [PreserveSig] int S070(); [PreserveSig] int S071(); [PreserveSig] int S072();
    [PreserveSig] int S073(); [PreserveSig] int S074(); [PreserveSig] int S075(); [PreserveSig] int S076(); [PreserveSig] int S077();
    [PreserveSig] int S078(); [PreserveSig] int S079(); [PreserveSig] int S080(); [PreserveSig] int S081(); [PreserveSig] int S082();
    [PreserveSig] int S083(); [PreserveSig] int S084(); [PreserveSig] int S085(); [PreserveSig] int S086(); [PreserveSig] int S087();
    [PreserveSig] int S088(); [PreserveSig] int S089(); [PreserveSig] int S090(); [PreserveSig] int S091(); [PreserveSig] int S092();
    [PreserveSig] int S093(); [PreserveSig] int S094(); [PreserveSig] int S095(); [PreserveSig] int S096(); [PreserveSig] int S097();
    [PreserveSig] int S098(); [PreserveSig] int S099(); [PreserveSig] int S100(); [PreserveSig] int S101(); [PreserveSig] int S102();
    [PreserveSig] int S103(); [PreserveSig] int S104(); [PreserveSig] int S105(); [PreserveSig] int S106(); [PreserveSig] int S107();
    [PreserveSig] int S108(); [PreserveSig] int S109(); [PreserveSig] int S110(); [PreserveSig] int S111(); [PreserveSig] int S112();
    [PreserveSig] int S113(); [PreserveSig] int S114(); [PreserveSig] int S115(); [PreserveSig] int S116(); [PreserveSig] int S117();
    [PreserveSig] int S118(); [PreserveSig] int S119(); [PreserveSig] int S120(); [PreserveSig] int S121(); [PreserveSig] int S122();
    void AddWebResourceRequestedFilterWithRequestSourceKinds(string uri, int resourceContext, int requestSourceKinds);
}

/// <summary>Newer interfaces called through raw vtables (pattern from spikes/theme-startup).</summary>
internal static unsafe class WebView2Raw
{
    public static readonly Guid IidWebView2_3 = new("a0d6df20-3b92-416d-aa0c-437a9c727857");
    public static readonly Guid IidWebView2_19 = new("6921f954-79b0-437f-a997-c85811897c68");
    public static readonly Guid IidTrySuspendHandler = new("00f206a7-9d17-4605-91f6-4e8e4de192e3");

    private static void* Slot(nint self, int index) => (*(void***)self)[index];

    /// <summary>An owned pointer to <paramref name="iid"/> on a source-generated COM object, or 0 if unsupported.</summary>
    public static nint Query<T>(T instance, in Guid iid)
    {
        var unknown = (nint)ComInterfaceMarshaller<T>.ConvertToUnmanaged(instance);
        try
        {
            return Marshal.QueryInterface(unknown, in iid, out var result) >= 0 ? result : 0;
        }
        finally
        {
            Marshal.Release(unknown);
        }
    }

    // ICoreWebView2_3: 68 TrySuspend, 69 Resume, 70 get_IsSuspended.
    public static int TrySuspend(nint webView3, ICoreWebView2TrySuspendCompletedHandler handler)
    {
        var callback = (nint)ComInterfaceMarshaller<ICoreWebView2TrySuspendCompletedHandler>.ConvertToUnmanaged(handler);
        try
        {
            return ((delegate* unmanaged[Stdcall]<nint, nint, int>)Slot(webView3, 68))(webView3, callback);
        }
        finally
        {
            Marshal.Release(callback);
        }
    }

    public static int Resume(nint webView3) => ((delegate* unmanaged[Stdcall]<nint, int>)Slot(webView3, 69))(webView3);

    public static int IsSuspended(nint webView3, out int suspended)
    {
        int value;
        var hr = ((delegate* unmanaged[Stdcall]<nint, int*, int>)Slot(webView3, 70))(webView3, &value);
        suspended = value;
        return hr;
    }

    // ICoreWebView2_19: 119 get_MemoryUsageTargetLevel, 120 put_MemoryUsageTargetLevel (0 normal, 1 low).
    public static int GetMemoryUsageTargetLevel(nint webView19, out int level)
    {
        int value;
        var hr = ((delegate* unmanaged[Stdcall]<nint, int*, int>)Slot(webView19, 119))(webView19, &value);
        level = value;
        return hr;
    }

    public static int PutMemoryUsageTargetLevel(nint webView19, int level) =>
        ((delegate* unmanaged[Stdcall]<nint, int, int>)Slot(webView19, 120))(webView19, level);
}
