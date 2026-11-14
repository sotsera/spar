namespace Checks.Desktop;

internal enum Hosting { Windowed, Visual }

internal enum Frame { Standard, Custom }

internal enum Buttons { System, Page }

internal enum SinkMode { None, Transparent, Direct }

internal sealed class Options
{
    private static readonly System.Globalization.CultureInfo s_inv = System.Globalization.CultureInfo.InvariantCulture;

    public Hosting Hosting { get; private set; } = Hosting.Windowed;
    public Frame Frame { get; private set; } = Frame.Custom;
    public Buttons Buttons { get; private set; } = Buttons.Page;
    public SinkMode Sink { get; private set; } = SinkMode.Direct;
    public bool NonClientSupport { get; private set; } = true;
    public bool CaptionDrag { get; private set; }
    public bool UiaProvider { get; private set; } = true;
    public bool NcrForward { get; private set; } = true;
    public bool NcrHost { get; private set; }
    public bool NoCaptionStyle { get; private set; }
    public uint SinkStyle { get; private set; } = 0x00000020; // WS_EX_TRANSPARENT
    public bool Dark { get; private set; } = true;
    public string Placement { get; private set; } = "offscreen";
    public int WidthDip { get; private set; } = 600;
    public int HeightDip { get; private set; } = 320;
    public int BarDip { get; private set; } = 32;
    public int TopMargin { get; private set; } = -1;
    public string Label { get; private set; } = "run";
    public string Out { get; private set; } = "artifacts/windows-sandbox-checks/runs";
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

    // Hit-test cache: "css" keeps the page's CSS pixels and scales them by the window DPI at hit-test time; "px"
    // converts them to client pixels when the layout message arrives (as spikes/windows-titlebar did).
    public string Cache { get; private set; } = "css";

    // Windows' own caption buttons when maximized: none (DwmDefWindowProc only), tbi (host hit test from
    // WM_GETTITLEBARINFOEX rectangles), bounds (from DWMWA_CAPTION_BUTTON_BOUNDS).
    public string DwmFix { get; private set; } = "none";

    // Accessibility experiments.
    public string Tbi { get; private set; } = "default";      // default | invisible | page: answer WM_GETTITLEBARINFOEX
    public string NcProvider { get; private set; } = "off";   // off | refuse: server-side provider with RefuseNonClientSupport
    public string Annotate { get; private set; } = "none";    // none | panes | titlebar | both: dynamic annotation
    public string Style { get; private set; } = "overlapped"; // overlapped | nosysmenu | nominmax

    // Media checks.
    public string Page { get; private set; } = "index.html";
    public string PermissionsPolicy { get; private set; } = "none"; // none | deny | self | camself
    public string Permission { get; private set; } = "observe";     // observe | allow | deny | default
    public string Saves { get; private set; } = "keep";             // keep | true | false
    public bool FakeMedia { get; private set; }
    public bool FakeUi { get; private set; }

    public string? PermissionsPolicyHeader => PermissionsPolicy switch
    {
        "deny" => "camera=(), microphone=()",
        "self" => "camera=(self), microphone=(self)",
        "camself" => "camera=(self), microphone=()",
        _ => null,
    };

    public string BrowserArguments => string.Join(' ',
        new[] { FakeMedia ? "--use-fake-device-for-media-stream" : null, FakeUi ? "--use-fake-ui-for-media-stream" : null }
            .Where(a => a is not null));

    public static Options Parse(string[] args)
    {
        var o = new Options();
        for (var i = 0; i < args.Length; i++)
        {
            string Next() => args[++i];
            int NextInt() => int.Parse(args[++i], s_inv);
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
                    o.WidthDip = int.Parse(parts[0], s_inv);
                    o.HeightDip = int.Parse(parts[1], s_inv);
                    break;
                case "--bar": o.BarDip = NextInt(); break;
                case "--top-margin": o.TopMargin = NextInt(); break;
                case "--label": o.Label = Next(); break;
                case "--out": o.Out = Next(); break;
                case "--profile-root": o.ProfileRoot = Next(); break;
                case "--profile": o.Profile = Next(); break;
                case "--browser-folder": o.BrowserFolder = Next(); break;
                case "--fresh": o.Fresh = true; break;
                case "--mode": o.Mode = Next(); break;
                case "--hold": o.HoldMs = NextInt(); break;
                case "--capture": o.CaptureMs = NextInt(); break;
                case "--after-ready": o.AfterReadyMs = NextInt(); break;
                case "--live": o.LiveSwitches = NextInt(); break;
                case "--steps": o.DriveSteps = Next(); break;
                case "--cache": o.Cache = Next(); break;
                case "--dwm-fix": o.DwmFix = Next(); break;
                case "--tbi": o.Tbi = Next(); break;
                case "--nc-provider": o.NcProvider = Next(); break;
                case "--annotate": o.Annotate = Next(); break;
                case "--style": o.Style = Next(); break;
                case "--page": o.Page = Next(); break;
                case "--pp": o.PermissionsPolicy = Next(); break;
                case "--perm": o.Permission = Next(); break;
                case "--saves": o.Saves = Next(); break;
                case "--fake-media": o.FakeMedia = true; break;
                case "--fake-ui": o.FakeUi = true; break;
                default: throw new ArgumentException($"unknown argument {args[i]}");
            }
        }

        return o;
    }

    public string Describe() =>
        $"hosting={Hosting} frame={Frame} buttons={Buttons} sink={Sink} cache={Cache} dwmFix={DwmFix} tbi={Tbi} ncProvider={NcProvider} "
        + $"annotate={Annotate} style={Style} theme={(Dark ? "dark" : "light")} page={Page} pp={PermissionsPolicy} perm={Permission} "
        + $"saves={Saves} browserArgs=[{BrowserArguments}]";
}
