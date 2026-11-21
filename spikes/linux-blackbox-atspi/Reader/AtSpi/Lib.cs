using System.Runtime.InteropServices;
using Spike.BlackBoxAtSpi.Native;

namespace Spike.BlackBoxAtSpi.AtSpi;

/// <summary>
/// The same reads through libatspi (at-spi2-core's client library, 2.52), for the comparison: <c>atspi_init</c>, the
/// desktop (the registry's root), its applications by process id, their windows' names and roles, and an action.
/// Every <c>AtspiAccessible*</c> returned is owned (transfer full: <see cref="GLib.g_object_unref"/>); returned strings
/// are owned (<see cref="GLib.g_free"/>).
/// </summary>
internal static unsafe partial class Lib
{
    private const string Library = "libatspi.so.0";

    public static int Init() => atspi_init();

    /// <summary>The applications on the desktop whose process is <paramref name="processId"/> (owned references).</summary>
    public static List<nint> ApplicationsOf(int processId)
    {
        var desktop = atspi_get_desktop(0);
        var found = new List<nint>();
        try
        {
            nint error = 0;
            var count = atspi_accessible_get_child_count(desktop, &error);
            Check(error);
            for (var i = 0; i < count; i++)
            {
                var application = atspi_accessible_get_child_at_index(desktop, i, &error);
                if (Check(error) is not null || application == 0)
                {
                    continue;
                }

                var pid = atspi_accessible_get_process_id(application, &error);
                if (Check(error) is null && pid == processId)
                {
                    found.Add(application);
                }
                else
                {
                    GLib.g_object_unref(application);
                }
            }
        }
        finally
        {
            GLib.g_object_unref(desktop);
        }

        return found;
    }

    public static List<nint> Children(nint node)
    {
        nint error = 0;
        var count = atspi_accessible_get_child_count(node, &error);
        Check(error);
        var children = new List<nint>();
        for (var i = 0; i < count; i++)
        {
            var child = atspi_accessible_get_child_at_index(node, i, &error);
            if (Check(error) is null && child != 0)
            {
                children.Add(child);
            }
        }

        return children;
    }

    public static string Name(nint node)
    {
        nint error = 0;
        var name = atspi_accessible_get_name(node, &error);
        Check(error);
        return Take(name);
    }

    public static string RoleName(nint node)
    {
        nint error = 0;
        var name = atspi_accessible_get_role_name(node, &error);
        Check(error);
        return Take(name);
    }

    public static int Role(nint node)
    {
        nint error = 0;
        var role = atspi_accessible_get_role(node, &error);
        Check(error);
        return role;
    }

    /// <summary>Runs action <paramref name="index"/> of <paramref name="node"/>; false when it has no Action interface.</summary>
    public static bool DoAction(nint node, int index)
    {
        var action = atspi_accessible_get_action_iface(node);
        if (action == 0)
        {
            return false;
        }

        try
        {
            nint error = 0;
            var done = atspi_action_do_action(action, index, &error);
            return Check(error) is null && done != 0;
        }
        finally
        {
            GLib.g_object_unref(action);
        }
    }

    public static void Release(IEnumerable<nint> nodes)
    {
        foreach (var node in nodes)
        {
            GLib.g_object_unref(node);
        }
    }

    private static string? Check(nint error) => GLib.TakeError(error);

    private static string Take(nint text)
    {
        var value = Marshal.PtrToStringUTF8(text) ?? "";
        GLib.g_free(text);
        return value;
    }

    [LibraryImport(Library)]
    private static partial int atspi_init();

    [LibraryImport(Library)]
    private static partial nint atspi_get_desktop(int index);

    [LibraryImport(Library)]
    private static partial int atspi_accessible_get_child_count(nint accessible, nint* error);

    [LibraryImport(Library)]
    private static partial nint atspi_accessible_get_child_at_index(nint accessible, int index, nint* error);

    [LibraryImport(Library)]
    private static partial uint atspi_accessible_get_process_id(nint accessible, nint* error);

    [LibraryImport(Library)]
    private static partial nint atspi_accessible_get_name(nint accessible, nint* error);

    [LibraryImport(Library)]
    private static partial nint atspi_accessible_get_role_name(nint accessible, nint* error);

    [LibraryImport(Library)]
    private static partial int atspi_accessible_get_role(nint accessible, nint* error);

    [LibraryImport(Library)]
    private static partial nint atspi_accessible_get_action_iface(nint accessible);

    [LibraryImport(Library)]
    private static partial int atspi_action_do_action(nint action, int index, nint* error);
}
