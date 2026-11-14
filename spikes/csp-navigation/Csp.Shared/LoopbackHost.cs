using System.Text.Json;

namespace Csp.Shared;

/// <summary>
/// Loopback mode: the same app routes over HTTP on 127.0.0.1, with the Host check that stops DNS rebinding, plus the
/// external server. A browser driver (tools/loopback.mjs) opens the page; this process ends when its input closes.
/// </summary>
public static class LoopbackHost
{
    public const int DefaultPort = 5190;

    public static int Run(HostOptions options)
    {
        var port = int.Parse(options.Value("--port") ?? DefaultPort.ToString(System.Globalization.CultureInfo.InvariantCulture), System.Globalization.CultureInfo.InvariantCulture);
        var origin = $"http://127.0.0.1:{port}";
        var profile = HeaderProfile.Load(options, origin, $"http://127.0.0.1:{options.ExternalPort}");
        Log.Write($"HOST loopback origin={origin} profile={profile.Name} csp={profile.Csp ?? "-"} meta={profile.Meta ?? "-"}");
        var routes = new AppRoutes(options.Roots, profile, options.Tamper, static (JsonElement _) => { });
        string[] allowedHosts = [$"127.0.0.1:{port}", $"localhost:{port}"];
        using var app = new MiniHttp(port, request =>
        {
            var host = request.Header("Host");
            if (host is null || !allowedHosts.Contains(host, StringComparer.OrdinalIgnoreCase))
            {
                Log.Write($"HOST_REJECTED host={host ?? "-"} {request.Target}");
                return HostResponse.Text(421, "Misdirected Request", "unexpected Host header");
            }

            if (!request.Target.StartsWith("/_spar/log", StringComparison.Ordinal))
            {
                Log.Write($"SERVE {request.Method} {request.Target} referer={request.Header("Referer") ?? "-"} dest={request.Header("Sec-Fetch-Dest") ?? "-"}");
            }

            return routes.Handle(request.Method, request.Target, request.Body, request.Header);
        });
        using var external = new MiniHttp(options.ExternalPort, ExternalServer.Create(options.Label));
        Console.WriteLine($"LOOPBACK_READY {origin}");
        while (Console.ReadLine() is not null)
        {
        }

        Log.Write("LOOPBACK_STOPPED");
        return 0;
    }
}
