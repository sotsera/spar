using System.Runtime.InteropServices;
using System.Text;
using Checks.Desktop.Interop;

namespace Checks.Desktop;

// Reads the UI Automation tree of one window with the native UIA client (the API screen readers use), read-only.
// Slots from UIAutomationClient.h (SDK 10.0.26100): IUIAutomation 6 ElementFromHandle, 14 ControlViewWalker,
// 16 RawViewWalker; IUIAutomationTreeWalker 4 GetFirstChildElement, 6 GetNextSiblingElement; IUIAutomationElement
// 21 ControlType, 23 Name, 29 AutomationId, 30 ClassName, 43 BoundingRectangle.
internal static unsafe partial class UiaDump
{
    private static readonly Guid s_clsid = new("ff48dba4-60ef-4201-aa87-54103eef594e");
    private static readonly Guid s_iid = new("30cbe57d-d9d0-452a-ab13-7ac5ac4825ee");

    [LibraryImport("ole32")]
    private static partial int CoInitializeEx(nint reserved, uint mode);

    [LibraryImport("ole32")]
    private static partial int CoCreateInstance(in Guid clsid, nint outer, uint context, in Guid iid, out nint result);

    [LibraryImport("oleaut32")]
    private static partial void SysFreeString(nint bstr);

    public static int Run(nint hwnd, string outFile, bool raw)
    {
        CoInitializeEx(0, 0x2); // COINIT_APARTMENTTHREADED
        Vt.Check(CoCreateInstance(s_clsid, 0, 1, s_iid, out var uia), "CoCreateInstance(CUIAutomation)");
        nint root = 0, walker = 0;
        Vt.Check(((delegate* unmanaged[Stdcall]<nint, nint, nint*, int>)(*(void***)uia)[6])(uia, hwnd, &root), "ElementFromHandle");
        Vt.Check(Vt.Get(uia, raw ? 16 : 14, out walker), "walker");
        var lines = new StringBuilder();
        Walk(walker, root, 0, lines);
        File.WriteAllText(outFile, lines.ToString());
        Vt.Release(ref root);
        Vt.Release(ref walker);
        Vt.Release(ref uia);
        return 0;
    }

    // Finds the first descendant with the given AutomationId and calls its Invoke pattern, as a screen reader or
    // voice control would. Slots: IUIAutomation 23 CreatePropertyCondition; IUIAutomationElement 5 FindFirst,
    // 16 GetCurrentPattern; IUIAutomationInvokePattern 3 Invoke.
    public static int Invoke(nint hwnd, string automationId, string outFile)
    {
        CoInitializeEx(0, 0x2);
        Vt.Check(CoCreateInstance(s_clsid, 0, 1, s_iid, out var uia), "CoCreateInstance(CUIAutomation)");
        nint root = 0, condition = 0, element = 0, pattern = 0;
        Vt.Check(((delegate* unmanaged[Stdcall]<nint, nint, nint*, int>)(*(void***)uia)[6])(uia, hwnd, &root), "ElementFromHandle");
        var bstr = Marshal.StringToBSTR(automationId);
        var variant = stackalloc byte[24];
        new Span<byte>(variant, 24).Clear();
        *(ushort*)variant = 8; // VT_BSTR
        *(nint*)(variant + 8) = bstr;
        var hr = ((delegate* unmanaged[Stdcall]<nint, int, Variant24, nint*, int>)(*(void***)uia)[23])(uia, 30011, *(Variant24*)variant, &condition);
        SysFreeString(bstr);
        Vt.Check(hr, "CreatePropertyCondition");
        var found = ((delegate* unmanaged[Stdcall]<nint, int, nint, nint*, int>)(*(void***)root)[5])(root, 0x4, condition, &element);
        var result = $"find=0x{found:X8} element={(element != 0)}";
        if (element != 0)
        {
            var got = ((delegate* unmanaged[Stdcall]<nint, int, nint*, int>)(*(void***)element)[16])(element, 10000, &pattern);
            result += $" pattern=0x{got:X8}/{(pattern != 0)} name={Bstr(element, 23)}";
            if (pattern != 0)
            {
                result += $" invoke=0x{Vt.Call(pattern, 3):X8}";
            }
        }

        File.AppendAllText(outFile, $"{DateTime.Now:HH:mm:ss.fff} invoke {automationId}: {result}{Environment.NewLine}");
        Vt.Release(ref pattern);
        Vt.Release(ref element);
        Vt.Release(ref condition);
        Vt.Release(ref root);
        Vt.Release(ref uia);
        return 0;
    }

    [StructLayout(LayoutKind.Sequential, Size = 24)]
    private struct Variant24
    {
        public long A;
        public long B;
        public long C;
    }

    private static void Walk(nint walker, nint element, int depth, StringBuilder lines)
    {
        Vt.GetInt(element, 21, out var type);
        var name = Bstr(element, 23);
        var id = Bstr(element, 29);
        var cls = Bstr(element, 30);
        Rect r = default;
        ((delegate* unmanaged[Stdcall]<nint, Rect*, int>)(*(void***)element)[43])(element, &r);
        lines.Append(' ', depth * 2).Append($"{TypeName(type)} name=\"{name}\" id=\"{id}\" class=\"{cls}\" rect={r}").Append('\n');
        if (depth >= 12)
        {
            return;
        }

        nint child = 0;
        ((delegate* unmanaged[Stdcall]<nint, nint, nint*, int>)(*(void***)walker)[4])(walker, element, &child);
        while (child != 0)
        {
            Walk(walker, child, depth + 1, lines);
            nint next = 0;
            ((delegate* unmanaged[Stdcall]<nint, nint, nint*, int>)(*(void***)walker)[6])(walker, child, &next);
            Vt.Release(ref child);
            child = next;
        }
    }

    private static string Bstr(nint element, int slot)
    {
        nint b = 0;
        ((delegate* unmanaged[Stdcall]<nint, nint*, int>)(*(void***)element)[slot])(element, &b);
        if (b == 0)
        {
            return "";
        }

        var s = Marshal.PtrToStringBSTR(b);
        SysFreeString(b);
        return s;
    }

    private static string TypeName(int type) => type switch
    {
        50000 => "Button",
        50004 => "Edit",
        50018 => "Tab",
        50019 => "TabItem",
        50020 => "Text",
        50026 => "Group",
        50030 => "Document",
        50032 => "Window",
        50033 => "Pane",
        50037 => "TitleBar",
        50010 => "MenuBar",
        50011 => "MenuItem",
        _ => type.ToString(System.Globalization.CultureInfo.InvariantCulture),
    };
}
