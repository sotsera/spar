namespace Spar.Testing;

/// <summary>
/// One scene's run (decision "M2c's decide-first questions"): the scene method starts the app with
/// <see cref="StartAsync{TApp}"/> and drives its page; when the method returns, <c>spar snap</c> captures the page
/// (a PNG, the accessibility tree, the bridge calls and the problems) and the scene ends the app.
/// <code>
/// [SparScene("new-page-dialog")]
/// public static async Task NewPageDialog(SparScene scene)
/// {
///     var app = await scene.StartAsync&lt;Program&gt;();
///     await app.Page.GetByRole(SparRole.Button, new() { Name = "New page" }).First.ClickAsync();
/// }
/// </code>
/// </summary>
public sealed class SparScene : IAsyncDisposable
{
    /// <summary>The instant a scene's clock starts at unless the scene sets <see cref="Time"/>.</summary>
    public static readonly DateTimeOffset DefaultTime = new(2026, 1, 15, 9, 30, 0, TimeSpan.Zero);

    /// <summary>The page's size in CSS pixels, at a device scale of 1, unless the scene sets <see cref="Size"/>.</summary>
    public static readonly (int Width, int Height) DefaultSize = (1280, 800);

    private SparTestApp? _app;

    internal SparScene(string name, CancellationToken cancellationToken)
    {
        Name = name;
        CancellationToken = cancellationToken;
    }

    /// <summary>The scene's name, from its <see cref="SparSceneAttribute"/>.</summary>
    public string Name { get; }

    /// <summary>The run's cancellation: the test's, or <c>spar snap</c>'s.</summary>
    public CancellationToken CancellationToken { get; }

    /// <summary>The instant the app's and the page's clocks start at; set it before <see cref="StartAsync{TApp}"/>.</summary>
    public DateTimeOffset Time { get; set; } = DefaultTime;

    /// <summary>The page's size in CSS pixels; set it before <see cref="StartAsync{TApp}"/>.</summary>
    public (int Width, int Height) Size { get; set; } = DefaultSize;

    /// <summary>The app the scene started, once it did.</summary>
    public SparTestApp App => _app ?? throw new InvalidOperationException($"Scene '{Name}' has not started its app: call scene.StartAsync<Program>() first.");

    internal SceneClock? Clock { get; private set; }

    /// <summary>The bridge calls the app ran, from its first, for <c>calls.json</c>.</summary>
    internal Devtools.CallRecorder? Calls { get; private set; }

    /// <summary>
    /// Starts the app on the Window surface, as a window test does, with the scene's defaults: the clock at
    /// <see cref="Time"/>, the light theme, the page at <see cref="Size"/> with a device scale of 1 and reduced motion.
    /// <paramref name="configure"/> adds the scene's own options (service overrides, the clock's <c>TimeProvider</c> is
    /// the scene's unless it sets another). A scene starts one app; it ends with the scene.
    /// </summary>
    public async Task<SparTestApp> StartAsync<TApp>(Action<SparTestOptions>? configure = null)
        where TApp : ISparEntryPoint
    {
        if (_app is not null)
        {
            throw new InvalidOperationException($"Scene '{Name}' started its app already; a scene captures one app.");
        }

        var clock = new SceneClock(Time);
        Clock = clock;
        _app = await SparTestApp.StartAsync<TApp>(
            options =>
            {
                options.Surface = SparTestSurface.Window;
                options.ColorScheme = SparColorScheme.Light;
                options.Time(clock);
                options.Scene = new ScenePage(clock.PageScript(), Size);
                options.Built = running => running.Dispatcher.Recorder = Calls = new Devtools.CallRecorder(clock);
                configure?.Invoke(options);
                options.Surface = SparTestSurface.Window;
            },
            CancellationToken);
        return _app;
    }

    public async ValueTask DisposeAsync()
    {
        if (_app is { } app)
        {
            _app = null;
            await app.DisposeAsync();
        }
    }
}
