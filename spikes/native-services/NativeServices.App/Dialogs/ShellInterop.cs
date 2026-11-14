#if PART_DIALOGS || PART_COMBASELINE
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace NativeServices.App.Dialogs;

// Hand-written from the Windows SDK 10.0.26100.0 headers (um/ShObjIdl_core.h, shared/ShTypes.h, um/OleIdl.h).
// Slots that this spike never calls are [PreserveSig] int placeholders that keep the offsets of the used methods; they
// are never invoked. Slot numbers count from 0 (IUnknown is 0-2). Only an interface's own trailing slots may be
// omitted, and only when nothing derives from it: a base interface must declare every slot or derived methods shift.
// Ownership: every method that hands out an interface returns a raw nint holding one reference that the caller owns;
// ShellCom.Wrap turns it into a unique managed wrapper that the caller releases with ComObject.FinalRelease.

[StructLayout(LayoutKind.Sequential)]
internal struct ComdlgFilterSpec
{
    public nint Name; // LPCWSTR, borrowed for the duration of SetFileTypes
    public nint Spec; // LPCWSTR, borrowed for the duration of SetFileTypes
}

internal static class ShellConstants
{
    public const uint ClsctxInprocServer = 0x1;

    // FILEOPENDIALOGOPTIONS (ShObjIdl_core.h)
    public const uint FosOverwritePrompt = 0x2;
    public const uint FosStrictFileTypes = 0x4;
    public const uint FosNoChangeDir = 0x8;
    public const uint FosForceFileSystem = 0x40;
    public const uint FosAllowMultiSelect = 0x200;
    public const uint FosPathMustExist = 0x800;
    public const uint FosFileMustExist = 0x1000;
    public const uint FosDontAddToRecent = 0x2000000;

    // SIGDN_FILESYSPATH (ShObjIdl_core.h)
    public const uint SigdnFileSysPath = 0x80058000;

    // FDE_OVERWRITE_RESPONSE
    public const int FdeorAccept = 1;

    public const int SOk = 0;
    public const int ENotImpl = unchecked((int)0x80004001);
    public const int EAbort = unchecked((int)0x80004004);

    // HRESULT_FROM_WIN32(ERROR_CANCELLED)
    public const int HResultCancelled = unchecked((int)0x800704C7);

    public static readonly Guid ClsidFileOpenDialog = new("DC1C5A9C-E88A-4dde-A5A1-60F82A20AEF7");
    public static readonly Guid ClsidFileSaveDialog = new("C0B4E2F3-BA21-4773-8DBA-335EC946EB8B");

    // IIDs repeated from the [Guid] attributes so no Type.GUID lookup is needed under NativeAOT.
    public static readonly Guid IidFileDialog = new("42f85136-db7e-439c-85f1-e4075d135fc8");
    public static readonly Guid IidFileOpenDialog = new("d57c7288-d4ad-4768-be02-9d969532d960");
    public static readonly Guid IidFileSaveDialog = new("84bccd23-5fde-4cdb-aea4-af64b83d78ab");
    public static readonly Guid IidShellItem = new("43826d1e-e718-42ee-bc55-a1e261c37bfe");
}

internal static partial class ShellNative
{
    // HRESULT CoCreateInstance(REFCLSID, LPUNKNOWN, DWORD, REFIID, LPVOID*). ppv holds one owned reference on success.
    [LibraryImport("ole32")]
    internal static partial int CoCreateInstance(in Guid clsid, nint outer, uint context, in Guid iid, out nint instance);

    // HRESULT SHCreateItemFromParsingName(PCWSTR, IBindCtx*, REFIID, void**). ppv holds one owned reference.
    [LibraryImport("shell32", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial int SHCreateItemFromParsingName(string path, nint bindContext, in Guid iid, out nint item);
}

[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("b4db1657-70d7-485e-8e3e-6fcb5a5c1802")]
internal partial interface IModalWindow
{
    // Returns HRESULT_FROM_WIN32(ERROR_CANCELLED) when the user cancels, or the HRESULT passed to IFileDialog::Close.
    [PreserveSig]
    int Show(nint owner);
}

[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("42f85136-db7e-439c-85f1-e4075d135fc8")]
internal unsafe partial interface IFileDialog : IModalWindow
{
    void SetFileTypes(uint count, ComdlgFilterSpec* specs);
    void SetFileTypeIndex(uint index);
    uint GetFileTypeIndex();
    uint Advise(IFileDialogEvents events);
    void Unadvise(uint cookie);
    void SetOptions(uint options);
    uint GetOptions();
    void SetDefaultFolder(IShellItem folder);
    void SetFolder(IShellItem folder);
    [PreserveSig] int Slot13GetFolder();
    [PreserveSig] int Slot14GetCurrentSelection();
    void SetFileName(string name);
    nint GetFileName(); // CoTaskMem LPWSTR, owned by the caller
    void SetTitle(string title);
    void SetOkButtonLabel(string text);
    void SetFileNameLabel(string label);
    [PreserveSig] int GetResult(out nint item);
    [PreserveSig] int Slot21AddPlace();
    void SetDefaultExtension(string extension);
    [PreserveSig] int Close(int hr);
    void SetClientGuid(in Guid guid);
    [PreserveSig] int ClearClientData(); // E_FAIL when nothing is stored for the client GUID

    // Not used, but a derived interface (IFileOpenDialog::GetResults) follows it, so the slot must exist.
    [PreserveSig] int Slot26SetFilter();
}

[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("d57c7288-d4ad-4768-be02-9d969532d960")]
internal partial interface IFileOpenDialog : IFileDialog
{
    [PreserveSig] int GetResults(out nint items);
}

[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("84bccd23-5fde-4cdb-aea4-af64b83d78ab")]
internal partial interface IFileSaveDialog : IFileDialog
{
    void SetSaveAsItem(IShellItem item);
}

[GeneratedComInterface]
[Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe")]
internal partial interface IShellItem
{
    [PreserveSig] int Slot03BindToHandler();
    [PreserveSig] int Slot04GetParent();

    // ppszName is a CoTaskMem LPWSTR owned by the caller.
    [PreserveSig]
    int GetDisplayName(uint sigdn, out nint name);
}

[GeneratedComInterface]
[Guid("b63ea76d-1f85-456f-a19c-48159efa858b")]
internal partial interface IShellItemArray
{
    [PreserveSig] int Slot03BindToHandler();
    [PreserveSig] int Slot04GetPropertyStore();
    [PreserveSig] int Slot05GetPropertyDescriptionList();
    [PreserveSig] int Slot06GetAttributes();
    uint GetCount();
    [PreserveSig] int GetItemAt(uint index, out nint item);
}

[GeneratedComInterface]
[Guid("00000114-0000-0000-C000-000000000046")]
internal partial interface IOleWindow
{
    nint GetWindow();
}

// Every event receives borrowed IFileDialog*/IShellItem* pointers; they are not wrapped, so no reference is taken.
[GeneratedComInterface]
[Guid("973510db-7d7f-452b-8975-74a85828d354")]
internal partial interface IFileDialogEvents
{
    [PreserveSig] int OnFileOk(nint dialog);
    [PreserveSig] int OnFolderChanging(nint dialog, nint folder);
    [PreserveSig] int OnFolderChange(nint dialog);
    [PreserveSig] int OnSelectionChange(nint dialog);
    [PreserveSig] int OnShareViolation(nint dialog, nint item, out int response);
    [PreserveSig] int OnTypeChange(nint dialog);
    [PreserveSig] int OnOverwrite(nint dialog, nint item, out int response);
}

internal static unsafe class ShellCom
{
    private static readonly StrategyBasedComWrappers s_wrappers = new();

    /// <summary>Creates a unique wrapper over an owned reference and transfers that reference to the wrapper.</summary>
    public static T Wrap<T>(nint owned)
        where T : class
    {
        var wrapper = (T)s_wrappers.GetOrCreateObjectForComInstance(owned, CreateObjectFlags.UniqueInstance);
        Marshal.Release(owned); // the wrapper took its own reference
        return wrapper;
    }

    /// <summary>Releases the wrapper's references now, on the calling (owning) thread, instead of at finalization.</summary>
    public static void Release(object? wrapper)
    {
        if (wrapper is ComObject comObject)
        {
            comObject.FinalRelease();
        }
    }

    public static T Create<T>(Guid clsid, Guid iid)
        where T : class
    {
        var hr = ShellNative.CoCreateInstance(clsid, 0, ShellConstants.ClsctxInprocServer, iid, out var instance);
        Marshal.ThrowExceptionForHR(hr);
        return Wrap<T>(instance);
    }

    public static IShellItem ItemFromPath(string path)
    {
        var iid = ShellConstants.IidShellItem;
        var hr = ShellNative.SHCreateItemFromParsingName(path, 0, iid, out var item);
        Marshal.ThrowExceptionForHR(hr);
        return Wrap<IShellItem>(item);
    }

    /// <summary>Size baseline: one generated COM wrapper and call, as a WebView2 host already has.</summary>
    public static bool BaselineProbe(string folder)
    {
        var iid = ShellConstants.IidShellItem;
        Marshal.ThrowExceptionForHR(ShellNative.SHCreateItemFromParsingName(folder, 0, iid, out var item));
        return FileSystemPath(item).EndsWith(Path.GetFileName(folder), StringComparison.OrdinalIgnoreCase);
    }

    public static string FileSystemPath(nint ownedItem)
    {
        var item = Wrap<IShellItem>(ownedItem);
        try
        {
            var hr = item.GetDisplayName(ShellConstants.SigdnFileSysPath, out var name);
            Marshal.ThrowExceptionForHR(hr);
            try
            {
                return new string((char*)name);
            }
            finally
            {
                Marshal.FreeCoTaskMem(name);
            }
        }
        finally
        {
            Release(item);
        }
    }
}
#endif
