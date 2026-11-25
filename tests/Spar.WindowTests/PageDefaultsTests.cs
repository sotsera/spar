using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using static Spar.Testing.SparAssertions;

namespace Spar.WindowTests;

/// <summary>
/// Defaults of the page that only a real browser shows (plan M1b step 9, <c>docs/evidence/webview2-flags</c>): WebAssembly
/// refused by the policy, and <c>app.localhost</c> never resolved to the loopback address.
/// </summary>
[Trait("Surface", "Window")]
public sealed partial class PageDefaultsTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task WebAssembly_is_refused_in_the_page_and_in_dedicated_and_shared_workers()
    {
        await using var host = await HostPage.OpenAsync();

        await host.ClickAsync("Check WebAssembly");

        // WebView2 keeps the global (no browser switch removes it), and the policy, without AllowWebAssembly(), refuses
        // every module wherever the page runs code; WebKitGTK has no WebAssembly at all (JSC_useWasm=false).
        var webAssembly = host.Page.GetByLabel("WebAssembly");
        await Expect(webAssembly).ToHaveTextAsync(RefusedOrAbsent());

        // Each refusal is a reported violation (one per place in the code, deduplicated), and nothing else went wrong.
        if ((await webAssembly.InnerTextAsync()).Contains("refused", StringComparison.Ordinal))
        {
            Assert.NotEmpty(host.App.Problems);
            Assert.All(host.App.Problems, problem => Assert.Contains("script-src blocked wasm-eval", problem, StringComparison.Ordinal));
        }
        else
        {
            Assert.Empty(host.App.Problems);
        }

        Assert.Empty(host.Page.PageErrors);
    }

    [Fact]
    public async Task Navigations_open_no_connection_to_the_loopback_address()
    {
        // Every navigation used to preconnect to [::1]:443, and an unanswered request opened a TLS connection there; the
        // resolver rule makes app.localhost unresolvable, so a listener on both loopback addresses sees nothing.
        using var listeners = Listeners.TryStart();
        if (listeners is null)
        {
            Assert.Skip("Port 443 on the loopback addresses cannot be listened on here (in use, or privileged without root on Linux), so connections cannot be counted.");
            return;
        }

        await using var host = await HostPage.OpenAsync();
        await host.Page.GotoAsync("/other.html");
        await host.Page.GotoAsync("/");
        await Expect(host.Status).ToHaveTextAsync("Ready");
        await Task.Delay(TimeSpan.FromSeconds(1), Token);

        Assert.Equal(0, listeners.Accepted);
    }

    [GeneratedRegex(@"^page (object refused \(\w+\); worker refused \(\w+\); shared worker refused \(\w+\)|undefined absent; worker absent; shared worker absent)$")]
    private static partial Regex RefusedOrAbsent();

    /// <summary>Listeners on <c>[::1]:443</c> and <c>127.0.0.1:443</c> that count the connections they accept.</summary>
    private sealed class Listeners : IDisposable
    {
        private readonly TcpListener[] _listeners;
        private int _accepted;

        private Listeners(TcpListener[] listeners)
        {
            _listeners = listeners;
            foreach (var listener in listeners)
            {
                _ = AcceptAsync(listener);
            }
        }

        public int Accepted => Volatile.Read(ref _accepted);

        public static Listeners? TryStart()
        {
            var started = new List<TcpListener>();
            try
            {
                foreach (var address in new[] { IPAddress.IPv6Loopback, IPAddress.Loopback })
                {
                    var listener = new TcpListener(address, 443);
                    listener.Start();
                    started.Add(listener);
                }

                return new Listeners([.. started]);
            }
            catch (SocketException)
            {
                started.ForEach(listener => listener.Stop());
                return null;
            }
        }

        public void Dispose()
        {
            foreach (var listener in _listeners)
            {
                listener.Stop();
            }
        }

        private async Task AcceptAsync(TcpListener listener)
        {
            try
            {
                while (true)
                {
                    using var client = await listener.AcceptTcpClientAsync();
                    Interlocked.Increment(ref _accepted);
                }
            }
            catch (Exception exception) when (exception is SocketException or ObjectDisposedException)
            {
                // Stopped.
            }
        }
    }
}
