using System.Globalization;

namespace Startup.Win;

internal enum Stage
{
    Version,     // OS and runtime version only (no browser process, no window)
    Environment, // plus CreateCoreWebView2EnvironmentWithOptions (starts the browser process; no window)
    Controller,  // plus a window and CreateCoreWebView2Controller (Windows Sandbox only)
}

internal sealed class Options
{
    public string AppName { get; private set; } = "Spar Sample";
    public string? RuntimeFolder { get; private set; }
    public string MinimumRuntime { get; private set; } = "120.0.2210.55";
    public int MinimumOsBuild { get; private set; } = 17763;
    public string Ui { get; private set; } = "auto";            // auto | taskdialog | messagebox | none
    public string Stderr { get; private set; } = "auto";        // auto | off
    public bool EventLog { get; private set; }
    public string? LogFile { get; private set; }
    public string? Profile { get; private set; }
    public Stage Stage { get; private set; } = Stage.Version;
    public int TimeoutMs { get; private set; } = 30_000;
    public bool KillBrowser { get; private set; }
    public int HoldMs { get; private set; }
    public string? Label { get; private set; }

    public static Options Parse(string[] args)
    {
        var o = new Options();
        for (var i = 0; i < args.Length; i++)
        {
            string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"{args[i]} needs a value");
            switch (args[i])
            {
                case "--app-name": o.AppName = Next(); break;
                case "--runtime": o.RuntimeFolder = Next(); break;
                case "--min-runtime": o.MinimumRuntime = Next(); break;
                case "--min-os-build": o.MinimumOsBuild = int.Parse(Next(), CultureInfo.InvariantCulture); break;
                case "--ui": o.Ui = Next(); break;
                case "--stderr": o.Stderr = Next(); break;
                case "--eventlog": o.EventLog = true; break;
                case "--log": o.LogFile = Next(); break;
                case "--profile": o.Profile = Next(); break;
                case "--stage": o.Stage = Enum.Parse<Stage>(Next(), ignoreCase: true); break;
                case "--timeout-ms": o.TimeoutMs = int.Parse(Next(), CultureInfo.InvariantCulture); break;
                case "--kill-browser": o.KillBrowser = true; break;
                case "--hold-ms": o.HoldMs = int.Parse(Next(), CultureInfo.InvariantCulture); break;
                case "--label": o.Label = Next(); break;
                default: throw new ArgumentException($"unknown option {args[i]}");
            }
        }

        if (o.Stage != Stage.Version && o.Profile is null)
        {
            throw new ArgumentException("--profile is required from the environment stage on (never the default user-data folder)");
        }

        return o;
    }
}
