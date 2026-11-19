using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Spar.Testing.BlackBox.Interop;

// UI Automation's client interfaces, hand-declared from the Windows SDK's UIAutomationClient.h (10.0.26100.0) with
// source-generated COM interop, so the black-box fixture runs under NativeAOT (skill "spar-native-interop"). Every slot
// of each vtable up to the last one called is declared in header order, unused ones as [PreserveSig] SlotNNN_Name
// placeholders; Spar.Tests checks each slot against the header. The fixture only calls these interfaces, so each has
// the COM-object wrapper only. A method that hands out an interface returns a raw pointer with one reference the caller
// owns (Uia.Take wraps it); conditions stay raw pointers, released by the caller.

/// <summary><c>IUIAutomation</c> (slots 3–28).</summary>
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16, Options = ComInterfaceOptions.ComObjectWrapper)]
[Guid("30cbe57d-d9d0-452a-ab13-7ac5ac4825ee")]
internal partial interface IUIAutomation
{
    [PreserveSig] int Slot003_CompareElements();
    [PreserveSig] int Slot004_CompareRuntimeIds();

    /// <summary>The desktop of the calling thread, with one owned reference.</summary>
    nint GetRootElement();

    nint ElementFromHandle(nint window);

    [PreserveSig] int Slot007_ElementFromPoint();
    [PreserveSig] int Slot008_GetFocusedElement();
    [PreserveSig] int Slot009_GetRootElementBuildCache();
    [PreserveSig] int Slot010_ElementFromHandleBuildCache();
    [PreserveSig] int Slot011_ElementFromPointBuildCache();
    [PreserveSig] int Slot012_GetFocusedElementBuildCache();
    [PreserveSig] int Slot013_CreateTreeWalker();
    [PreserveSig] int Slot014_GetControlViewWalker();
    [PreserveSig] int Slot015_GetContentViewWalker();
    [PreserveSig] int Slot016_GetRawViewWalker();
    [PreserveSig] int Slot017_GetRawViewCondition();
    [PreserveSig] int Slot018_GetControlViewCondition();
    [PreserveSig] int Slot019_GetContentViewCondition();
    [PreserveSig] int Slot020_CreateCacheRequest();

    nint CreateTrueCondition();

    [PreserveSig] int Slot022_CreateFalseCondition();

    nint CreatePropertyCondition(int propertyId, Variant value);

    nint CreatePropertyConditionEx(int propertyId, Variant value, int flags);

    nint CreateAndCondition(nint condition1, nint condition2);

    [PreserveSig] int Slot026_CreateAndConditionFromArray();
    [PreserveSig] int Slot027_CreateAndConditionFromNativeArray();

    nint CreateOrCondition(nint condition1, nint condition2);
}

/// <summary><c>IUIAutomationElement</c> (slots 3–45).</summary>
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16, Options = ComInterfaceOptions.ComObjectWrapper)]
[Guid("d22108aa-8ac5-49a5-837b-37bbb3d7591e")]
internal partial interface IUIAutomationElement
{
    [PreserveSig] int Slot003_SetFocus();
    [PreserveSig] int Slot004_GetRuntimeId();

    /// <summary>The first match, or 0 when there is none (<c>S_OK</c> with a null element).</summary>
    nint FindFirst(int scope, nint condition);

    nint FindAll(int scope, nint condition);

    [PreserveSig] int Slot007_FindFirstBuildCache();
    [PreserveSig] int Slot008_FindAllBuildCache();
    [PreserveSig] int Slot009_BuildUpdatedCache();

    Variant GetCurrentPropertyValue(int propertyId);

    [PreserveSig] int Slot011_GetCurrentPropertyValueEx();
    [PreserveSig] int Slot012_GetCachedPropertyValue();
    [PreserveSig] int Slot013_GetCachedPropertyValueEx();
    [PreserveSig] int Slot014_GetCurrentPatternAs();
    [PreserveSig] int Slot015_GetCachedPatternAs();

    /// <summary>The pattern's object (an <c>IUnknown</c> with one owned reference), or 0 when the element has none.</summary>
    nint GetCurrentPattern(int patternId);

    [PreserveSig] int Slot017_GetCachedPattern();
    [PreserveSig] int Slot018_GetCachedParent();
    [PreserveSig] int Slot019_GetCachedChildren();

    int GetCurrentProcessId();

    int GetCurrentControlType();

    [return: MarshalAs(UnmanagedType.BStr)]
    string GetCurrentLocalizedControlType();

    [return: MarshalAs(UnmanagedType.BStr)]
    string GetCurrentName();

    [PreserveSig] int Slot024_GetCurrentAcceleratorKey();
    [PreserveSig] int Slot025_GetCurrentAccessKey();
    [PreserveSig] int Slot026_GetCurrentHasKeyboardFocus();
    [PreserveSig] int Slot027_GetCurrentIsKeyboardFocusable();

    int GetCurrentIsEnabled();

    [return: MarshalAs(UnmanagedType.BStr)]
    string GetCurrentAutomationId();

    [return: MarshalAs(UnmanagedType.BStr)]
    string GetCurrentClassName();

    [PreserveSig] int Slot031_GetCurrentHelpText();
    [PreserveSig] int Slot032_GetCurrentCulture();
    [PreserveSig] int Slot033_GetCurrentIsControlElement();
    [PreserveSig] int Slot034_GetCurrentIsContentElement();
    [PreserveSig] int Slot035_GetCurrentIsPassword();

    nint GetCurrentNativeWindowHandle();

    [PreserveSig] int Slot037_GetCurrentItemType();

    int GetCurrentIsOffscreen();

    [PreserveSig] int Slot039_GetCurrentOrientation();
    [PreserveSig] int Slot040_GetCurrentFrameworkId();
    [PreserveSig] int Slot041_GetCurrentIsRequiredForForm();
    [PreserveSig] int Slot042_GetCurrentItemStatus();
    [PreserveSig] int Slot043_GetCurrentBoundingRectangle();
    [PreserveSig] int Slot044_GetCurrentLabeledBy();

    [return: MarshalAs(UnmanagedType.BStr)]
    string GetCurrentAriaRole();
}

/// <summary><c>IUIAutomationElementArray</c> (slots 3–4).</summary>
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16, Options = ComInterfaceOptions.ComObjectWrapper)]
[Guid("14314595-b4bc-4055-95f2-58f2e42c9855")]
internal partial interface IUIAutomationElementArray
{
    int GetLength();

    nint GetElement(int index);
}

/// <summary><c>IUIAutomationInvokePattern</c> (slot 3).</summary>
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16, Options = ComInterfaceOptions.ComObjectWrapper)]
[Guid("fb377fbe-8ea6-46d5-9c73-6499642d3059")]
internal partial interface IUIAutomationInvokePattern
{
    void Invoke();
}

/// <summary><c>IUIAutomationValuePattern</c> (slots 3–4).</summary>
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16, Options = ComInterfaceOptions.ComObjectWrapper)]
[Guid("a94cd8b1-0844-4cd6-9d2d-640537ab39e9")]
internal partial interface IUIAutomationValuePattern
{
    [PreserveSig] int Slot003_SetValue();

    [return: MarshalAs(UnmanagedType.BStr)]
    string GetCurrentValue();
}

/// <summary><c>IUIAutomationWindowPattern</c> (slot 3): <c>Close</c> asks the window to close, as its close button does.</summary>
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16, Options = ComInterfaceOptions.ComObjectWrapper)]
[Guid("0faef453-9208-43ef-bbb2-3b485177864f")]
internal partial interface IUIAutomationWindowPattern
{
    void Close();
}

/// <summary>
/// A <c>VARIANT</c> as UI Automation passes it, by value: the type, three reserved words and the value, 24 bytes on
/// 64-bit Windows (16 on 32-bit). Only <c>VT_I4</c>, <c>VT_BOOL</c> and <c>VT_BSTR</c> are written or read.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal partial struct Variant
{
    public const ushort Empty = 0;
    public const ushort Int32 = 3;
    public const ushort String = 8;
    public const ushort Boolean = 11;

    public ushort Type;
    public ushort Reserved1;
    public ushort Reserved2;
    public ushort Reserved3;
    public nint Value;
    public nint Extra;

    public static Variant FromInt32(int value) => new() { Type = Int32, Value = value };

    /// <summary>A <c>VT_BSTR</c> whose string the caller frees with <see cref="Clear"/> after the call.</summary>
    public static Variant FromString(string value) => new() { Type = String, Value = Marshal.StringToBSTR(value) };

    /// <summary>The string a <c>VT_BSTR</c> holds; null for any other type.</summary>
    public readonly string? AsString() => Type == String && Value != 0 ? Marshal.PtrToStringBSTR(Value) : null;

    /// <summary>Frees what the variant owns (<c>VariantClear</c>) and leaves it empty.</summary>
    public static unsafe void Clear(ref Variant variant)
    {
        fixed (Variant* pointer = &variant)
        {
            _ = VariantClear(pointer);
        }
    }

    [LibraryImport("oleaut32")]
    private static unsafe partial int VariantClear(Variant* variant);
}
