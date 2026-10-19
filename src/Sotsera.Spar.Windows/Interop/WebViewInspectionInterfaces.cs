#if DEBUG
// ABI projections from Microsoft.Web.WebView2 1.0.4258.31, build/native/include/WebView2.h.
// See third-party/webview2/LICENSE.txt and docs/09-native-host.md for provenance.
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Sotsera.Spar.Windows.Interop;

[GeneratedComInterface(Options = ComInterfaceOptions.ManagedObjectWrapper)]
[Guid("49511172-cc67-4bca-9923-137112f4c4cc")]
internal partial interface IScriptCompleted
{
    [PreserveSig] int Invoke(int error, nint result);
}

[GeneratedComInterface(Options = ComInterfaceOptions.ManagedObjectWrapper)]
[Guid("5c4889f0-5ef6-4c5a-952c-d8f1b92d0574")]
internal partial interface IProtocolCompleted
{
    [PreserveSig] int Invoke(int error, nint result);
}

[GeneratedComInterface(Options = ComInterfaceOptions.ComObjectWrapper)]
[Guid("b32ca51a-8371-45e9-9317-af021d080367")]
internal partial interface IProtocolReceiver
{
    [PreserveSig] int AddReceived(nint handler, out long token);
    [PreserveSig] int RemoveReceived(long token);
}

[GeneratedComInterface(Options = ComInterfaceOptions.ComObjectWrapper)]
[Guid("653c2959-bb3a-4377-8632-b58ada4e66c4")]
internal partial interface IProtocolEventArgs
{
    [PreserveSig] int GetJson(out nint json);
}

[GeneratedComInterface(Options = ComInterfaceOptions.ManagedObjectWrapper)]
[Guid("e2fda4be-5456-406c-a261-3d452138362c")]
internal partial interface IProtocolReceived
{
    [PreserveSig] int Invoke(nint sender, nint args);
}

[GeneratedComClass]
internal sealed partial class ScriptCompleted(Action<int, nint> callback) : IScriptCompleted
{
    public int Invoke(int error, nint result)
    {
        callback(error, result);
        return 0;
    }
}

[GeneratedComClass]
internal sealed partial class ProtocolCompleted(Action<int, nint> callback) : IProtocolCompleted
{
    public int Invoke(int error, nint result)
    {
        callback(error, result);
        return 0;
    }
}

[GeneratedComClass]
internal sealed partial class ProtocolReceived(Action<nint> callback) : IProtocolReceived
{
    public int Invoke(nint sender, nint args)
    {
        callback(args);
        return 0;
    }
}
#endif
