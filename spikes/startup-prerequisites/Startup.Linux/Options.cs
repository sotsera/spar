using System.Globalization;

namespace Startup.Linux;

internal sealed class Options
{
    public string AppName { get; private set; } = "Spar Sample";
    public Version MinimumWebKit { get; private set; } = new(2, 40, 0);
    public string Ui { get; private set; } = "auto"; // auto | gtk | zenity | kdialog | yad | dbus | notify-send | xmessage | none
    public string? LogFile { get; private set; }
    public string? Label { get; private set; }
    public bool TryView { get; private set; }
    public bool SkipSandboxTrial { get; private set; }
    public bool Journal { get; private set; } = true;
    public int DialogTimeoutMs { get; private set; } // spike only: closes the GTK dialog by itself (Wayland runs have no input)

    public static Options Parse(string[] args)
    {
        var o = new Options();
        for (var i = 0; i < args.Length; i++)
        {
            string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"{args[i]} needs a value");
            switch (args[i])
            {
                case "--app-name": o.AppName = Next(); break;
                case "--min-webkit": o.MinimumWebKit = Version.Parse(Next()); break;
                case "--ui": o.Ui = Next(); break;
                case "--log": o.LogFile = Next(); break;
                case "--label": o.Label = Next(); break;
                case "--try-view": o.TryView = true; break;
                case "--no-sandbox-trial": o.SkipSandboxTrial = true; break;
                case "--no-journal": o.Journal = false; break;
                case "--dialog-timeout-ms": o.DialogTimeoutMs = int.Parse(Next(), CultureInfo.InvariantCulture); break;
                default: throw new ArgumentException($"unknown option {args[i]}");
            }
        }

        return o;
    }
}
