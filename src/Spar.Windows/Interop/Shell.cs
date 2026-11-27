using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Spar.Windows.Interop;

// The common file dialogs, declared flat from the Windows SDK's um/ShObjIdl_core.h, as the WebView2 interfaces are:
// each interface lists every slot from 3, its bases' included, so the slot-order test reads it as the header does.
// Slots Spar never calls are [PreserveSig] int SlotNNN_Name() placeholders. Every method that hands out an interface
// returns a raw pointer holding one reference the caller owns (Com.Take).

/// <summary><c>COMDLG_FILTERSPEC</c>: two borrowed strings, valid while <c>SetFileTypes</c> runs.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct FilterSpec
{
    public nint Name;
    public nint Spec;
}

internal static class Shell
{
    public const uint ClsctxInprocServer = 0x1;

    // FILEOPENDIALOGOPTIONS.
    public const uint OverwritePrompt = 0x2;
    public const uint NoChangeDir = 0x8;
    public const uint PickFolders = 0x20;
    public const uint ForceFileSystem = 0x40;
    public const uint AllowMultiSelect = 0x200;
    public const uint PathMustExist = 0x800;
    public const uint FileMustExist = 0x1000;

    /// <summary><c>SIGDN_FILESYSPATH</c>.</summary>
    public const uint FileSystemPath = 0x80058000;

    /// <summary><c>HRESULT_FROM_WIN32(ERROR_CANCELLED)</c>: the user closed the dialog, or the host did.</summary>
    public const int Cancelled = unchecked((int)0x800704C7);

    public static readonly Guid FileOpenDialog = new("DC1C5A9C-E88A-4dde-A5A1-60F82A20AEF7");
    public static readonly Guid FileSaveDialog = new("C0B4E2F3-BA21-4773-8DBA-335EC946EB8B");

    // The IIDs of the [Guid] attributes below, passed explicitly so nothing looks a type's GUID up at run time.
    public static readonly Guid IidFileDialog = new("42f85136-db7e-439c-85f1-e4075d135fc8");
    public static readonly Guid IidFileOpenDialog = new("d57c7288-d4ad-4768-be02-9d969532d960");
}

internal static partial class ShellNative
{
    /// <summary><c>CoCreateInstance</c>; the instance holds one reference the caller owns.</summary>
    [LibraryImport("ole32")]
    internal static partial int CoCreateInstance(in Guid clsid, nint outer, uint context, in Guid iid, out nint instance);
}

/// <summary>
/// <c>IFileDialog</c>, flat: <c>IModalWindow</c>'s <c>Show</c> (3), then its own 4–26. The open and save dialogs both
/// are one; Spar asks for it from either class.
/// </summary>
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16, Options = ComInterfaceOptions.ComObjectWrapper)]
[Guid("42f85136-db7e-439c-85f1-e4075d135fc8")]
internal unsafe partial interface IFileDialog
{
    /// <summary>Returns <see cref="Shell.Cancelled"/> when the dialog was closed without a choice.</summary>
    [PreserveSig] int Show(nint owner);
    [PreserveSig] int SetFileTypes(uint count, FilterSpec* specs);
    [PreserveSig] int SetFileTypeIndex(uint index);
    [PreserveSig] int Slot006_GetFileTypeIndex();
    [PreserveSig] int Slot007_Advise();
    [PreserveSig] int Slot008_Unadvise();
    [PreserveSig] int SetOptions(uint options);
    [PreserveSig] int GetOptions(out uint options);
    [PreserveSig] int Slot011_SetDefaultFolder();
    [PreserveSig] int Slot012_SetFolder();
    [PreserveSig] int Slot013_GetFolder();
    [PreserveSig] int Slot014_GetCurrentSelection();
    [PreserveSig] int SetFileName(string name);
    [PreserveSig] int Slot016_GetFileName();
    [PreserveSig] int SetTitle(string title);
    [PreserveSig] int Slot018_SetOkButtonLabel();
    [PreserveSig] int Slot019_SetFileNameLabel();
    [PreserveSig] int GetResult(out nint item);
    [PreserveSig] int Slot021_AddPlace();
    [PreserveSig] int SetDefaultExtension(string extension);
    [PreserveSig] int Close(int hr);
}

/// <summary><c>IFileOpenDialog</c>, flat: <c>IFileDialog</c>'s 3–26 as placeholders (Spar calls them through <see cref="IFileDialog"/>), then 27.</summary>
[GeneratedComInterface(Options = ComInterfaceOptions.ComObjectWrapper)]
[Guid("d57c7288-d4ad-4768-be02-9d969532d960")]
internal partial interface IFileOpenDialog
{
    [PreserveSig] int Slot003_Show();
    [PreserveSig] int Slot004_SetFileTypes();
    [PreserveSig] int Slot005_SetFileTypeIndex();
    [PreserveSig] int Slot006_GetFileTypeIndex();
    [PreserveSig] int Slot007_Advise();
    [PreserveSig] int Slot008_Unadvise();
    [PreserveSig] int Slot009_SetOptions();
    [PreserveSig] int Slot010_GetOptions();
    [PreserveSig] int Slot011_SetDefaultFolder();
    [PreserveSig] int Slot012_SetFolder();
    [PreserveSig] int Slot013_GetFolder();
    [PreserveSig] int Slot014_GetCurrentSelection();
    [PreserveSig] int Slot015_SetFileName();
    [PreserveSig] int Slot016_GetFileName();
    [PreserveSig] int Slot017_SetTitle();
    [PreserveSig] int Slot018_SetOkButtonLabel();
    [PreserveSig] int Slot019_SetFileNameLabel();
    [PreserveSig] int Slot020_GetResult();
    [PreserveSig] int Slot021_AddPlace();
    [PreserveSig] int Slot022_SetDefaultExtension();
    [PreserveSig] int Slot023_Close();
    [PreserveSig] int Slot024_SetClientGuid();
    [PreserveSig] int Slot025_ClearClientData();
    [PreserveSig] int Slot026_SetFilter();
    [PreserveSig] int GetResults(out nint items);
}

/// <summary><c>IShellItem</c> (slots 3–5).</summary>
[GeneratedComInterface(Options = ComInterfaceOptions.ComObjectWrapper)]
[Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe")]
internal partial interface IShellItem
{
    [PreserveSig] int Slot003_BindToHandler();
    [PreserveSig] int Slot004_GetParent();

    /// <summary>The name is a <c>CoTaskMem</c> string the caller frees.</summary>
    [PreserveSig]
    int GetDisplayName(uint sigdn, out nint name);
}

/// <summary><c>IShellItemArray</c> (slots 3–8).</summary>
[GeneratedComInterface(Options = ComInterfaceOptions.ComObjectWrapper)]
[Guid("b63ea76d-1f85-456f-a19c-48159efa858b")]
internal partial interface IShellItemArray
{
    [PreserveSig] int Slot003_BindToHandler();
    [PreserveSig] int Slot004_GetPropertyStore();
    [PreserveSig] int Slot005_GetPropertyDescriptionList();
    [PreserveSig] int Slot006_GetAttributes();
    [PreserveSig] int GetCount(out uint count);
    [PreserveSig] int GetItemAt(uint index, out nint item);
}
