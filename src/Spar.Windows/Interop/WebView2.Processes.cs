using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Spar.Windows.Interop;

// Which browser processes serve which frames, hand-declared from WebView2.h (see WebView2.Environment.cs for the rules):
// ICoreWebView2Environment13's GetProcessExtendedInfos lists each process with the frames it renders, so the renderer
// of one WebView can be told from the renderers of others on the same environment (window tests share one environment
// per test process). Runtime 121 and later, below Spar's minimum.

/// <summary><c>ICoreWebView2Environment13</c>, flat: as <see cref="ICoreWebView2Environment10"/>, then _11 23, _12 24, _13 25.</summary>
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16, Options = ComInterfaceOptions.ComObjectWrapper)]
[Guid("af641f58-72b2-11ee-b962-0242ac120002")]
internal partial interface ICoreWebView2Environment13
{
    // ICoreWebView2Environment
    [PreserveSig] int Slot003_CreateCoreWebView2Controller();
    [PreserveSig] int Slot004_CreateWebResourceResponse();
    [PreserveSig] int Slot005_GetBrowserVersionString();
    [PreserveSig] int Slot006_AddNewBrowserVersionAvailable();
    [PreserveSig] int Slot007_RemoveNewBrowserVersionAvailable();
    // ICoreWebView2Environment2
    [PreserveSig] int Slot008_CreateWebResourceRequest();
    // ICoreWebView2Environment3
    [PreserveSig] int Slot009_CreateCoreWebView2CompositionController();
    [PreserveSig] int Slot010_CreateCoreWebView2PointerInfo();
    // ICoreWebView2Environment4
    [PreserveSig] int Slot011_GetAutomationProviderForWindow();
    // ICoreWebView2Environment5
    [PreserveSig] int Slot012_AddBrowserProcessExited();
    [PreserveSig] int Slot013_RemoveBrowserProcessExited();
    // ICoreWebView2Environment6
    [PreserveSig] int Slot014_CreatePrintSettings();
    // ICoreWebView2Environment7
    [PreserveSig] int Slot015_GetUserDataFolder();
    // ICoreWebView2Environment8
    [PreserveSig] int Slot016_AddProcessInfosChanged();
    [PreserveSig] int Slot017_RemoveProcessInfosChanged();
    [PreserveSig] int Slot018_GetProcessInfos();
    // ICoreWebView2Environment9
    [PreserveSig] int Slot019_CreateContextMenuItem();
    // ICoreWebView2Environment10
    [PreserveSig] int Slot020_CreateCoreWebView2ControllerOptions();
    [PreserveSig] int Slot021_CreateCoreWebView2ControllerWithOptions();
    [PreserveSig] int Slot022_CreateCoreWebView2CompositionControllerWithOptions();
    // ICoreWebView2Environment11
    [PreserveSig] int Slot023_GetFailureReportFolderPath();
    // ICoreWebView2Environment12
    [PreserveSig] int Slot024_CreateSharedBuffer();
    // ICoreWebView2Environment13
    void GetProcessExtendedInfos(ICoreWebView2GetProcessExtendedInfosCompletedHandler handler);
}

[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("f45e55aa-3bc2-11ee-be56-0242ac120002")]
internal partial interface ICoreWebView2GetProcessExtendedInfosCompletedHandler
{
    /// <summary><paramref name="result"/> is borrowed: the handler reads it before it returns.</summary>
    void Invoke(int errorCode, nint result);
}

/// <summary><c>ICoreWebView2ProcessExtendedInfoCollection</c> (slots 3–4).</summary>
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16, Options = ComInterfaceOptions.ComObjectWrapper)]
[Guid("32efa696-407a-11ee-be56-0242ac120002")]
internal partial interface ICoreWebView2ProcessExtendedInfoCollection
{
    uint GetCount();
    nint GetValueAtIndex(uint index);
}

/// <summary><c>ICoreWebView2ProcessExtendedInfo</c> (slots 3–4): a process and the frames it renders.</summary>
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16, Options = ComInterfaceOptions.ComObjectWrapper)]
[Guid("af4c4c2e-45db-11ee-be56-0242ac120002")]
internal partial interface ICoreWebView2ProcessExtendedInfo
{
    nint GetProcessInfo();
    nint GetAssociatedFrameInfos();
}

/// <summary><c>ICoreWebView2FrameInfoCollection</c> (slot 3).</summary>
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16, Options = ComInterfaceOptions.ComObjectWrapper)]
[Guid("8f834154-d38e-4d90-affb-6800a7272839")]
internal partial interface ICoreWebView2FrameInfoCollection
{
    nint GetIterator();
}

/// <summary><c>ICoreWebView2FrameInfoCollectionIterator</c> (slots 3–5).</summary>
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16, Options = ComInterfaceOptions.ComObjectWrapper)]
[Guid("1bf89e2d-1b2b-4629-b28f-05099b41bb03")]
internal partial interface ICoreWebView2FrameInfoCollectionIterator
{
    int GetHasCurrent();
    nint GetCurrent();
    int MoveNext();
}

/// <summary><c>ICoreWebView2FrameInfo2</c>, flat: FrameInfo 3–4, _2 5–7.</summary>
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16, Options = ComInterfaceOptions.ComObjectWrapper)]
[Guid("56f85cfa-72c4-11ee-b962-0242ac120002")]
internal partial interface ICoreWebView2FrameInfo2
{
    // ICoreWebView2FrameInfo
    [PreserveSig] int Slot003_GetName();
    [PreserveSig] int Slot004_GetSource();
    // ICoreWebView2FrameInfo2
    [PreserveSig] int Slot005_GetParentFrameInfo();
    uint GetFrameId();
    [PreserveSig] int Slot007_GetFrameKind();
}

/// <summary>
/// Completes with the ids of the renderer processes that render the frame <c>frameId</c> (a WebView's main frame, from
/// <c>ICoreWebView2_20.get_FrameId</c>). Reads the collection on the UI thread, inside the callback, where it is valid.
/// </summary>
[GeneratedComClass]
internal sealed partial class FrameProcessesCompleted(uint frameId) : ICoreWebView2GetProcessExtendedInfosCompletedHandler
{
    private readonly TaskCompletionSource<IReadOnlyList<int>> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task<IReadOnlyList<int>> Task => _completion.Task;

    public void Invoke(int errorCode, nint result)
    {
        try
        {
            if (errorCode < 0)
            {
                _completion.TrySetException(new WebView2Exception("GetProcessExtendedInfos", errorCode));
                return;
            }

            _completion.TrySetResult(Read(result));
        }
        catch (Exception exception)
        {
            _completion.TrySetException(exception);
        }
    }

    private List<int> Read(nint result)
    {
        var found = new List<int>();
        var processes = Com.Borrow<ICoreWebView2ProcessExtendedInfoCollection>(result);
        try
        {
            var count = processes.GetCount();
            for (uint index = 0; index < count; index++)
            {
                var process = Com.Take<ICoreWebView2ProcessExtendedInfo>(processes.GetValueAtIndex(index));
                try
                {
                    if (RendersFrame(process))
                    {
                        found.Add(ProcessId(process));
                    }
                }
                finally
                {
                    Com.Release(process);
                }
            }
        }
        finally
        {
            Com.Release(processes);
        }

        return found;
    }

    private bool RendersFrame(ICoreWebView2ProcessExtendedInfo process)
    {
        var frames = Com.Take<ICoreWebView2FrameInfoCollection>(process.GetAssociatedFrameInfos());
        ICoreWebView2FrameInfoCollectionIterator? iterator = null;
        try
        {
            iterator = Com.Take<ICoreWebView2FrameInfoCollectionIterator>(frames.GetIterator());
            while (iterator.GetHasCurrent() != 0)
            {
                var frame = Com.Take<ICoreWebView2FrameInfo2>(iterator.GetCurrent());
                try
                {
                    if (frame.GetFrameId() == frameId)
                    {
                        return true;
                    }
                }
                finally
                {
                    Com.Release(frame);
                }

                iterator.MoveNext();
            }

            return false;
        }
        finally
        {
            Com.Release(iterator);
            Com.Release(frames);
        }
    }

    private static int ProcessId(ICoreWebView2ProcessExtendedInfo process)
    {
        var info = Com.Take<ICoreWebView2ProcessInfo>(process.GetProcessInfo());
        try
        {
            return info.GetProcessId();
        }
        finally
        {
            Com.Release(info);
        }
    }
}
