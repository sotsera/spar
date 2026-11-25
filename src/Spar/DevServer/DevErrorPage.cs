using System.Text;

namespace Spar.DevServer;

/// <summary>
/// The Development error page a Debug build's window shows when its development server failed to start or stopped
/// (decision "M1d step 1's shape"): what happened, the server's last output, what to do, and a Retry button. The app
/// keeps running behind it, so the backend and a debugger stay attached. It runs under the app's own strict policy:
/// its style and script are files of the proxy's own route (<see cref="DevServerProxy.RoutePrefix"/>), never inline,
/// and the script writes text only. Retry posts to the proxy, which tries the server again and answers once that try
/// has ended, then the page loads the app's start address again.
/// </summary>
internal static class DevErrorPage
{
    public const string Style =
        """
        :root { color-scheme: light dark; font: 14px/1.5 system-ui, sans-serif; }
        body { margin: 0; padding: 32px; background: Canvas; color: CanvasText; }
        main { max-width: 960px; margin: 0 auto; }
        h1 { font-size: 20px; margin: 0 0 8px; }
        .problem { margin: 0 0 16px; }
        ul { padding-left: 20px; }
        pre { background: color-mix(in srgb, CanvasText 6%, Canvas); padding: 12px; overflow: auto; max-height: 50vh;
          font: 12px/1.45 ui-monospace, Consolas, monospace; white-space: pre-wrap; border-radius: 6px; }
        button { font: inherit; padding: 6px 16px; }
        #status { margin-left: 12px; }
        """;

    public const string Script =
        """
        const button = document.getElementById("retry");
        const status = document.getElementById("status");
        button?.addEventListener("click", async () => {
          button.disabled = true;
          status.textContent = "Starting the development server…";
          try {
            const answer = await fetch("/_spar/dev-server/retry", { method: "POST" });
            status.textContent = answer.ok ? "Ready. Loading the page…" : "It failed again. Loading its output…";
          } catch {
            status.textContent = "The retry could not reach the app.";
          }
          location.replace("/");
        });
        """;

    /// <summary>The page for <paramref name="outcome"/>, every value from the server or the system HTML-escaped.</summary>
    public static string Render(DevServerOutcome outcome, DevServerSettings settings)
    {
        var html = new StringBuilder(4096);
        html.Append("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\">")
            .Append("<title>Development server unavailable</title>")
            .Append("<link rel=\"stylesheet\" href=\"").Append(DevServerProxy.RoutePrefix).Append("page.css\">")
            .Append("<script type=\"module\" src=\"").Append(DevServerProxy.RoutePrefix).Append("page.js\"></script>")
            .Append("</head><body><main>")
            .Append("<h1>The page cannot come from the development server</h1>")
            .Append("<p class=\"problem\">").Append(Escape(outcome.Problem ?? "The development server is not ready.")).Append("</p>")
            .Append("<p><button id=\"retry\" type=\"button\" autofocus>Retry</button><span id=\"status\" role=\"status\"></span></p>")
            .Append("<ul>");
        if (outcome.External)
        {
            html.Append("<li>This build uses a development server Spar does not start (")
                .Append(Escape(settings.UrlSource ?? DevServerSettings.UrlVariable))
                .Append("). Start it, then retry; its output is where it runs.</li>");
        }
        else
        {
            html.Append("<li>Spar starts it with <code>npm run dev</code> in <code>").Append(Escape(settings.FrontendDirectory))
                .Append("</code>. Its output is below, and in the app's log under <code>").Append(DevServerSession.Category).Append("</code>.</li>");
        }

        html.Append("<li>The app keeps running: its backend and an attached debugger are unaffected.</li>")
            .Append("<li>Build with <code>-p:SparDevServer=false</code> to embed the page as a Release build does, without hot reload.</li>")
            .Append("</ul>");
        if (outcome.Output.Count > 0)
        {
            html.Append("<h2>Last output</h2><pre>");
            foreach (var line in outcome.Output)
            {
                html.Append(Escape(line)).Append('\n');
            }

            html.Append("</pre>");
        }

        return html.Append("</main></body></html>").ToString();
    }

    /// <summary>Escapes text for an element's content or a quoted attribute.</summary>
    internal static string Escape(string text)
    {
        var escaped = new StringBuilder(text.Length + 16);
        foreach (var character in text)
        {
            escaped.Append(character switch
            {
                '&' => "&amp;",
                '<' => "&lt;",
                '>' => "&gt;",
                '"' => "&quot;",
                '\'' => "&#39;",
                _ => character.ToString(),
            });
        }

        return escaped.ToString();
    }
}
