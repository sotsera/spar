using System.Globalization;
using System.Runtime.Versioning;
using System.Text;

namespace Spar.Testing;

/// <summary>
/// The black box's accessibility client on Linux, for one launched app: AT-SPI over GDBus on the app's own
/// accessibility bus (<see cref="AtSpiBus"/>). Its windows are its connection's top-level nodes, found by its process
/// through the bus daemon; the main window is the frame (its <c>Name</c> is the title), a dialog is a dialog or alert
/// node. The page's elements are WebKit's nodes under the frame, matched by the ARIA role WebKit computes
/// (<c>computed-role</c>) and their accessible name, as UI Automation's ARIA role is on Windows; a native element (a
/// dialog's button) has no computed role and matches by its AT-SPI role. Actions go through AT-SPI's interfaces
/// (<c>Action</c>, <c>Text</c>, <c>Component</c>), never through input. A dialog's title is GTK's empty one, reported as
/// the app's name, as Windows' task dialogs show it (decision "The Linux black box…"): substituted.
/// </summary>
[SupportedOSPlatform("linux")]
internal sealed class AtSpiAutomation : IBlackBoxAutomation, IDisposable
{
    private readonly AtSpiBus _bus;
    private readonly string _appName;
    private readonly Func<int, Task<bool>> _endSession;

    /// <param name="sessionAddress">The app's own session bus, through which its accessibility bus is found.</param>
    /// <param name="appName">The name the app's dialogs are reported with (their GTK title is empty).</param>
    /// <param name="endSession">Sends the app the session's end (<c>SIGTERM</c>) and waits as the OS would.</param>
    public AtSpiAutomation(string sessionAddress, string appName, Func<int, Task<bool>> endSession)
    {
        _bus = new AtSpiBus(sessionAddress);
        _appName = appName;
        _endSession = endSession;
    }

    public string? Desktop => null;

    /// <summary>Zero: a WebKit page's invoke as the first action after the launch landed in 32 of 32 measured launches.</summary>
    public TimeSpan PageSettleTime => TimeSpan.Zero;

    /// <summary>Always: with no settle time there is nothing to wait for.</summary>
    public Task<bool> HasPageAsync(int processId, CancellationToken cancellationToken = default) => Task.FromResult(true);

    public Task<string?> TitleAsync(int processId, CancellationToken cancellationToken = default) =>
        Run(() => MainWindow(processId) is { } window ? Read(() => _bus.NameOf(window)) : null, cancellationToken);

    public Task<IReadOnlyList<FoundElement>> FindAsync(int processId, ElementTarget target, CancellationToken cancellationToken = default) =>
        Run<IReadOnlyList<FoundElement>>(
            () => [.. Resolve(processId, target).Select(element => new FoundElement(element.Name, element.IsEnabled, element.IsOffscreen))],
            cancellationToken);

    public Task<ElementOutcome> InvokeAsync(int processId, ElementTarget target, CancellationToken cancellationToken = default) =>
        Run(() => OnOne(processId, target, element =>
        {
            if (!element.IsEnabled)
            {
                return new ElementOutcome(ElementStatus.Disabled);
            }

            // WebKit answers no GetActions (measured: the call times out), so its first action, its default one, is done directly.
            if (Read(() => _bus.Interfaces(element.Node))?.Contains("org.a11y.atspi.Action") != true)
            {
                return new ElementOutcome(ElementStatus.Unsupported, "AT-SPI's Action interface");
            }

            _bus.DoAction(element.Node, 0);
            return new ElementOutcome(ElementStatus.Done);
        }), cancellationToken);

    public Task<ElementOutcome> ValueAsync(int processId, ElementTarget target, CancellationToken cancellationToken = default) =>
        Run(() => OnOne(processId, target, element =>
            Read(() => _bus.Interfaces(element.Node))?.Contains("org.a11y.atspi.Text") == true
                ? new ElementOutcome(ElementStatus.Done, _bus.Text(element.Node))
                : new ElementOutcome(ElementStatus.Unsupported, "AT-SPI's Text interface")), cancellationToken);

    public Task<bool> CloseWindowAsync(int processId, CancellationToken cancellationToken = default) =>
        Run(() =>
        {
            if (MainWindow(processId) is not { } window || Read(() => _bus.Actions(window)) is not { } actions)
            {
                return false;
            }

            var close = actions.IndexOf("window.close");
            return close >= 0 && _bus.DoAction(window, close);
        }, cancellationToken);

    public Task<DialogContent?> FindDialogAsync(int processId, CancellationToken cancellationToken = default) =>
        Run(() =>
        {
            foreach (var (node, role) in _bus.TopLevels(processId))
            {
                // Only once it shows: GTK lists a new dialog on the bus slightly before it maps it.
                if (role is AtSpiBus.RoleDialog or AtSpiBus.RoleAlert
                    && Read(() => _bus.States(node)) is { } states && AtSpiBus.Has(states, AtSpiBus.StateShowing)
                    && ReadDialog(node) is { } dialog)
                {
                    return dialog;
                }
            }

            return null;
        }, cancellationToken);

    public Task<bool> PressAsync(DialogContent dialog, string button, CancellationToken cancellationToken = default) =>
        Run(() =>
        {
            if (dialog.Native is not AtSpiNode node || FindButton(node, button) is not { } target || Read(() => _bus.Actions(target)) is not { } actions)
            {
                return false;
            }

            // GTK answers a button's action about 250 ms later: it shows the press first (docs/evidence/linux-blackbox-atspi).
            var click = actions.IndexOf("click");
            return _bus.DoAction(target, click < 0 ? 0 : click);
        }, cancellationToken);

    public Task<ElementOutcome> BoundsAsync(int processId, ElementTarget target, CancellationToken cancellationToken = default) =>
        Run(() => OnOne(processId, target, element =>
        {
            if (!element.IsEnabled)
            {
                return new ElementOutcome(ElementStatus.Disabled);
            }

            var bounds = Read(() => _bus.WindowExtents(element.Node));
            return element.IsOffscreen || bounds is not { IsEmpty: false } box
                ? new ElementOutcome(ElementStatus.NotFound)
                : new ElementOutcome(ElementStatus.Done, Bounds: box);
        }), cancellationToken);

    public Task<ElementOutcome> FocusAsync(int processId, ElementTarget target, CancellationToken cancellationToken = default) =>
        Run(() => OnOne(processId, target, element =>
        {
            if (!element.IsEnabled)
            {
                return new ElementOutcome(ElementStatus.Disabled);
            }

            return _bus.GrabFocus(element.Node)
                ? new ElementOutcome(ElementStatus.Done)
                : new ElementOutcome(ElementStatus.Unsupported, "AT-SPI's GrabFocus");
        }), cancellationToken);

    public Task<WindowParts?> WindowPartsAsync(int processId, CancellationToken cancellationToken = default) =>
        Run<WindowParts?>(() =>
        {
            if (MainWindow(processId) is not { } window || Read(() => _bus.WindowExtents(window)) is not { } bounds)
            {
                return null;
            }

            // GTK 4 draws no title bar of the window manager's, and knows no window position on X11; no handle either.
            var states = Read(() => _bus.States(window)) ?? 0;
            return new WindowParts(0, bounds, null, AtSpiBus.Has(states, 1));
        }, cancellationToken);

    public Task<IReadOnlyList<TopLevelWindow>> TopLevelWindowsAsync(IReadOnlyList<int> processIds, CancellationToken cancellationToken = default) =>
        Run<IReadOnlyList<TopLevelWindow>>(() =>
        {
            var found = new List<TopLevelWindow>();
            foreach (var processId in processIds)
            {
                foreach (var (node, role) in _bus.TopLevels(processId))
                {
                    var items = Walk(node).Where(element => element.Role == AtSpiBus.RoleMenuItem).Select(element => element.Name).ToList();
                    var title = Read(() => _bus.NameOf(node)) ?? "";
                    found.Add(new TopLevelWindow(processId, 0, RoleName(role), title, (int)role, items));
                }
            }

            return found;
        }, cancellationToken);

    public Task<ScreenRect?> DialogButtonBoundsAsync(DialogContent dialog, string button, CancellationToken cancellationToken = default) =>
        Run<ScreenRect?>(() =>
            dialog.Native is AtSpiNode node && FindButton(node, button) is { } target ? Read(() => _bus.WindowExtents(target)) : null,
            cancellationToken);

    /// <summary>
    /// Sends <c>SIGTERM</c>, the session's end on Linux, and returns once the app exited or the session end's budget and a
    /// second passed, the point after which a session manager ends it; false when the app had no window.
    /// </summary>
    public async Task<bool> EndSessionAsync(int processId, CancellationToken cancellationToken = default)
    {
        if (await TitleAsync(processId, cancellationToken) is null)
        {
            return false;
        }

        return await _endSession(processId);
    }

    public Task<string> DumpAsync(IReadOnlyList<int> processIds, CancellationToken cancellationToken = default) =>
        Run(() =>
        {
            var text = new StringBuilder();
            foreach (var processId in processIds)
            {
                foreach (var (node, role) in _bus.TopLevels(processId))
                {
                    text.Append(CultureInfo.InvariantCulture, $"window {node} process {processId} role {RoleName(role)}\n");
                    foreach (var element in Walk(node))
                    {
                        var aria = element.AriaRole is { Length: > 0 } computed ? $" role={computed}" : "";
                        var state = (element.IsEnabled ? "" : " disabled") + (element.IsOffscreen ? " offscreen" : "");
                        text.Append(CultureInfo.InvariantCulture, $"  {new string(' ', element.Depth * 2)}{RoleName(element.Role)} ({element.Role}){aria} \"{element.Name}\"{state}\n");
                    }
                }
            }

            return text.ToString();
        }, cancellationToken);

    public void Dispose() => _bus.Dispose();

    private static string RoleName(uint role) => role switch
    {
        AtSpiBus.RoleFrame => "frame",
        AtSpiBus.RoleDialog => "dialog",
        AtSpiBus.RoleAlert => "alert",
        AtSpiBus.RoleWindow => "window",
        AtSpiBus.RolePushButton => "push button",
        AtSpiBus.RoleLabel => "label",
        AtSpiBus.RoleMenu => "menu",
        AtSpiBus.RoleMenuItem => "menu item",
        _ => "role " + role.ToString(CultureInfo.InvariantCulture),
    };

    /// <summary>The app's main window: its first top-level frame.</summary>
    private AtSpiNode? MainWindow(int processId)
    {
        foreach (var (node, role) in _bus.TopLevels(processId))
        {
            if (role == AtSpiBus.RoleFrame)
            {
                return node;
            }
        }

        return null;
    }

    /// <summary>
    /// A dialog's texts (its labels, in order: heading, then detail) and its push buttons, walked node by node. GTK 4's
    /// cache (<c>Cache.GetItems</c>) is not used: it holds only the nodes a client has already reached, so a new dialog's
    /// labels are missing from it until a walk (measured: its button and four of its generic nodes, no label, 3 s after it
    /// showed; pitfalls).
    /// </summary>
    private DialogContent? ReadDialog(AtSpiNode dialog)
    {
        var texts = new List<string>();
        var buttons = new List<string>();

        Visit(dialog, underButton: false);

        // A dialog whose texts or buttons are not there yet is not shown yet: the caller looks again.
        return buttons.Count == 0 || texts.Count == 0 ? null : new DialogContent(0, _appName, texts, buttons, dialog);

        void Visit(AtSpiNode parent, bool underButton)
        {
            var children = (Read(() => _bus.Children(parent)) ?? []).Select(node => (node, AtSpiBus.TryRead(() => _bus.Role(node)))).ToList();
            foreach (var (node, role) in children)
            {
                if (role is not { } known)
                {
                    continue;
                }

                var name = known is AtSpiBus.RoleLabel or AtSpiBus.RolePushButton ? Read(() => _bus.NameOf(node)) ?? "" : "";
                if (known == AtSpiBus.RolePushButton && name.Length > 0)
                {
                    buttons.Add(name);
                }
                else if (known == AtSpiBus.RoleLabel && name.Length > 0 && !underButton)
                {
                    // A button's own label is its name, not a text of the dialog.
                    texts.Add(name);
                }

                Visit(node, underButton || known == AtSpiBus.RolePushButton);
            }
        }
    }

    private AtSpiNode? FindButton(AtSpiNode dialog, string button) =>
        Walk(dialog).FirstOrDefault(element => element.Role == AtSpiBus.RolePushButton && element.Name == button)?.Node;

    private List<AtSpiElement> Resolve(int processId, ElementTarget target)
    {
        if (MainWindow(processId) is not { } window)
        {
            return [];
        }

        var query = target.Query;
        var found = Walk(window, query.TextOnly).Where(element => Matches(element, query)).ToList();
        return target.Index switch
        {
            null => found,
            -1 => found is [.., var last] ? [last] : [],
            { } index => index < found.Count ? [found[index]] : [],
        };
    }

    private static bool Matches(AtSpiElement element, BlackBoxQuery query)
    {
        string name;
        if (query.TextOnly)
        {
            if (element.Text is not { Length: > 0 } text || element.IsEditable)
            {
                return false;
            }

            name = text;
        }
        else
        {
            var byRole = element.AriaRole is { Length: > 0 } aria
                ? string.Equals(aria, query.AriaRole, StringComparison.OrdinalIgnoreCase)
                : NativeRole(query.AriaRole) is { } native && element.Role == native;
            if (!byRole)
            {
                return false;
            }

            name = element.Name;
        }

        if (query.Name is { } wanted
            && !(query.Exact ? string.Equals(name, wanted, StringComparison.Ordinal) : name.Contains(wanted, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        return (query.NameRegex is null || query.NameRegex.IsMatch(name)) && (query.Disabled is not { } disabled || element.IsEnabled != disabled);
    }

    /// <summary>The AT-SPI role a native (GTK) element of an ARIA role has; WebKit's elements carry their ARIA role instead.</summary>
    private static uint? NativeRole(string? aria) => aria switch
    {
        "button" => AtSpiBus.RolePushButton,
        "checkbox" => AtSpiBus.RoleCheckBox,
        "textbox" => AtSpiBus.RoleEntry,
        "link" => AtSpiBus.RoleLink,
        "menu" => AtSpiBus.RoleMenu,
        "menuitem" => AtSpiBus.RoleMenuItem,
        "dialog" => AtSpiBus.RoleDialog,
        _ => null,
    };

    /// <summary>Every node under <paramref name="top"/>, in tree order, read as it is now; nodes that went meanwhile are left out.</summary>
    private List<AtSpiElement> Walk(AtSpiNode top, bool withText = false)
    {
        var found = new List<AtSpiElement>();
        var pending = new Stack<(AtSpiNode Node, int Depth)>();
        foreach (var child in Read(() => _bus.Children(top)) ?? [])
        {
            pending.Push((child, 0));
        }

        Reverse(pending);
        while (pending.TryPop(out var item) && found.Count < 5_000)
        {
            if (ReadElement(item.Node, item.Depth, withText) is not { } element)
            {
                continue;
            }

            found.Add(element);
            var children = Read(() => _bus.Children(item.Node)) ?? [];
            for (var index = children.Count - 1; index >= 0; index--)
            {
                pending.Push((children[index], item.Depth + 1));
            }
        }

        return found;
    }

    private static void Reverse(Stack<(AtSpiNode, int)> stack)
    {
        var items = stack.ToArray();
        stack.Clear();
        foreach (var item in items)
        {
            stack.Push(item);
        }
    }

    private AtSpiElement? ReadElement(AtSpiNode node, int depth, bool withText)
    {
        try
        {
            var role = _bus.Role(node);
            var name = _bus.NameOf(node);
            var states = _bus.States(node);
            var attributes = _bus.Attributes(node);
            string? text = null;
            if (withText && _bus.Interfaces(node).Contains("org.a11y.atspi.Text"))
            {
                text = AtSpiText.Clean(_bus.Text(node));
            }

            return new AtSpiElement(node, depth, role, name, states, attributes.GetValueOrDefault("computed-role"), text);
        }
        catch (DBusException gone) when (gone.IsGone)
        {
            return null;
        }
    }

    private static ElementOutcome OnOne(int processId, ElementTarget target, Func<AtSpiElement, ElementOutcome> action, AtSpiAutomation self)
    {
        var found = self.Resolve(processId, target);
        return found.Count switch
        {
            0 => new ElementOutcome(ElementStatus.NotFound),
            1 => action(found[0]),
            _ => new ElementOutcome(ElementStatus.Ambiguous, Names: [.. found.Select(element => element.Name)]),
        };
    }

    private ElementOutcome OnOne(int processId, ElementTarget target, Func<AtSpiElement, ElementOutcome> action)
    {
        try
        {
            return OnOne(processId, target, action, this);
        }
        catch (DBusException gone) when (gone.IsGone)
        {
            // The element went between finding it and acting on it: not there, so the locator looks again.
            return new ElementOutcome(ElementStatus.NotFound);
        }
    }

    /// <summary>A read of a node that may have gone: null then.</summary>
    private static T? Read<T>(Func<T> read)
        where T : class
    {
        try
        {
            return read();
        }
        catch (DBusException gone) when (gone.IsGone)
        {
            return null;
        }
    }

    private static ScreenRect? Read(Func<ScreenRect> read) => AtSpiBus.TryRead(read);

    private static ulong? Read(Func<ulong> read) => AtSpiBus.TryRead(read);

    /// <summary>Runs a read on the thread pool (GDBus's synchronous calls block their thread, never the test's).</summary>
    private static Task<T> Run<T>(Func<T> work, CancellationToken cancellationToken) => Task.Run(work, cancellationToken);
}

/// <summary>One node as a walk read it.</summary>
[SupportedOSPlatform("linux")]
internal sealed record AtSpiElement(AtSpiNode Node, int Depth, uint Role, string Name, ulong States, string? AriaRole, string? Text)
{
    public bool IsEnabled => AtSpiBus.Has(States, AtSpiBus.StateEnabled) || AtSpiBus.Has(States, AtSpiBus.StateSensitive);

    public bool IsOffscreen => !AtSpiBus.Has(States, AtSpiBus.StateShowing);

    public bool IsEditable => AtSpiBus.Has(States, AtSpiBus.StateEditable);
}
