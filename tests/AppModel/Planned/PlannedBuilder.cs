namespace Spar;

/// <summary>
/// The configuration calls of <c>Program.cs</c> that Spar's builder does not have yet, on the real
/// <see cref="SparBuilder"/>. They run the app's configuration and keep nothing: the real-Spar check only compiles.
/// </summary>
public static class PlannedBuilder
{
    extension(SparBuilder builder)
    {
        /// <summary>
        /// Window kinds (6.7) are not built yet. The real options type has no public constructor, so the kind's
        /// configuration runs against the main window's options, which this compile-only check never shows.
        /// </summary>
        public SparBuilder Window(string kind, Action<WindowOptions> configure)
        {
            ArgumentException.ThrowIfNullOrEmpty(kind);
            return builder.Window(configure);
        }

        public SparBuilder Tray(Action<TrayOptions> configure) => Configure(builder, new TrayOptions(), configure);

        public SparBuilder Shell(Action<ShellOptions> configure) => Configure(builder, new ShellOptions(), configure);
    }

    private static SparBuilder Configure<TOptions>(SparBuilder builder, TOptions options, Action<TOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        configure(options);
        return builder;
    }
}
