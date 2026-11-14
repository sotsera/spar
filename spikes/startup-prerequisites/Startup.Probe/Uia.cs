using System.Runtime.InteropServices;
using System.Text;

namespace Startup.Probe;

// Native UI Automation client (the API screen readers use). Slots from UIAutomationClient.h (SDK 10.0.26100):
// IUIAutomation 6 ElementFromHandle, 14 ControlViewWalker, 23 CreatePropertyCondition, 25 CreateAndCondition;
// IUIAutomationTreeWalker 4 GetFirstChildElement, 6 GetNextSiblingElement; IUIAutomationElement 5 FindFirst,
// 16 GetCurrentPattern, 21 ControlType, 23 Name, 27 IsKeyboardFocusable, 28 IsEnabled, 29 AutomationId,
// 30 ClassName, 31 HelpText, 40 FrameworkId, 43 BoundingRectangle; IUIAutomationInvokePattern 3 Invoke.
internal static unsafe class Uia
{
    private static readonly Guid s_clsid = new("ff48dba4-60ef-4201-aa87-54103eef594e");
    private static readonly Guid s_iid = new("30cbe57d-d9d0-452a-ab13-7ac5ac4825ee");
    private const int NameProperty = 30005, ControlTypeProperty = 30003, InvokePattern = 10000;

    private static void* Fn(nint p, int slot) => (*(void***)p)[slot];

    private static void Release(ref nint p)
    {
        if (p != 0)
        {
            Marshal.Release(p);
            p = 0;
        }
    }

    private static nint Create()
    {
        Native.CoInitializeEx(0, 0x2);
        var hr = Native.CoCreateInstance(s_clsid, 0, 1, s_iid, out var uia);
        return hr < 0 ? throw new COMException("CUIAutomation", hr) : uia;
    }

    private static nint FromHandle(nint uia, nint hwnd)
    {
        nint root = 0;
        var hr = ((delegate* unmanaged[Stdcall]<nint, nint, nint*, int>)Fn(uia, 6))(uia, hwnd, &root);
        return hr < 0 ? throw new COMException("ElementFromHandle", hr) : root;
    }

    public static string Dump(nint hwnd)
    {
        var uia = Create();
        var root = FromHandle(uia, hwnd);
        nint walker = 0;
        ((delegate* unmanaged[Stdcall]<nint, nint*, int>)Fn(uia, 14))(uia, &walker);
        var lines = new StringBuilder();
        Walk(walker, root, 0, lines);
        Release(ref walker);
        Release(ref root);
        Release(ref uia);
        return lines.ToString();
    }

    private static void Walk(nint walker, nint element, int depth, StringBuilder lines)
    {
        int type = 0, focusable = 0, enabled = 0;
        ((delegate* unmanaged[Stdcall]<nint, int*, int>)Fn(element, 21))(element, &type);
        ((delegate* unmanaged[Stdcall]<nint, int*, int>)Fn(element, 27))(element, &focusable);
        ((delegate* unmanaged[Stdcall]<nint, int*, int>)Fn(element, 28))(element, &enabled);
        Native.Rect r = default;
        ((delegate* unmanaged[Stdcall]<nint, Native.Rect*, int>)Fn(element, 43))(element, &r);
        var name = Bstr(element, 23).Replace("\r", "\\r", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal);
        lines.Append(' ', depth * 2).Append($"{TypeName(type)} name=\"{name}\" id=\"{Bstr(element, 29)}\" class=\"{Bstr(element, 30)}\"")
            .Append($" help=\"{Bstr(element, 31)}\" framework=\"{Bstr(element, 40)}\" focusable={focusable} enabled={enabled} rect={r}\n");
        if (depth >= 10)
        {
            return;
        }

        nint child = 0;
        ((delegate* unmanaged[Stdcall]<nint, nint, nint*, int>)Fn(walker, 4))(walker, element, &child);
        while (child != 0)
        {
            Walk(walker, child, depth + 1, lines);
            nint next = 0;
            ((delegate* unmanaged[Stdcall]<nint, nint, nint*, int>)Fn(walker, 6))(walker, child, &next);
            Release(ref child);
            child = next;
        }
    }

    // Finds the first descendant of the given control type (and name, if given) and invokes it, as assistive
    // technology would.
    public static string Invoke(nint hwnd, int controlType, string? name)
    {
        var uia = Create();
        var root = FromHandle(uia, hwnd);
        nint typeCondition = 0, nameCondition = 0, condition = 0, element = 0, pattern = 0;
        var variant = stackalloc long[3];
        variant[0] = 3; // VT_I4
        variant[1] = controlType;
        variant[2] = 0;
        ((delegate* unmanaged[Stdcall]<nint, int, Variant24, nint*, int>)Fn(uia, 23))(uia, ControlTypeProperty, *(Variant24*)variant, &typeCondition);
        condition = typeCondition;
        if (name is not null)
        {
            var bstr = Marshal.StringToBSTR(name);
            variant[0] = 8; // VT_BSTR
            variant[1] = bstr;
            ((delegate* unmanaged[Stdcall]<nint, int, Variant24, nint*, int>)Fn(uia, 23))(uia, NameProperty, *(Variant24*)variant, &nameCondition);
            Native.SysFreeString(bstr);
            ((delegate* unmanaged[Stdcall]<nint, nint, nint, nint*, int>)Fn(uia, 25))(uia, typeCondition, nameCondition, &condition);
        }

        var found = ((delegate* unmanaged[Stdcall]<nint, int, nint, nint*, int>)Fn(root, 5))(root, 0x4, condition, &element);
        var result = $"find=0x{found:X8} element={element != 0}";
        if (element != 0)
        {
            var got = ((delegate* unmanaged[Stdcall]<nint, int, nint*, int>)Fn(element, 16))(element, InvokePattern, &pattern);
            result += $" name=\"{Bstr(element, 23)}\" pattern=0x{got:X8}";
            if (pattern != 0)
            {
                result += $" invoke=0x{((delegate* unmanaged[Stdcall]<nint, int>)Fn(pattern, 3))(pattern):X8}";
            }
        }

        Release(ref pattern);
        Release(ref element);
        if (condition != typeCondition)
        {
            Release(ref condition);
        }

        Release(ref nameCondition);
        Release(ref typeCondition);
        Release(ref root);
        Release(ref uia);
        return result;
    }

    [StructLayout(LayoutKind.Sequential, Size = 24)]
    private struct Variant24
    {
        public long A;
        public long B;
        public long C;
    }

    private static string Bstr(nint element, int slot)
    {
        nint b = 0;
        ((delegate* unmanaged[Stdcall]<nint, nint*, int>)Fn(element, slot))(element, &b);
        if (b == 0)
        {
            return "";
        }

        var s = Marshal.PtrToStringBSTR(b);
        Native.SysFreeString(b);
        return s;
    }

    private static string TypeName(int type) => type switch
    {
        50000 => "Button",
        50005 => "Hyperlink",
        50006 => "Image",
        50020 => "Text",
        50026 => "Group",
        50032 => "Window",
        50033 => "Pane",
        50037 => "TitleBar",
        50010 => "MenuBar",
        50011 => "MenuItem",
        _ => type.ToString(System.Globalization.CultureInfo.InvariantCulture),
    };
}
