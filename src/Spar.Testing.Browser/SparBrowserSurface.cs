using Spar.Hosting;
using Spar.Loopback;
using Spar.Runtime;

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
        var packed = app.Options.PageFolders.Count > 0 ? null : await PackedAsync(running.Catalog.AppAssembly);
        ILoopbackPage page = packed is null ? Folders(app.Options.PageFolders) : new PackedPage(packed);
        var host = await LoopbackHost.StartAsync(
            new LoopbackOptions
            {
                Dispatcher = running.Dispatcher,
                Fingerprint = running.Catalog.Fingerprint,
                Page = page,
                IncludeDetails = true,
                Log = app.ReportProblem,

                // The policy the app's window sends: its declared page security, and its page's import-map and style
                // hashes. The data root takes the page's colour scheme choice, as the window's would.
                Security = running.Catalog.PageSecurity ?? SparPageSecurity.Default,
                ScriptHashes = packed is null ? [] : PageHtml.ImportMapHashes(packed),
                StyleHashes = packed is null ? FolderStyleHashes(app.Options.PageFolders) : PageHtml.StyleHashes(packed),
                DataRoot = running.Folders.DataRoot,
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

    /// <summary>
    /// The theme and splash style hashes of the HTML files at the top of each served folder, so a page the Spar Vite
    /// plugin built (its <c>dist</c> folder) gets the policy its window would.
    /// </summary>
    private static List<string> FolderStyleHashes(IEnumerable<(string Prefix, string Folder)> folders)
    {
        var hashes = new List<string>();
        foreach (var (_, folder) in folders)
        {
            if (!Directory.Exists(folder))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(folder, "*.html"))
            {
                hashes.AddRange(PageHtml.StyleHashes(File.ReadAllBytes(file)).Where(hash => !hashes.Contains(hash)));
            }
        }

        return hashes;
    }

    private static async Task<SparFrontend> PackedAsync(System.Reflection.Assembly? assembly)
    {
        var frontend = (assembly is null ? null : SparFrontend.Open(assembly))
            ?? throw new InvalidOperationException(
                "The app's executable carries no page: Development builds serve it from Vite instead. Test projects build " +
                "the app in the Test variant, which packs it; check that the app has a frontend folder.");

        // As a host does at start: decode the compressed files off the thread that serves them, before the hashes.
        await Task.Run(frontend.DecodeAll);
        return frontend;
    }

    /// <summary>The running loopback host; disposing the app stops it before the backend.</summary>
    private sealed class Surface(LoopbackHost host) : IAsyncDisposable
    {
        public LoopbackHost Host { get; } = host;

        public ValueTask DisposeAsync() => Host.DisposeAsync();
    }
}
