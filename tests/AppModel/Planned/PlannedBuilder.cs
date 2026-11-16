namespace Spar;

/// <summary>
/// The configuration calls of <c>Program.cs</c> that Spar's builder does not have yet, on the real
/// <see cref="SparBuilder"/>. They run the app's configuration and keep nothing: the real-Spar check only compiles.
/// </summary>
public static class PlannedBuilder
{
    extension(SparBuilder builder)
    {
        public SparBuilder Window(Action<WindowOptions> configure) => Configure(builder, new WindowOptions(), configure);

        public SparBuilder Window(string kind, Action<WindowOptions> configure)
        {
            ArgumentException.ThrowIfNullOrEmpty(kind);
            return Configure(builder, new WindowOptions(), configure);
        }

        public SparBuilder Tray(Action<TrayOptions> configure) => Configure(builder, new TrayOptions(), configure);

        public SparBuilder Shell(Action<ShellOptions> configure) => Configure(builder, new ShellOptions(), configure);

        public SparBuilder ContentSecurity(Action<CspBuilder> configure) => Configure(builder, new CspBuilder(), configure);

        public SparBuilder Permissions(Action<PermissionsBuilder> configure) => Configure(builder, new PermissionsBuilder(), configure);

        public SparBuilder OnCloseRequested(Func<CloseRequest, IServiceProvider, CancellationToken, ValueTask<CloseDecision>> guard)
        {
            ArgumentNullException.ThrowIfNull(guard);
            return builder;
        }

        public SparBuilder OnCloseRequested(Func<CloseRequest, IServiceProvider, CloseDecision> guard)
        {
            ArgumentNullException.ThrowIfNull(guard);
            return builder;
        }
    }

    private static SparBuilder Configure<TOptions>(SparBuilder builder, TOptions options, Action<TOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        configure(options);
        return builder;
    }
}
