namespace D8.App;

internal enum WindowMode
{
    Normal,
    Offscreen,
    Hidden,
    ControllerHidden,
    Minimized,
}

internal sealed class Options
{
    public string Mode { get; private set; } = "show";

    public WindowMode Window { get; private set; } = WindowMode.Normal;

    public string ProfileDirectory { get; private set; } = "";

    public string OutputDirectory { get; private set; } = "";

    public int Iterations { get; private set; } = 20;

    public bool DevtoolsUiEnabled { get; private set; } = true;

    public static Options Parse(string[] args)
    {
        var options = new Options();
        var spikeRoot = FindSpikeRoot();
        var profileName = "default";

        for (var i = 0; i < args.Length; i++)
        {
            var value = i + 1 < args.Length ? args[i + 1] : "";
            switch (args[i])
            {
                case "--mode":
                    options.Mode = value;
                    i++;
                    break;
                case "--window":
                    options.Window = value switch
                    {
                        "normal" => WindowMode.Normal,
                        "offscreen" => WindowMode.Offscreen,
                        "hidden" => WindowMode.Hidden,
                        "controller-hidden" => WindowMode.ControllerHidden,
                        "minimized" => WindowMode.Minimized,
                        _ => throw new ArgumentException($"Unknown window mode '{value}'."),
                    };
                    i++;
                    break;
                case "--profile":
                    profileName = value;
                    i++;
                    break;
                case "--iterations":
                    options.Iterations = int.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
                    i++;
                    break;
                case "--devtools-ui":
                    options.DevtoolsUiEnabled = value != "off";
                    i++;
                    break;
                default:
                    throw new ArgumentException($"Unknown argument '{args[i]}'.");
            }
        }

        options.ProfileDirectory = Path.Combine(spikeRoot, "artifacts", "webview2-profiles", profileName);
        options.OutputDirectory = Path.Combine(spikeRoot, "artifacts", "results");
        return options;
    }

    private static string FindSpikeRoot()
    {
        // The executable lives under <spike>/D8.App/bin/... or <spike>/artifacts/publish/...; the marker is the
        // spike's Directory.Packages.props.
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Directory.Packages.props")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("Spike root not found above " + AppContext.BaseDirectory);
    }
}
