using System.Text;

namespace Csp.Shared;

/// <summary>
/// The "external" origin: a second local server on another port. Every request is logged with its Referer, Origin and
/// fetch metadata, so a probe that never reached it is visible as a missing <c>EXT_HIT</c>.
/// </summary>
public static class ExternalServer
{
    public const int DefaultPort = 5180;

    private const string Svg = "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"4\" height=\"4\"><rect width=\"4\" height=\"4\" fill=\"red\"/></svg>";

    // Loaded from the external origin with ?app=<app origin>: tries to use the app's resources cross-origin.
    private const string CrossOriginPage = """
        <!doctype html><html><head><meta charset="utf-8"><title>external</title></head><body>
        <p>external page</p>
        <script>
        (async () => {
          const app = new URL(location.href).searchParams.get("app");
          const results = { page: "xpage", app };
          const wait = ms => new Promise(r => setTimeout(r, ms));
          results.corpImg = await new Promise(resolve => {
            const img = new Image();
            img.onload = () => resolve("loaded");
            img.onerror = () => resolve("error");
            img.src = app + "/pixel.svg?p=corpImg";
            setTimeout(() => resolve("timeout"), 3000);
          });
          try {
            const r = await fetch(app + "/data.json?p=corpFetch", { mode: "no-cors" });
            results.corpFetchNoCors = "loaded:" + r.type;
          } catch (e) { results.corpFetchNoCors = "error:" + e.name; }
          results.frame = await new Promise(resolve => {
            // The app frame posts a message when its script runs; a frame blocked by frame-ancestors never does.
            addEventListener("message", e => { if (e.data === "app-frame-script-ran") resolve("app-frame-ran"); });
            const f = document.createElement("iframe");
            f.src = app + "/frame.html?p=frameAncestors";
            document.body.append(f);
            setTimeout(() => resolve("app-frame-did-not-run"), 3000);
          });
          results.opener = await new Promise(resolve => {
            let w = null;
            try { w = window.open(app + "/frame.html?p=coopPopup", "coop"); } catch (e) { resolve("open-error:" + e.name); return; }
            if (!w) { resolve("open-returned-null"); return; }
            setTimeout(() => {
              let s = "closed=" + w.closed + " opener-handle-severed=" + (w.closed === true);
              try { s += " sameOriginAccess=" + typeof w.document; } catch { s += " crossOriginAccess"; }
              try { w.close(); } catch {}
              resolve(s);
            }, 1500);
          });
          await wait(300);
          await fetch("/xlog", { method: "POST", body: JSON.stringify(results) });
          console.log("SPAR_XPAGE " + JSON.stringify(results));
          console.log("SPAR_DONE");
        })();
        </script></body></html>
        """;

    public static Func<MiniRequest, HostResponse> Create(string label) => request =>
    {
        var path = request.Target.Split('?')[0];
        Log.Write($"EXT_HIT {request.Method} {request.Target} referer={request.Header("Referer") ?? "-"} origin={request.Header("Origin") ?? "-"} dest={request.Header("Sec-Fetch-Dest") ?? "-"} mode={request.Header("Sec-Fetch-Mode") ?? "-"} ua={Short(request.Header("User-Agent"))}");
        var cors = new List<KeyValuePair<string, string>> { new("Access-Control-Allow-Origin", "*"), new("Cache-Control", "no-store") };
        if (request.Method == "OPTIONS")
        {
            cors.Add(new("Access-Control-Allow-Methods", "POST, GET"));
            cors.Add(new("Access-Control-Allow-Headers", "content-type"));
            return new HostResponse(204, "No Content", "text/plain", [], cors);
        }

        if (request.Body.Length > 0)
        {
            Log.Write($"EXT_BODY {path} type={request.Header("Content-Type") ?? "-"} {Encoding.UTF8.GetString(request.Body)}");
        }

        switch (path)
        {
            case "/corp-same.svg":
                cors.Add(new("Cross-Origin-Resource-Policy", "same-origin"));
                return new HostResponse(200, "OK", "image/svg+xml", Encoding.UTF8.GetBytes(Svg), cors);
            case "/corp-none.svg":
                return new HostResponse(200, "OK", "image/svg+xml", Encoding.UTF8.GetBytes(Svg), cors);
            case "/xpage.html":
                return new HostResponse(200, "OK", "text/html; charset=utf-8", Encoding.UTF8.GetBytes(CrossOriginPage), cors);
        }

        cors.Add(new("Cross-Origin-Resource-Policy", "cross-origin"));
        var (type, body) = Path.GetExtension(path) switch
        {
            ".svg" => ("image/svg+xml", Svg),
            ".js" => ("text/javascript", "window.__extScript = true;"),
            ".woff2" => ("font/woff2", "not a font"),
            ".html" => ("text/html; charset=utf-8", $"<!doctype html><title>external</title><p>external page {label}</p>"),
            _ when path == "/data" => ("application/json", "{\"ext\":true}"),
            _ => ("text/html; charset=utf-8", "<!doctype html><p>external " + path + "</p>"),
        };
        return new HostResponse(200, "OK", type, Encoding.UTF8.GetBytes(body), cors);
    };

    private static string Short(string? userAgent) =>
        userAgent is null ? "-" : userAgent.Length > 60 ? userAgent[..60].Replace(' ', '_') : userAgent.Replace(' ', '_');
}
