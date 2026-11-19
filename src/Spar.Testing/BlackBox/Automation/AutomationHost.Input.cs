using System.Globalization;
using System.Text;
using Spar.Testing.BlackBox.Interop;
using Windows.Win32;

namespace Spar.Testing;

/// <summary>
/// What real input in Windows Sandbox needs from UI Automation (step 8c): where an element and the window's title bar
/// are, focus given without input, the windows of the app's whole process tree (a context menu or a devtools window
/// is the browser's, not the app's), and a text dump of them for the evidence. Reading only: nothing here sends input.
/// </summary>
internal sealed partial class AutomationHost
{
    private const int MenuItemType = 50011;

    public Task<ElementOutcome> BoundsAsync(int processId, ElementTarget target, CancellationToken cancellationToken = default) =>
        RunAsync(
            uia => OnOne(uia, processId, target, element =>
            {
                if (!element.IsEnabled)
                {
                    return new ElementOutcome(ElementStatus.Disabled);
                }

                var bounds = Screen(element.Bounds);
                return element.IsOffscreen || bounds.IsEmpty
                    ? new ElementOutcome(ElementStatus.NotFound)
                    : new ElementOutcome(ElementStatus.Done, Bounds: bounds);
            }),
            cancellationToken);

    public Task<ElementOutcome> FocusAsync(int processId, ElementTarget target, CancellationToken cancellationToken = default) =>
        RunAsync(
            uia => OnOne(uia, processId, target, element =>
            {
                if (!element.IsEnabled)
                {
                    return new ElementOutcome(ElementStatus.Disabled);
                }

                element.Com.SetFocus();
                return new ElementOutcome(ElementStatus.Done);
            }),
            cancellationToken);

    public Task<WindowParts?> WindowPartsAsync(int processId, CancellationToken cancellationToken = default) =>
        RunAsync(
            uia =>
            {
                if (MainWindow(uia, processId) is not { } window)
                {
                    return null;
                }

                var titleBar = uia.FindFirst(window, Uia.ScopeChildren, uia.Equal(Uia.ControlTypeProperty, NativeControlTypes.TitleBar));
                var handle = window.Handle;
                var foreground = TopWindows.Foreground();
                return new WindowParts(handle, Screen(window.Bounds), titleBar is null ? null : Screen(titleBar.Bounds), handle == foreground);
            },
            cancellationToken);

    public Task<IReadOnlyList<TopLevelWindow>> TopLevelWindowsAsync(IReadOnlyList<int> processIds, CancellationToken cancellationToken = default) =>
        RunAsync<IReadOnlyList<TopLevelWindow>>(
            uia =>
            {
                var found = new List<TopLevelWindow>();
                var isMenuItem = uia.Equal(Uia.ControlTypeProperty, MenuItemType);
                foreach (var processId in processIds)
                {
                    foreach (var window in TopWindows.Of(processId))
                    {
                        if (uia.FromHandle(window.Handle) is not { } element)
                        {
                            continue;
                        }

                        var items = uia.FindAll(element, Uia.ScopeSubtree, isMenuItem).Select(item => item.Name).ToList();
                        found.Add(new TopLevelWindow(processId, window.Handle, window.ClassName, element.Name, element.ControlType, items));
                    }
                }

                return found;
            },
            cancellationToken);

    public Task<ScreenRect?> DialogButtonBoundsAsync(DialogContent dialog, string button, CancellationToken cancellationToken = default) =>
        RunAsync<ScreenRect?>(
            uia =>
            {
                var target = uia.FromHandle(dialog.Handle) is { } element
                    ? DialogButtons(uia, element).FirstOrDefault(found => found.Name == button)
                    : null;
                return target is null ? null : Screen(target.Bounds);
            },
            cancellationToken);

    public Task<string> DumpAsync(IReadOnlyList<int> processIds, CancellationToken cancellationToken = default) =>
        RunAsync(
            uia =>
            {
                var text = new StringBuilder();
                var all = uia.True();
                foreach (var processId in processIds)
                {
                    foreach (var window in TopWindows.Of(processId))
                    {
                        text.Append(CultureInfo.InvariantCulture, $"window 0x{window.Handle:X} process {processId} class {window.ClassName}\n");
                        if (uia.FromHandle(window.Handle) is not { } element)
                        {
                            continue;
                        }

                        foreach (var child in uia.FindAll(element, Uia.ScopeSubtree, all))
                        {
                            text.Append(CultureInfo.InvariantCulture, $"  {Line(child)}\n");
                        }
                    }
                }

                return text.ToString();
            },
            cancellationToken);

    public Task<bool> EndSessionAsync(int processId, CancellationToken cancellationToken = default) =>
        RunAsync(
            uia =>
            {
                if (MainWindow(uia, processId) is not { } window)
                {
                    return false;
                }

                // Sent from this thread, which is on the app's desktop; each waits at most as long as Windows would.
                var handle = window.Handle;
                TopWindows.SendSessionEnd(handle, query: true);
                TopWindows.SendSessionEnd(handle, query: false);
                return true;
            },
            cancellationToken);

    /// <summary>One element as a dump line: control type, ARIA role, name, rectangle, and its state.</summary>
    private static string Line(UiaElement element)
    {
        var role = element.AriaRole is { Length: > 0 } aria ? $" role={aria}" : "";
        var state = (element.IsEnabled ? "" : " disabled") + (element.IsOffscreen ? " offscreen" : "");
        return $"{element.LocalizedControlType} ({element.ControlType}){role} \"{element.Name}\" {Screen(element.Bounds)}{state}";
    }

    private static ScreenRect Screen(UiaRect rect) => new(rect.Left, rect.Top, rect.Right, rect.Bottom);
}
