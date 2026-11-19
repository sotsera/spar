using System.Text.RegularExpressions;

namespace Spar.Testing;

/// <summary>
/// The WAI-ARIA roles a window test finds elements by, named as Microsoft's browser automation library names them, so
/// a window journey reads like a browser journey.
/// </summary>
public enum SparRole
{
    Alert,
    Alertdialog,
    Application,
    Article,
    Banner,
    Blockquote,
    Button,
    Caption,
    Cell,
    Checkbox,
    Code,
    Columnheader,
    Combobox,
    Complementary,
    Contentinfo,
    Definition,
    Deletion,
    Dialog,
    Directory,
    Document,
    Emphasis,
    Feed,
    Figure,
    Form,
    Generic,
    Grid,
    Gridcell,
    Group,
    Heading,
    Img,
    Insertion,
    Link,
    List,
    Listbox,
    Listitem,
    Log,
    Main,
    Mark,
    Marquee,
    Math,
    Menu,
    Menubar,
    Menuitem,
    Menuitemcheckbox,
    Menuitemradio,
    Meter,
    Navigation,
    None,
    Note,
    Option,
    Paragraph,
    Presentation,
    Progressbar,
    Radio,
    Radiogroup,
    Region,
    Row,
    Rowgroup,
    Rowheader,
    Scrollbar,
    Search,
    Searchbox,
    Separator,
    Slider,
    Spinbutton,
    Status,
    Strong,
    Subscript,
    Superscript,
    Switch,
    Tab,
    Table,
    Tablist,
    Tabpanel,
    Term,
    Textbox,
    Time,
    Timer,
    Toolbar,
    Tooltip,
    Tree,
    Treegrid,
    Treeitem,
}

/// <summary>What <c>GetByRole</c> matches besides the role: the accessible name and the element's states.</summary>
public sealed class SparGetByRoleOptions
{
    /// <summary>The accessible name: a case-insensitive substring, or the whole name with <see cref="Exact"/>.</summary>
    public string? Name { get; set; }

    /// <summary>The accessible name as a regular expression, in syntax that JavaScript's <c>RegExp</c> also reads.</summary>
    public Regex? NameRegex { get; set; }

    /// <summary>Match <see cref="Name"/> whole and case-sensitive (after whitespace is normalized).</summary>
    public bool? Exact { get; set; }

    public bool? Checked { get; set; }

    public bool? Disabled { get; set; }

    public bool? Expanded { get; set; }

    /// <summary>Also match elements hidden from assistive technology.</summary>
    public bool? IncludeHidden { get; set; }

    /// <summary>The heading level (<c>h1</c>–<c>h6</c>, or <c>aria-level</c>).</summary>
    public int? Level { get; set; }

    public bool? Pressed { get; set; }

    public bool? Selected { get; set; }
}

/// <summary>How <c>GetByText</c>, <c>GetByLabel</c> and the attribute queries match their text.</summary>
public sealed class SparGetByTextOptions
{
    /// <summary>Match the whole text, case-sensitive (after whitespace is normalized); a substring without case otherwise.</summary>
    public bool? Exact { get; set; }
}

/// <summary>What <c>Filter</c> keeps: elements with or without a text, or with or without a matching descendant.</summary>
public sealed class SparLocatorFilterOptions
{
    public string? HasText { get; set; }

    public Regex? HasTextRegex { get; set; }

    public string? HasNotText { get; set; }

    public Regex? HasNotTextRegex { get; set; }

    /// <summary>Keeps elements that contain an element this locator finds (searched from each element).</summary>
    public SparLocator? Has { get; set; }

    public SparLocator? HasNot { get; set; }
}

/// <summary>An action's limit: how long it waits for the element to be actionable.</summary>
public sealed class SparActionOptions
{
    /// <summary>Milliseconds; the page's <see cref="SparPage.DefaultTimeout"/> when null.</summary>
    public float? Timeout { get; set; }
}

/// <summary>An assertion's limit: how long it retries before it fails.</summary>
public sealed class SparAssertionOptions
{
    /// <summary>Milliseconds; the page's <see cref="SparPage.DefaultExpectTimeout"/> when null.</summary>
    public float? Timeout { get; set; }
}

/// <summary>Where a screenshot goes, besides the bytes it returns.</summary>
public sealed class SparScreenshotOptions
{
    /// <summary>A file to write the PNG to; its folder is created.</summary>
    public string? Path { get; set; }
}

/// <summary>The states <see cref="SparLocator.WaitForAsync"/> waits for.</summary>
public enum SparWaitForState
{
    Attached,
    Detached,
    Visible,
    Hidden,
}

/// <summary>What <see cref="SparLocator.WaitForAsync"/> waits for, and how long.</summary>
public sealed class SparWaitForOptions
{
    public SparWaitForState? State { get; set; }

    public float? Timeout { get; set; }
}
