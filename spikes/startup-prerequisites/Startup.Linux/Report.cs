using System.Diagnostics;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace Startup.Linux;

// Shows a start-up problem: stderr, then the journal (native protocol, else /dev/log), then the first graphical way
// that works: GtkAlertDialog when GTK 4 is initialised, else zenity, kdialog (first on KDE), yad, a desktop
// notification through D-Bus (GIO) or notify-send, and xmessage. Nothing graphical is tried without a display.
internal static unsafe class Report
{
    private static nint s_loop;
    private static int s_button = -2;
    private static string s_error = "";
    private static nint s_cancellable;

    public static void Show(StartupProblem p, Options o, Checks checks, Log log)
    {
        var text = $"{o.AppName} cannot start: {p.Summary}\n{p.Fix}\nDetails: {p.Details}\nExit code {p.ExitCode}.\n";
        Console.Error.Write(text);
        log.Mark("stderr", ("isatty", Console.IsErrorRedirected ? "redirected" : "terminal"));
        if (o.Journal)
        {
            log.Mark("journal", ("result", Journal(o.AppName, text, p)));
        }

        if (o.Ui == "none" || p.Kind == ProblemKind.NoDisplay || !checks.DisplayInEnvironment)
        {
            log.Mark("graphical", ("skipped", o.Ui == "none" ? "ui none" : "no display"));
            return;
        }

        var desktop = Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP") ?? "";
        var order = o.Ui != "auto" ? [o.Ui]
            : desktop.Contains("KDE", StringComparison.OrdinalIgnoreCase)
                ? new[] { "gtk", "kdialog", "zenity", "yad", "dbus", "notify-send", "xmessage" }
                : ["gtk", "zenity", "kdialog", "yad", "dbus", "notify-send", "xmessage"];
        foreach (var way in order)
        {
            var started = log.Now;
            var (shown, detail) = way switch
            {
                "gtk" => checks.GtkReady ? ShowGtk(p, o) : (false, "GTK 4 not initialised"),
                "dbus" => checks.GlibLoaded ? Notify(p, o) : (false, "GLib not loaded"),
                _ => Helper(way, p, o, log),
            };
            log.Mark("graphical", ("way", way), ("shown", shown), ("detail", detail), ("ms", log.Now - started));
            if (shown)
            {
                return;
            }
        }
    }

    // --- journal: systemd's native protocol keeps the fields; /dev/log (RFC 3164) is the fallback.
    private static string Journal(string app, string text, StartupProblem p)
    {
        var id = new string(app.ToLowerInvariant().Select(c => char.IsAsciiLetterOrDigit(c) ? c : '-').ToArray());
        try
        {
            using var s = new Socket(AddressFamily.Unix, SocketType.Dgram, ProtocolType.Unspecified);
            var payload = new MemoryStream();
            void Field(string key, string value)
            {
                var bytes = Encoding.UTF8.GetBytes(value);
                if (value.Contains('\n'))
                {
                    payload.Write(Encoding.ASCII.GetBytes(key + "\n"));
                    payload.Write(BitConverter.GetBytes((ulong)bytes.Length)); // little-endian on x64
                    payload.Write(bytes);
                    payload.WriteByte((byte)'\n');
                }
                else
                {
                    payload.Write(Encoding.UTF8.GetBytes($"{key}={value}\n"));
                }
            }

            Field("MESSAGE", text.TrimEnd());
            Field("PRIORITY", "3");
            Field("SYSLOG_IDENTIFIER", id);
            Field("SPAR_STARTUP_PROBLEM", p.Kind.ToString());
            Field("SPAR_EXIT_CODE", p.ExitCode.ToString(System.Globalization.CultureInfo.InvariantCulture));
            s.SendTo(payload.ToArray(), new UnixDomainSocketEndPoint("/run/systemd/journal/socket"));
            return "journal-socket";
        }
        catch (SocketException e)
        {
            try
            {
                using var s = new Socket(AddressFamily.Unix, SocketType.Dgram, ProtocolType.Unspecified);
                var line = $"<11>{id}[{LibC.getpid()}]: {text.TrimEnd().Replace('\n', ' ')}"; // user.err
                s.SendTo(Encoding.UTF8.GetBytes(line), new UnixDomainSocketEndPoint("/dev/log"));
                return $"dev-log (journal socket: {e.SocketErrorCode})";
            }
            catch (SocketException e2)
            {
                return $"none (journal socket: {e.SocketErrorCode}; /dev/log: {e2.SocketErrorCode})";
            }
        }
    }

    // --- GtkAlertDialog (GTK 4.10+), built with g_object_new_with_properties: gtk_alert_dialog_new is variadic.
    private static (bool, string) ShowGtk(StartupProblem p, Options o)
    {
        var names = new[] { "message", "detail", "buttons", "cancel-button", "default-button", "modal" };
        var utf8 = names.Select(n => Marshal.StringToCoTaskMemUTF8(n)).ToArray();
        var close = Marshal.StringToCoTaskMemUTF8("Close");
        var strv = stackalloc nint[2] { close, 0 };
        var values = stackalloc GObject.GValue[names.Length];
        new Span<GObject.GValue>(values, names.Length).Clear();
        GObject.g_value_init(&values[0], GObject.TypeString);
        GObject.g_value_set_string(&values[0], $"{o.AppName} can't start");
        GObject.g_value_init(&values[1], GObject.TypeString);
        GObject.g_value_set_string(&values[1], $"{p.Summary}\n\n{p.Fix}\n\nDetails: {p.Details}");
        GObject.g_value_init(&values[2], GObject.g_strv_get_type());
        GObject.g_value_set_boxed(&values[2], (nint)strv); // copied
        GObject.g_value_init(&values[3], GObject.TypeInt);
        GObject.g_value_set_int(&values[3], 0);
        GObject.g_value_init(&values[4], GObject.TypeInt);
        GObject.g_value_set_int(&values[4], 0);
        GObject.g_value_init(&values[5], GObject.TypeBoolean);
        GObject.g_value_set_boolean(&values[5], 1);
        var namePointers = stackalloc byte*[names.Length];
        for (var i = 0; i < names.Length; i++)
        {
            namePointers[i] = (byte*)utf8[i];
        }

        var dialog = GObject.g_object_new_with_properties(Gtk.gtk_alert_dialog_get_type(), (uint)names.Length, namePointers, values);
        for (var i = 0; i < names.Length; i++)
        {
            GObject.g_value_unset(&values[i]);
            Marshal.FreeCoTaskMem(utf8[i]);
        }

        Marshal.FreeCoTaskMem(close);
        if (dialog == 0)
        {
            return (false, "g_object_new_with_properties returned NULL");
        }

        s_loop = GLib.g_main_loop_new(0, 0);
        s_cancellable = Gio.g_cancellable_new();
        if (o.DialogTimeoutMs > 0)
        {
            GLib.g_timeout_add((uint)o.DialogTimeoutMs, &OnTimeout, 0);
        }

        Gtk.gtk_alert_dialog_choose(dialog, 0, s_cancellable, &OnChosen, 0);
        GLib.g_main_loop_run(s_loop);
        GObject.g_object_unref(dialog);
        return (true, $"button={s_button} {s_error}".Trim());
    }

    [UnmanagedCallersOnly]
    private static void OnChosen(nint source, nint result, nint data)
    {
        nint error = 0;
        s_button = Gtk.gtk_alert_dialog_choose_finish(source, result, &error);
        if (error != 0)
        {
            s_error = GLib.Message(error);
            GLib.g_error_free(error);
        }

        GLib.g_main_loop_quit(s_loop);
    }

    [UnmanagedCallersOnly]
    private static int OnTimeout(nint data)
    {
        Gio.g_cancellable_cancel(s_cancellable);
        return 0; // G_SOURCE_REMOVE
    }

    // --- org.freedesktop.Notifications.Notify (susssasa{sv}i), built from non-variadic GVariant constructors.
    private static (bool, string) Notify(StartupProblem p, Options o)
    {
        nint error = 0;
        var bus = Gio.g_bus_get_sync(2, 0, &error); // G_BUS_TYPE_SESSION
        if (bus == 0)
        {
            var message = GLib.Message(error);
            GLib.g_error_free(error);
            return (false, "no session bus: " + message);
        }

        var hintType = GLib.g_variant_type_new("{sv}");
        var urgency = GLib.g_variant_new_dict_entry(GLib.g_variant_new_string("urgency"), GLib.g_variant_new_variant(GLib.g_variant_new_byte(2)));
        var children = stackalloc nint[8]
        {
            GLib.g_variant_new_string(o.AppName),
            GLib.g_variant_new_uint32(0),
            GLib.g_variant_new_string("dialog-error"),
            GLib.g_variant_new_string($"{o.AppName} can't start"),
            GLib.g_variant_new_string($"{p.Summary}\n{p.Fix}"),
            GLib.g_variant_new_strv(0, 0),
            GLib.g_variant_new_array(hintType, &urgency, 1),
            GLib.g_variant_new_int32(0), // never expires; critical notifications stay until dismissed
        };
        var parameters = GLib.g_variant_new_tuple(children, 8);
        GLib.g_variant_type_free(hintType);
        var reply = Gio.g_dbus_connection_call_sync(bus, "org.freedesktop.Notifications", "/org/freedesktop/Notifications",
            "org.freedesktop.Notifications", "Notify", parameters, 0, 0, 3000, 0, &error);
        GObject.g_object_unref(bus);
        if (reply == 0)
        {
            var message = GLib.Message(error);
            GLib.g_error_free(error);
            return (false, "Notify failed: " + message);
        }

        var idValue = GLib.g_variant_get_child_value(reply, 0);
        var id = GLib.g_variant_get_uint32(idValue);
        GLib.g_variant_unref(idValue);
        GLib.g_variant_unref(reply);
        return (true, $"notification id {id}");
    }

    // --- external helpers. A helper that ends with an error within 300 ms is taken as "could not show".
    private static (bool, string) Helper(string way, StartupProblem p, Options o, Log log)
    {
        var tool = way == "notify-send" ? "notify-send" : way;
        var path = (Environment.GetEnvironmentVariable("PATH") ?? "/usr/bin").Split(':').Select(d => Path.Combine(d, tool)).FirstOrDefault(File.Exists);
        if (path is null)
        {
            return (false, "not installed");
        }

        var title = $"{o.AppName} can't start";
        var body = $"{p.Summary}\n\n{p.Fix}\n\nDetails: {p.Details}";
        var args = way switch
        {
            "zenity" => new[] { "--error", "--no-markup", "--title", title, "--text", body, "--width", "520" },
            "kdialog" => ["--title", o.AppName, "--error", body],
            "yad" => ["--title", title, "--image", "dialog-error", "--text", Escape(body), "--button", "Close:0", "--width", "520", "--center"],
            "notify-send" => ["--urgency", "critical", "--app-name", o.AppName, "--icon", "dialog-error", title, $"{p.Summary}\n{p.Fix}"],
            "xmessage" => ["-center", "-title", title, "-buttons", "Close:0", "-default", "Close", $"{title}\n\n{body}"],
            _ => [],
        };
        var info = new ProcessStartInfo(path) { UseShellExecute = false, RedirectStandardError = true };
        foreach (var a in args)
        {
            info.ArgumentList.Add(a);
        }

        var clock = Stopwatch.StartNew();
        using var process = Process.Start(info)!;
        var error = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        var stderr = error.Result.Trim();
        log.Mark("helper-exit", ("tool", tool), ("exit", process.ExitCode), ("ms", clock.Elapsed.TotalMilliseconds), ("stderr", stderr));
        // notify-send returns at once either way, so any error counts; a dialog closed by its window button may exit
        // non-zero after the user saw it.
        var failed = process.ExitCode != 0 && (way == "notify-send" || clock.ElapsedMilliseconds < 300);
        return (!failed, $"exit {process.ExitCode} after {clock.ElapsedMilliseconds} ms {stderr}".Trim());
    }

    // yad reads Pango markup in --text.
    private static string Escape(string text) => text.Replace("&", "&amp;", StringComparison.Ordinal).Replace("<", "&lt;", StringComparison.Ordinal)
        .Replace(">", "&gt;", StringComparison.Ordinal);
}
