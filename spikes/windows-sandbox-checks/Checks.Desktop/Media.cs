using System.Text.RegularExpressions;

namespace Checks.Desktop;

// Camera and microphone probes, driven from the host. Windows Sandbox only (configured without video and audio
// input): it refuses to run anywhere else, so the host machine's camera and microphone are never reached.
internal static class Media
{
    private const string AppOrigin = "https://app.localhost";
    private static int s_id;

    public static void Run(Host host)
    {
        var log = host.Log;
        if (!DisplayScale.InSandbox)
        {
            log.Mark("media-refused", ("user", "not the Windows Sandbox account"));
            return;
        }

        Drive.Init(host);
        var steps = (host.Options.DriveSteps ?? "enum,query,gum:video,gum:audio,list").Split(',');
        foreach (var step in steps)
        {
            try
            {
                Step(host, step);
            }
            catch (Exception e)
            {
                log.Mark("step-error", ("name", step), ("error", e.ToString()));
            }
        }
    }

    private static void Step(Host host, string step)
    {
        var log = host.Log;
        var parts = step.Split(':');
        log.Mark("step", ("name", step));
        switch (parts[0])
        {
            case "enum": Page(host, "enumerate", "", 5000); break;
            case "query": Page(host, "query", "", 5000); break;
            case "frames": Page(host, "frames", "", 5000); break;
            case "tracks": Page(host, "tracks", "", 5000); break;
            case "stop": Page(host, "stop", "", 5000); break;
            case "gum":
                {
                    var constraints = parts[1] switch
                    {
                        "video" => "{ video: true }",
                        "audio" => "{ audio: true }",
                        _ => "{ video: true, audio: true }",
                    };
                    var before = Drive.TopLevelWindows();
                    int eventsBefore;
                    lock (host.PermissionEvents)
                    {
                        eventsBefore = host.PermissionEvents.Count;
                    }

                    var result = Page(host, "gum", ", " + constraints + ", 8000", 12000);
                    Thread.Sleep(500);
                    var after = Drive.TopLevelWindows();
                    string[] events;
                    lock (host.PermissionEvents)
                    {
                        events = [.. host.PermissionEvents.Skip(eventsBefore)];
                    }

                    log.Mark("result", ("step", step), ("page", result), ("permissionRequested", events.Length), ("events", string.Join(" | ", events)),
                        ("newTopLevelWindows", string.Join(" | ", after.Except(before))));
                    Drive.Shot(step.Replace(':', '-'));
                    break;
                }

            case "list":
                {
                    var done = new ManualResetEventSlim();
                    string? text = null;
                    host.Invoke(() => host.ListPermissions(t =>
                    {
                        text = t;
                        done.Set();
                    }));
                    done.Wait(5000);
                    log.Mark("result", ("step", "list"), ("settings", text ?? "timeout"));
                    break;
                }

            case "set":
                {
                    // set:<kind>:<state>[:origin], kind camera=2, microphone=1; state default=0, allow=1, deny=2.
                    var kind = parts[1] == "camera" ? 2 : 1;
                    var state = parts[2] switch { "allow" => 1, "deny" => 2, _ => 0 };
                    var origin = parts.Length > 3 ? string.Join(':', parts[3..]) : AppOrigin;
                    var done = new ManualResetEventSlim();
                    string? text = null;
                    host.Invoke(() => host.SetPermission(kind, origin, state, t =>
                    {
                        text = t;
                        done.Set();
                    }));
                    done.Wait(5000);
                    log.Mark("result", ("step", step), ("origin", origin), ("set", text ?? "timeout"));
                    break;
                }

            case "wait":
                Thread.Sleep(int.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture));
                break;
            case "reload":
                {
                    host.Invoke(() => host.Navigate(host.Options.Page));
                    Thread.Sleep(2500);
                    log.Mark("result", ("step", "reload"));
                    break;
                }

            case "shot":
                Drive.Shot(parts.Length > 1 ? parts[1] : "shot");
                break;
        }
    }

    // Calls window.media.<op>(id ...) in the page and waits for its reply message.
    private static string Page(Host host, string op, string args, int timeoutMs)
    {
        var id = Interlocked.Increment(ref s_id);
        host.Invoke(() => host.ExecuteScript($"window.media.{op}({id}{args})", _ => { }));
        var marker = IdPattern(id);
        var end = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < end)
        {
            lock (host.PageEvents)
            {
                var hit = host.PageEvents.FirstOrDefault(e => e.Contains("\"type\":\"media\"", StringComparison.Ordinal) && marker.IsMatch(e));
                if (hit is not null)
                {
                    return hit;
                }
            }

            Thread.Sleep(50);
        }

        return "no reply";
    }

    private static Regex IdPattern(int id) => new($"\"id\":{id}[,}}]");
}
