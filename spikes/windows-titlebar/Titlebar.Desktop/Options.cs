namespace Titlebar.Desktop;

internal enum Hosting { Windowed, Visual }

internal enum Frame { Standard, Custom }

internal enum Buttons { System, Page }

internal enum SinkMode { None, Transparent, Direct }

internal sealed class Options
{
    public Hosting Hosting { get; private set; } = Hosting.Windowed;
    public Frame Frame { get; private set; } = Frame.Custom;
    public Buttons Buttons { get; private set; } = Buttons.Page;
    public SinkMode Sink { get; private set; } = SinkMode.None;
    public bool NonClientSupport { get; private set; } = true;
    public bool CaptionDrag { get; private set; }
    public bool UiaProvider { get; private set; } = true;
    public bool NcrForward { get; private set; } = true;
    public bool NcrHost { get; private set; }
    public bool NoCaptionStyle { get; private set; }
    public uint SinkStyle { get; private set; } = 0x00000020; // WS_EX_TRANSPARENT
    public bool Dark { get; private set; } = true;
    public string Placement { get; private set; } = "offscreen";
    public int WidthDip { get; private set; } = 900;
    public int HeightDip { get; private set; } = 600;
    public int BarDip { get; private set; } = 32;
    public int TopMargin { get; private set; } = -1;
    public string Label { get; private set; } = "run";
    public string Out { get; private set; } = "artifacts/windows-titlebar/runs";
    public string? ProfileRoot { get; private set; }
    public string? BrowserFolder { get; private set; }
    public string Profile { get; private set; } = "default";
    public bool Fresh { get; private set; }
    public string? Mode { get; private set; }
    public int HoldMs { get; private set; }
    public int CaptureMs { get; private set; }
    public int AfterReadyMs { get; private set; } = 300;
    public int LiveSwitches { get; private set; }
    public string? DriveSteps { get; private set; }

    public static Options Parse(string[] args)
    {
        var o = new Options();
        for (var i = 0; i < args.Length; i++)
        {
            string Next() => args[++i];
            switch (args[i])
            {
                case "--hosting": o.Hosting = Next() == "visual" ? Hosting.Visual : Hosting.Windowed; break;
                case "--frame": o.Frame = Next() == "standard" ? Frame.Standard : Frame.Custom; break;
                case "--buttons": o.Buttons = Next() == "system" ? Buttons.System : Buttons.Page; break;
                case "--sink": o.Sink = Next() switch { "transparent" => SinkMode.Transparent, "direct" => SinkMode.Direct, _ => SinkMode.None }; break;
                case "--sink-style": o.SinkStyle = Next() == "noredirection" ? 0x00200000u : 0x00000020u; break;
                case "--ncr":
                    var ncr = Next();
                    o.NcrForward = ncr == "forward";
                    o.NcrHost = ncr == "host";
                    break;
                case "--no-caption-style": o.NoCaptionStyle = true; break;
                case "--uia": o.UiaProvider = Next() != "off"; break;
                case "--capdrag": o.CaptionDrag = true; break;
                case "--nc": o.NonClientSupport = Next() != "off"; break;
                case "--theme": o.Dark = Next() != "light"; break;
                case "--placement": o.Placement = Next(); break;
                case "--size":
                    var parts = Next().Split('x');
                    o.WidthDip = int.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture);
                    o.HeightDip = int.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture);
                    break;
                case "--bar": o.BarDip = int.Parse(Next(), System.Globalization.CultureInfo.InvariantCulture); break;
                case "--top-margin": o.TopMargin = int.Parse(Next(), System.Globalization.CultureInfo.InvariantCulture); break;
                case "--label": o.Label = Next(); break;
                case "--out": o.Out = Next(); break;
                case "--profile-root": o.ProfileRoot = Next(); break;
                case "--profile": o.Profile = Next(); break;
                case "--browser-folder": o.BrowserFolder = Next(); break;
                case "--fresh": o.Fresh = true; break;
                case "--mode": o.Mode = Next(); break;
                case "--hold": o.HoldMs = int.Parse(Next(), System.Globalization.CultureInfo.InvariantCulture); break;
                case "--capture": o.CaptureMs = int.Parse(Next(), System.Globalization.CultureInfo.InvariantCulture); break;
                case "--after-ready": o.AfterReadyMs = int.Parse(Next(), System.Globalization.CultureInfo.InvariantCulture); break;
                case "--live": o.LiveSwitches = int.Parse(Next(), System.Globalization.CultureInfo.InvariantCulture); break;
                case "--steps": o.DriveSteps = Next(); break;
                default: throw new ArgumentException($"unknown argument {args[i]}");
            }
        }

        return o;
    }

    public string Describe() =>
        $"hosting={Hosting} frame={Frame} buttons={Buttons} sink={Sink} capdrag={CaptionDrag} ncr={(NcrForward ? "forward" : NcrHost ? "host" : "default")} nc={NonClientSupport} noCaptionStyle={NoCaptionStyle} theme={(Dark ? "dark" : "light")}";
}
