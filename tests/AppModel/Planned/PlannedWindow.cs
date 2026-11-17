namespace Spar;

/// <summary>
/// The window options of <c>docs/app-model.md</c> that Spar's real <see cref="WindowOptions"/> does not have yet
/// (it has <c>Background</c>, <c>Size</c> and <c>MinSize</c>, M1b build step 2), as extension members on the real type.
/// They keep nothing: the real-Spar check only compiles. Each leaves this file when Spar gains it.
/// </summary>
public static class PlannedWindow
{
    extension(WindowOptions options)
    {
        public WindowOptions TitleBar(TitleBar kind)
        {
            _ = kind;
            return options;
        }

        public WindowOptions RememberPlacement(bool remember = true)
        {
            _ = remember;
            return options;
        }

        public WindowOptions CloseToTray() => options;

        public WindowOptions Route(string route)
        {
            ArgumentException.ThrowIfNullOrEmpty(route);
            return options;
        }

        public WindowOptions Modal() => options;

        public WindowOptions SingleInstance() => options;

        public WindowOptions ProtectFromCapture() => options;

        public WindowOptions Zoom(bool enabled = true, double min = 0.25, double max = 5)
        {
            _ = (enabled, min, max);
            return options;
        }

        public WindowOptions FindInPage() => options;

        public WindowOptions Commands(Action<CommandAllowList> configure)
        {
            ArgumentNullException.ThrowIfNull(configure);
            configure(new CommandAllowList([]));
            return options;
        }
    }
}
