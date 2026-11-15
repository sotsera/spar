using Spar.Loopback;

namespace Spar.Testing;

/// <summary>
/// The Browser surface: <c>o.Surface = SparTestSurface.Browser</c> with the page's folder from <c>o.Page(folder)</c>, then
/// <c>app.PageUrl()</c> for the browser to open. The loopback host runs in the test process in front of the app's backend.
/// </summary>
public static class SparBrowserSurface
{
    extension(SparTestOptions options)
    {
        /// <summary>
        /// Serves <paramref name="folder"/> as the page, or under <paramref name="prefix"/> (for example <c>"spar/"</c>).
        /// A stand-in until the packing step embeds the app's built page, which then becomes the default.
        /// </summary>
        public SparTestOptions Page(string folder, string prefix = "")
        {
            options.PageFolders.Add((prefix, folder));
            options.StartBrowser = StartAsync;
            return options;
        }
    }

    extension(SparTestApp app)
    {
        /// <summary>The page's origin, for example <c>http://127.0.0.1:52311</c>.</summary>
        public Uri Origin => HostOf(app).Origin;

        /// <summary>The page's address with its token in the fragment, for the browser to open.</summary>
        public Uri PageUrl(string path = "/") => HostOf(app).PageUrl(path);
    }

    private static LoopbackHost HostOf(SparTestApp app) =>
        app.Surface is Surface surface
            ? surface.Host
            : throw new InvalidOperationException("The app has no page: start it with Surface = SparTestSurface.Browser and o.Page(folder).");

    private static async Task<IAsyncDisposable> StartAsync(SparTestApp app, CancellationToken cancellationToken)
    {
        var assets = new LoopbackAssets();
        foreach (var (prefix, folder) in app.Options.PageFolders)
        {
            assets.Mount(prefix, folder);
        }

        var running = app.RunningApp;
        var host = await LoopbackHost.StartAsync(
            new LoopbackOptions
            {
                Dispatcher = running.Dispatcher,
                Fingerprint = running.Catalog.Fingerprint,
                Assets = assets,
                IncludeDetails = true,
                Log = app.ReportProblem,
            },
            cancellationToken);
        return new Surface(host);
    }

    /// <summary>The running loopback host; disposing the app stops it before the backend.</summary>
    private sealed class Surface(LoopbackHost host) : IAsyncDisposable
    {
        public LoopbackHost Host { get; } = host;

        public ValueTask DisposeAsync() => Host.DisposeAsync();
    }
}
