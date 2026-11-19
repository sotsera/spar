using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Spar.Testing.BlackBox.Interop;

namespace Spar.Testing;

/// <summary>
/// UI Automation as the fixture uses it, on <see cref="AutomationThread"/> only: conditions, searches and the elements
/// they find. Every element and condition made during one call is kept here and released when the call returns.
/// Identifiers are UIAutomationClient.h's.
/// </summary>
internal sealed class Uia
{
    public const int ScopeChildren = 0x2;
    public const int ScopeDescendants = 0x4;

    public const int ProcessIdProperty = 30002;
    public const int ControlTypeProperty = 30003;
    public const int NameProperty = 30005;
    public const int ClassNameProperty = 30012;

    public const int InvokePattern = 10000;
    public const int ValuePattern = 10002;
    public const int WindowPattern = 10009;

    public const int MatchIgnoreCase = 0x1;
    public const int MatchSubstring = 0x2;

    /// <summary>The wrappers for unique instances: stateless strategies, safe from any thread.</summary>
    private static readonly StrategyBasedComWrappers Wrappers = new();

    private readonly IUIAutomation _automation;
    private readonly List<object> _wrappers = [];
    private readonly List<nint> _conditions = [];

    public Uia(IUIAutomation automation)
    {
        _automation = automation;
    }

    /// <summary>Wraps a pointer whose reference the caller owns, as a wrapper of its own that takes the reference over.</summary>
    public static T Take<T>(nint pointer)
        where T : class
    {
        try
        {
            return (T)Wrappers.GetOrCreateObjectForComInstance(pointer, CreateObjectFlags.UniqueInstance);
        }
        finally
        {
            Marshal.Release(pointer);
        }
    }

    /// <summary>The desktop the thread is on: its children are the top-level windows there.</summary>
    public UiaElement Root() => Element(_automation.GetRootElement())!;

    public UiaElement? FromHandle(nint window) => Element(_automation.ElementFromHandle(window));

    public nint True() => Keep(_automation.CreateTrueCondition());

    public nint Equal(int property, int value) => Keep(_automation.CreatePropertyCondition(property, Variant.FromInt32(value)));

    /// <summary>A string property equal to <paramref name="value"/>, or containing it, case-insensitively, with the flags.</summary>
    public nint Equal(int property, string value, int flags = 0)
    {
        var variant = Variant.FromString(value);
        try
        {
            return Keep(_automation.CreatePropertyConditionEx(property, variant, flags));
        }
        finally
        {
            Variant.Clear(ref variant);
        }
    }

    public nint And(nint first, nint second) => Keep(_automation.CreateAndCondition(first, second));

    public nint Or(nint first, nint second) => Keep(_automation.CreateOrCondition(first, second));

    public List<UiaElement> FindAll(UiaElement from, int scope, nint condition)
    {
        var array = Take<IUIAutomationElementArray>(from.Com.FindAll(scope, condition));
        try
        {
            var length = array.GetLength();
            var found = new List<UiaElement>(length);
            for (var index = 0; index < length; index++)
            {
                if (Element(array.GetElement(index)) is { } element)
                {
                    found.Add(element);
                }
            }

            return found;
        }
        finally
        {
            Release(array);
        }
    }

    public UiaElement? FindFirst(UiaElement from, int scope, nint condition) => Element(from.Com.FindFirst(scope, condition));

    /// <summary>The element's pattern object, as <typeparamref name="T"/>, or null when it does not have the pattern.</summary>
    public T? Pattern<T>(UiaElement element, int pattern)
        where T : class
    {
        var pointer = element.Com.GetCurrentPattern(pattern);
        if (pointer == 0)
        {
            return null;
        }

        var wrapper = Take<T>(pointer);
        _wrappers.Add(wrapper);
        return wrapper;
    }

    /// <summary>Releases everything the call found; called by the thread when the call returns.</summary>
    public void ReleaseFound()
    {
        foreach (var wrapper in _wrappers)
        {
            Release(wrapper);
        }

        foreach (var condition in _conditions)
        {
            Marshal.Release(condition);
        }

        _wrappers.Clear();
        _conditions.Clear();
    }

    private static void Release(object wrapper)
    {
        if (wrapper is ComObject com)
        {
            com.FinalRelease();
        }
    }

    private UiaElement? Element(nint pointer)
    {
        if (pointer == 0)
        {
            return null;
        }

        var com = Take<IUIAutomationElement>(pointer);
        _wrappers.Add(com);
        return new UiaElement(com);
    }

    private nint Keep(nint condition)
    {
        _conditions.Add(condition);
        return condition;
    }
}

/// <summary>One element, valid until the call that found it returns. Reading a property of an element that has gone throws.</summary>
internal sealed class UiaElement(IUIAutomationElement com)
{
    public const int ButtonType = 50000;
    public const int TextType = 50020;
    public const int WindowType = 50032;
    public const int PaneType = 50033;
    public const int DocumentType = 50030;

    public IUIAutomationElement Com { get; } = com;

    public string Name => Com.GetCurrentName() ?? "";

    public int ControlType => Com.GetCurrentControlType();

    public string LocalizedControlType => Com.GetCurrentLocalizedControlType() ?? "";

    public int ProcessId => Com.GetCurrentProcessId();

    public string ClassName => Com.GetCurrentClassName() ?? "";

    public string AutomationId => Com.GetCurrentAutomationId() ?? "";

    public string AriaRole => Com.GetCurrentAriaRole() ?? "";

    public nint Handle => Com.GetCurrentNativeWindowHandle();

    public bool IsEnabled => Com.GetCurrentIsEnabled() != 0;

    public bool IsOffscreen => Com.GetCurrentIsOffscreen() != 0;
}
