using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Spar.Windows.Interop;

// What page security needs from WebView2.h (plan section 3b): new-window requests, permission requests and the script
// added to every document, hand-declared as the other files of this folder (see WebView2.Environment.cs for the rules).

/// <summary><c>ICoreWebView2NewWindowRequestedEventArgs</c> (slots 3–10): the host always handles it, so no window opens.</summary>
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16, Options = ComInterfaceOptions.ComObjectWrapper)]
[Guid("34acb11c-fc37-4418-9132-f9c21d1eafb9")]
internal partial interface ICoreWebView2NewWindowRequestedEventArgs
{
    string GetUri();
    [PreserveSig] int Slot004_PutNewWindow();
    [PreserveSig] int Slot005_GetNewWindow();
    void PutHandled(int handled);
    [PreserveSig] int Slot007_GetHandled();
    int GetIsUserInitiated();
}

/// <summary><c>ICoreWebView2PermissionRequestedEventArgs</c>, the base interface (slots 3–8), for a Runtime without _3.</summary>
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16, Options = ComInterfaceOptions.ComObjectWrapper)]
[Guid("973ae2ef-ff18-4894-8fb2-3c758f046810")]
internal partial interface ICoreWebView2PermissionRequestedEventArgs
{
    string GetUri();
    int GetPermissionKind();
    int GetIsUserInitiated();
    [PreserveSig] int Slot006_GetState();
    void PutState(int state);
}

/// <summary>
/// <c>ICoreWebView2PermissionRequestedEventArgs3</c>, flat: the base 3–8, _2 9–10, _3 11–12. <c>SavesInProfile</c>
/// defaults to true, which would let a remembered answer start the camera with no event (pitfalls, "Page security").
/// </summary>
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16, Options = ComInterfaceOptions.ComObjectWrapper)]
[Guid("e61670bc-3dce-4177-86d2-c629ae3cb6ac")]
internal partial interface ICoreWebView2PermissionRequestedEventArgs3
{
    // ICoreWebView2PermissionRequestedEventArgs
    string GetUri();
    int GetPermissionKind();
    int GetIsUserInitiated();
    [PreserveSig] int Slot006_GetState();
    void PutState(int state);
    [PreserveSig] int Slot008_GetDeferral();
    // ICoreWebView2PermissionRequestedEventArgs2
    [PreserveSig] int Slot009_GetHandled();
    void PutHandled(int handled);
    // ICoreWebView2PermissionRequestedEventArgs3
    [PreserveSig] int Slot011_GetSavesInProfile();
    void PutSavesInProfile(int value);
}

[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("d4c185fe-c81c-4989-97af-2d3fa7ab5651")]
internal partial interface ICoreWebView2NewWindowRequestedEventHandler
{
    void Invoke(nint sender, nint args);
}

[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("15e1c6a3-c72a-4df3-91d7-d097fbec6bfd")]
internal partial interface ICoreWebView2PermissionRequestedEventHandler
{
    void Invoke(nint sender, nint args);
}

[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("b99369f3-9b11-47b5-bc6f-8e7895fcea17")]
internal partial interface ICoreWebView2AddScriptToExecuteOnDocumentCreatedCompletedHandler
{
    void Invoke(int errorCode, string? id);
}

/// <summary>Completes when the script is added to every future document, or fails with its HRESULT.</summary>
[GeneratedComClass]
internal sealed partial class ScriptAdded : ICoreWebView2AddScriptToExecuteOnDocumentCreatedCompletedHandler
{
    private readonly TaskCompletionSource<string?> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task<string?> Task => _completion.Task;

    public void Invoke(int errorCode, string? id)
    {
        if (errorCode < 0)
        {
            _completion.TrySetException(new WebView2Exception("AddScriptToExecuteOnDocumentCreated", errorCode));
            return;
        }

        _completion.TrySetResult(id);
    }
}

/// <summary>
/// Reads a request body through the <c>IStream</c> WebView2 hands out, without declaring the interface: only
/// <c>ISequentialStream::Read</c> (vtable slot 3) is called, at most <paramref name="limit"/> plus one bytes.
/// </summary>
internal static unsafe class RequestBody
{
    /// <summary>The body, or null when it has none or is longer than <paramref name="limit"/>; releases the stream.</summary>
    public static byte[]? Read(nint stream, int limit)
    {
        if (stream == 0)
        {
            return null;
        }

        try
        {
            var buffer = new byte[limit + 1];
            var total = 0;
            var vtable = *(nint**)stream;
            var read = (delegate* unmanaged[Stdcall]<nint, byte*, uint, uint*, int>)vtable[3];
            fixed (byte* start = buffer)
            {
                while (total < buffer.Length)
                {
                    uint count = 0;
                    var result = read(stream, start + total, (uint)(buffer.Length - total), &count);
                    if (result < 0 || count == 0)
                    {
                        break;
                    }

                    total += (int)count;
                }
            }

            return total > limit ? null : buffer[..total];
        }
        finally
        {
            Marshal.Release(stream);
        }
    }
}
