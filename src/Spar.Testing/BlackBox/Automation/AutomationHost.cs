using System.Runtime.Versioning;
using Spar.Testing.BlackBox.Interop;

namespace Spar.Testing;

/// <summary>
/// UI Automation for launched apps, per placement and test process: one desktop and one UI Automation thread. The
/// hidden desktop is created at the first launch that needs it and lives until the test process exits, when Windows
/// frees it with the last handle. The app's windows are found by its process id with <see cref="TopWindows"/> and
/// reached by handle, never through a search of the whole desktop, which would ask every other app's windows too. A
/// call that does not return within <see cref="CallLimit"/> (an app that stopped answering its window messages) is
/// abandoned with its thread, which a new one replaces, so one hung app never stalls the next query.
/// </summary>
[SupportedOSPlatform("windows10.0.17763")]
internal sealed partial class AutomationHost : IBlackBoxAutomation
{
    private const int AriaRoleProperty = 30101;

    /// <summary>How long one UI Automation call may take before the fixture stops waiting for it.</summary>
    public static readonly TimeSpan CallLimit = TimeSpan.FromSeconds(10);

    private static readonly Lock Gate = new();
    private static AutomationHost? s_hidden;
    private static AutomationHost? s_shared;

    private readonly Lock _gate = new();
    private readonly HiddenDesktop? _desktop;
    private AutomationThread _thread;

    private AutomationHost(HiddenDesktop? desktop)
    {
        _desktop = desktop;
        _thread = AutomationThread.Start(desktop);
    }

    public string? Desktop => _desktop?.Name;

    /// <summary>The host for <paramref name="placement"/>, created at its first use.</summary>
    public static AutomationHost For(SparBlackBoxPlacement placement)
    {
        lock (Gate)
        {
            if (placement == SparBlackBoxPlacement.HiddenDesktop)
            {
                return s_hidden ??= new AutomationHost(HiddenDesktop.Create($"spar-black-box-{Environment.ProcessId}"));
            }

            return s_shared ??= new AutomationHost(null);
        }
    }

    public Task<string?> TitleAsync(int processId, CancellationToken cancellationToken = default) =>
        RunAsync(uia => MainWindow(uia, processId)?.Name, cancellationToken);

    public Task<IReadOnlyList<FoundElement>> FindAsync(int processId, ElementTarget target, CancellationToken cancellationToken = default) =>
        RunAsync<IReadOnlyList<FoundElement>>(
            uia => [.. Resolve(uia, processId, target).Select(element => new FoundElement(element.Name, element.IsEnabled, element.IsOffscreen))],
            cancellationToken);

    public Task<ElementOutcome> InvokeAsync(int processId, ElementTarget target, CancellationToken cancellationToken = default) =>
        RunAsync(
            uia => OnOne(uia, processId, target, element =>
            {
                if (!element.IsEnabled)
                {
                    return new ElementOutcome(ElementStatus.Disabled);
                }

                if (uia.Pattern<IUIAutomationInvokePattern>(element, Uia.InvokePattern) is not { } invoke)
                {
                    return new ElementOutcome(ElementStatus.Unsupported, "UI Automation's Invoke pattern");
                }

                invoke.Invoke();
                return new ElementOutcome(ElementStatus.Done);
            }),
            cancellationToken);

    public Task<ElementOutcome> ValueAsync(int processId, ElementTarget target, CancellationToken cancellationToken = default) =>
        RunAsync(
            uia => OnOne(uia, processId, target, element =>
                uia.Pattern<IUIAutomationValuePattern>(element, Uia.ValuePattern) is { } value
                    ? new ElementOutcome(ElementStatus.Done, value.GetCurrentValue() ?? "")
                    : new ElementOutcome(ElementStatus.Unsupported, "UI Automation's Value pattern")),
            cancellationToken);

    public Task<bool> CloseWindowAsync(int processId, CancellationToken cancellationToken = default) =>
        RunAsync(
            uia =>
            {
                if (MainWindow(uia, processId) is not { } window
                    || uia.Pattern<IUIAutomationWindowPattern>(window, Uia.WindowPattern) is not { } pattern)
                {
                    return false;
                }

                pattern.Close();
                return true;
            },
            cancellationToken);

    public Task<DialogContent?> FindDialogAsync(int processId, CancellationToken cancellationToken = default) =>
        RunAsync(
            uia =>
            {
                foreach (var window in TopWindows.Of(processId))
                {
                    if (!window.IsDialog || uia.FromHandle(window.Handle) is not { } dialog)
                    {
                        continue;
                    }

                    var texts = uia.FindAll(dialog, Uia.ScopeDescendants, uia.Equal(Uia.ControlTypeProperty, NativeControlTypes.Text))
                        .Select(text => text.Name)
                        .Where(text => text.Length > 0)
                        .ToList();
                    var buttons = DialogButtons(uia, dialog).Select(button => button.Name).ToList();
                    return new DialogContent(window.Handle, dialog.Name, texts, buttons);
                }

                return null;
            },
            cancellationToken);

    public Task<bool> PressAsync(DialogContent dialog, string button, CancellationToken cancellationToken = default) =>
        RunAsync(
            uia =>
            {
                var target = uia.FromHandle(dialog.Handle) is { } element
                    ? DialogButtons(uia, element).FirstOrDefault(found => found.Name == button)
                    : null;
                if (target is null || uia.Pattern<IUIAutomationInvokePattern>(target, Uia.InvokePattern) is not { } invoke)
                {
                    return false;
                }

                invoke.Invoke();
                return true;
            },
            cancellationToken);

    /// <summary>The app's main window: its first visible top-level window that is neither a dialog nor owned.</summary>
    private static UiaElement? MainWindow(Uia uia, int processId)
    {
        foreach (var window in TopWindows.Of(processId))
        {
            if (!window.IsDialog && window.Owner == 0 && uia.FromHandle(window.Handle) is { ControlType: NativeControlTypes.Window } element)
            {
                return element;
            }
        }

        return null;
    }

    private static List<UiaElement> Resolve(Uia uia, int processId, ElementTarget target)
    {
        if (MainWindow(uia, processId) is not { } window)
        {
            return [];
        }

        var query = target.Query;
        var found = uia.FindAll(window, Uia.ScopeDescendants, Condition(uia, query));
        if (query.NameRegex is { } pattern)
        {
            found = found.Where(element => pattern.IsMatch(element.Name)).ToList();
        }

        if (query.Disabled is { } disabled)
        {
            found = found.Where(element => element.IsEnabled != disabled).ToList();
        }

        return target.Index switch
        {
            null => found,
            -1 => found is [.., var last] ? [last] : [],
            { } index => index < found.Count ? [found[index]] : [],
        };
    }

    private static nint Condition(Uia uia, BlackBoxQuery query)
    {
        nint condition;
        if (query.TextOnly)
        {
            condition = uia.Equal(Uia.ControlTypeProperty, NativeControlTypes.Text);
        }
        else
        {
            condition = uia.Equal(AriaRoleProperty, query.AriaRole!, Uia.MatchIgnoreCase);
            if (query.NativeType is { } type)
            {
                var native = uia.And(uia.Equal(AriaRoleProperty, ""), uia.Equal(Uia.ControlTypeProperty, type));
                condition = uia.Or(condition, native);
            }
        }

        if (query.Name is not { } name)
        {
            return condition;
        }

        var flags = query.Exact ? 0 : Uia.MatchIgnoreCase | Uia.MatchSubstring;
        return uia.And(condition, uia.Equal(Uia.NameProperty, name, flags));
    }

    /// <summary>Runs <paramref name="action"/> on the one element found; none, several and the reasons are outcomes, not errors.</summary>
    private static ElementOutcome OnOne(Uia uia, int processId, ElementTarget target, Func<UiaElement, ElementOutcome> action)
    {
        var found = Resolve(uia, processId, target);
        return found.Count switch
        {
            0 => new ElementOutcome(ElementStatus.NotFound),
            1 => action(found[0]),
            _ => new ElementOutcome(ElementStatus.Ambiguous, Names: [.. found.Select(element => element.Name)]),
        };
    }

    /// <summary>A dialog's own buttons: every button under it but those of its title bar.</summary>
    private static List<UiaElement> DialogButtons(Uia uia, UiaElement dialog)
    {
        var buttons = new List<UiaElement>();
        var isButton = uia.Equal(Uia.ControlTypeProperty, NativeControlTypes.Button);
        foreach (var child in uia.FindAll(dialog, Uia.ScopeChildren, uia.True()))
        {
            if (child.ControlType == NativeControlTypes.TitleBar)
            {
                continue;
            }

            if (child.ControlType == NativeControlTypes.Button)
            {
                buttons.Add(child);
            }

            buttons.AddRange(uia.FindAll(child, Uia.ScopeDescendants, isButton));
        }

        return buttons;
    }

    /// <summary>Runs <paramref name="work"/> on the UI Automation thread, abandoning it after <see cref="CallLimit"/>.</summary>
    private async Task<T> RunAsync<T>(Func<Uia, T> work, CancellationToken cancellationToken)
    {
        AutomationThread thread;
        lock (_gate)
        {
            thread = _thread;
        }

        try
        {
            return await thread.RunAsync(work).WaitAsync(CallLimit, cancellationToken);
        }
        catch (TimeoutException)
        {
            Replace(thread);
            throw new TimeoutException(
                $"A UI Automation call did not return within {CallLimit.TotalSeconds:0} s: the app's window does not answer. " +
                "The call was abandoned, never repeated.");
        }
    }

    private void Replace(AutomationThread stuck)
    {
        lock (_gate)
        {
            if (_thread != stuck)
            {
                return;
            }

            // The stuck call keeps its thread; the new thread takes every later call.
            stuck.Dispose();
            _thread = AutomationThread.Start(_desktop);
        }
    }
}
