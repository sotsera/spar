namespace Spar;

/// <summary>
/// The window options of <c>docs/app-model.md</c> that Spar's real <see cref="WindowOptions"/> does not have yet
/// (it has <c>Background</c>, <c>Size</c> and <c>MinSize</c>, M1b build step 2, and <c>Route</c>, <c>SingleInstance</c>,
/// <c>Commands</c>, <c>Modal</c>, <c>RememberPlacement</c> and <c>Zoom</c>, M3d), as extension members on the real type.
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

        public WindowOptions CloseToTray() => options;

        public WindowOptions ProtectFromCapture() => options;

        public WindowOptions FindInPage() => options;
    }
}
