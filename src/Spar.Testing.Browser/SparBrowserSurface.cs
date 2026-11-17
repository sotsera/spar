using Spar.Hosting;
using Spar.Loopback;

namespace Spar.Testing;

/// <summary>
/// The Browser surface: <c>o.Surface = SparTestSurface.Browser</c> with <c>o.Page()</c>, the app's own packed page, or
/// <c>o.Page(folder)</c>; then <c>app.PageUrl()</c> for the browser to open. The loopback host runs in the test process in
/// front of the app's backend.
/// </summary>
public static class SparBrowserSurface
{
    extension(SparTestOptions options)
    {
        /// <summary>
        /// Serves the app's own page: the one the build packed into its executable, as users get it. The app must be
        /// built in a variant that packs its page (Test and Production do).
        /// </summary>
        public SparTestOptions Page()
        {
            options.StartBrowser = StartAsync;
            return options;
        }

        /// <summary>
        /// Serves <paramref name="folder"/> as the page, or under <paramref name="prefix"/> (for example <c>"spar/"</c>),
        /// for a test page that is not the app's own.
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
            : throw new InvalidOperationException("The app has no page: start it with Surface = SparTestSurface.Browser and o.Page().");

    private static async Task<IAsyncDisposable> StartAsync(SparTestApp app, CancellationToken cancellationToken)
    {
        var running = app.RunningApp;
        ILoopbackPage page = app.Options.PageFolders.Count > 0 ? Folders(app.Options.PageFolders) : Packed(running.Catalog.AppAssembly);
        var host = await LoopbackHost.StartAsync(
            new LoopbackOptions
            {
                Dispatcher = running.Dispatcher,
                Fingerprint = running.Catalog.Fingerprint,
                Page = page,
                IncludeDetails = true,
                Log = app.ReportProblem,
            },
            cancellationToken);
        return new Surface(host);
    }

    private static LoopbackAssets Folders(IEnumerable<(string Prefix, string Folder)> folders)
    {
        var assets = new LoopbackAssets();
        foreach (var (prefix, folder) in folders)
        {
            assets.Mount(prefix, folder);
        }

        return assets;
    }

    private static PackedPage Packed(System.Reflection.Assembly? assembly)
    {
        var frontend = (assembly is null ? null : SparFrontend.Open(assembly))
            ?? throw new InvalidOperationException(
                "The app's executable carries no page: Development builds serve it from Vite instead. Test projects build " +
                "the app in the Test variant, which packs it; check that the app has a frontend folder.");

        // As a host does at start: decode the compressed files off the thread that serves them.
        _ = Task.Run(frontend.DecodeAll);
        return new PackedPage(frontend);
    }

    /// <summary>The running loopback host; disposing the app stops it before the backend.</summary>
    private sealed class Surface(LoopbackHost host) : IAsyncDisposable
    {
        public LoopbackHost Host { get; } = host;

        public ValueTask DisposeAsync() => Host.DisposeAsync();
    }
}
