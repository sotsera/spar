using System.Diagnostics;

namespace Spike.BlackBoxAtSpi.AtSpi;

/// <summary>A dialog as read: its window, title, text parts and buttons (with their refs), and the D-Bus calls the read took.</summary>
internal sealed record Dialog(Ref Window, string Title, string Role, List<string> Texts, List<(string Name, Ref Node)> Buttons, int Calls);

/// <summary>The reads the black box needs, over <see cref="Bus"/>: an app's top-level windows by process id, a dialog whole.</summary>
internal static class Reading
{
    // AtspiRole (atspi-constants.h, 2.52).
    public const uint RoleAlert = 2;
    public const uint RoleDialog = 16;
    public const uint RoleFrame = 23;
    public const uint RoleLabel = 29;
    public const uint RolePushButton = 43;
    public const uint RoleWindow = 69;

    // AtspiStateType bits.
    public const int StateActive = 1;
    public const int StateModal = 16;
    public const int StateShowing = 25;
    public const int StateVisible = 30;

    /// <summary>The application roots of <paramref name="processId"/>: its bus names, asked of the bus daemon (never of the app).</summary>
    public static List<Ref> ApplicationsOf(Bus bus, int processId) =>
        bus.NamesOf(processId).Select(name => new Ref(name, Bus.RootPath)).ToList();

    /// <summary>The top-level windows of <paramref name="processId"/>, each with its role and name.</summary>
    public static List<(Ref Node, uint Role, string Name)> WindowsOf(Bus bus, int processId)
    {
        var windows = new List<(Ref, uint, string)>();
        foreach (var application in ApplicationsOf(bus, processId))
        {
            List<Ref> children;
            try
            {
                children = bus.Children(application);
            }
            catch (InvalidOperationException)
            {
                // A name that serves no accessible root (the app's other connections, a bus name of WebKit's).
                continue;
            }

            foreach (var child in children)
            {
                try
                {
                    windows.Add((child, bus.Role(child), bus.Name(child)));
                }
                catch (InvalidOperationException)
                {
                    // Destroyed between the two calls ("Object does not exist at path"): it is gone.
                }
            }
        }

        return windows;
    }

    /// <summary>The first window of <paramref name="processId"/> that is a dialog (alert or dialog role), read whole by walking its tree.</summary>
    public static Dialog? FindDialog(Bus bus, int processId, string? notTitle = null)
    {
        foreach (var (node, role, name) in WindowsOf(bus, processId))
        {
            if (role is RoleAlert or RoleDialog || (notTitle is not null && name != notTitle))
            {
                return ReadDialog(bus, node, role, name);
            }
        }

        return null;
    }

    /// <summary>A dialog's labels and buttons, in tree order, one call per property (no cache).</summary>
    public static Dialog ReadDialog(Bus bus, Ref window, uint role, string title)
    {
        var texts = new List<string>();
        var buttons = new List<(string, Ref)>();
        var calls = 0;
        var pending = new Stack<(Ref Node, uint Role)>();
        pending.Push((window, role));
        while (pending.TryPop(out var entry))
        {
            var (node, nodeRole) = entry;
            if (nodeRole == RoleLabel)
            {
                var text = bus.Name(node);
                calls++;
                if (text.Length == 0)
                {
                    text = bus.Text(node);
                    calls++;
                }

                texts.Add(text);
            }
            else if (nodeRole == RolePushButton)
            {
                buttons.Add((bus.Name(node), node));
                calls++;
                continue;
            }

            var children = bus.Children(node);
            calls++;
            for (var i = children.Count - 1; i >= 0; i--)
            {
                pending.Push((children[i], bus.Role(children[i])));
                calls++;
            }
        }

        return new Dialog(window, title, bus.RoleName(window), texts, buttons, calls + 1);
    }

    /// <summary>The same dialog from the application's cache in one call (<c>org.a11y.atspi.Cache.GetItems</c>); null when not served.</summary>
    public static Dialog? ReadDialogFromCache(Bus bus, Ref window, string title, out string? error)
    {
        var items = bus.CacheItems(window.Name, out error);
        if (items is null)
        {
            return null;
        }

        var children = new Dictionary<string, List<(int Index, string Path, uint Role, string Name)>>();
        foreach (var raw in items)
        {
            var item = (object?[])raw!;
            var self = (string)((object?[])item[0]!)[1]!;
            var parent = (string)((object?[])item[2]!)[1]!;
            var index = (int)item[3]!;
            var name = (string)item[6]!;
            var role = (uint)item[7]!;
            if (!children.TryGetValue(parent, out var list))
            {
                children[parent] = list = [];
            }

            list.Add((index, self, role, name));
        }

        var texts = new List<string>();
        var buttons = new List<(string, Ref)>();
        void Visit(string path)
        {
            if (!children.TryGetValue(path, out var list))
            {
                return;
            }

            foreach (var (_, child, role, name) in list.OrderBy(c => c.Index))
            {
                if (role == RoleLabel)
                {
                    texts.Add(name);
                }
                else if (role == RolePushButton)
                {
                    buttons.Add((name, new Ref(window.Name, child)));
                    continue;
                }

                Visit(child);
            }
        }

        Visit(window.Path);
        return new Dialog(window, title, "(cache)", texts, buttons, 1);
    }

    /// <summary>A text dump of <paramref name="root"/>'s tree (role, name, interfaces), at most <paramref name="limit"/> nodes.</summary>
    public static (string Text, int Nodes) Dump(Bus bus, Ref root, int limit = 400)
    {
        var lines = new List<string>();
        var nodes = 0;
        void Visit(Ref node, int depth)
        {
            if (nodes++ >= limit)
            {
                return;
            }

            string line;
            try
            {
                line = $"{new string(' ', depth * 2)}[{bus.RoleName(node)}] \"{bus.Name(node)}\" {{{string.Join(",", bus.Interfaces(node).Select(i => i.Replace("org.a11y.atspi.", "")))}}} {node.Path}";
            }
            catch (InvalidOperationException exception)
            {
                lines.Add($"{new string(' ', depth * 2)}! {node}: {exception.Message}");
                return;
            }

            lines.Add(line);
            foreach (var child in bus.Children(node))
            {
                Visit(child, depth + 1);
            }
        }

        Visit(root, 0);
        return (string.Join('\n', lines), nodes);
    }

    public static bool Has(ulong states, int bit) => (states & (1UL << bit)) != 0;

    public static double Ms(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;
}
