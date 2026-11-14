using System.Globalization;

namespace Integ.Linux;

/// <summary>
/// The script the host injects into the top frame at document start. It gives the page <c>window.spar</c> (post and
/// on), and implements the title-bar regions:
/// <list type="bullet">
/// <item>a primary-button press on a drag region (and not on an interactive element) asks the host to start a window
/// move, either at once ("press") or once the pointer has moved a few pixels with the button held ("threshold");</item>
/// <item>a second press of a double click on a drag region asks for maximize/restore instead;</item>
/// <item>a context-menu event on a drag region asks for the window menu;</item>
/// <item>with <c>edges</c>, a press within a few pixels of the viewport edge asks for a resize from that edge.</item>
/// </list>
/// A drag region is marked with <c>data-spar-drag</c> (and excluded with <c>data-spar-no-drag</c>), or, in "css" mode,
/// with the inherited custom property <c>--spar-region: drag | no-drag</c>.
/// </summary>
internal static class Bridge
{
    public static string Script(string dragMode, string regionMode, bool edges, int threshold) =>
        Template
            .Replace("__DRAG__", dragMode, StringComparison.Ordinal)
            .Replace("__REGION__", regionMode, StringComparison.Ordinal)
            .Replace("__EDGES__", edges ? "true" : "false", StringComparison.Ordinal)
            .Replace("__THRESHOLD__", threshold.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);

    private const string Template = """
        (() => {
          const config = Object.freeze({ drag: "__DRAG__", region: "__REGION__", edges: __EDGES__, edge: 6, threshold: __THRESHOLD__ });
          const handler = window.webkit.messageHandlers.spar;
          const listeners = new Set();
          const post = message => handler.postMessage(JSON.stringify(message));
          const regionCost = [];
          Object.defineProperty(window, "spar", {
            value: Object.freeze({ post, on: listener => listeners.add(listener), config, regionCost }),
          });
          Object.defineProperty(window, "__sparDeliver", {
            value: message => { for (const listener of listeners) { try { listener(message); } catch (error) { post({ t: "log", n: "listener-error", v: String(error) }); } } },
          });
          const interactive = 'button, input, textarea, select, option, a[href], label, [contenteditable]:not([contenteditable="false"]), [data-spar-no-drag]';
          const region = target => {
            if (!(target instanceof Element)) return null;
            const started = performance.now();
            let result = null;
            if (config.region === "css") {
              const value = getComputedStyle(target).getPropertyValue("--spar-region").trim();
              result = value === "drag" && !target.closest(interactive) ? "drag" : null;
            } else {
              const marked = target.closest("[data-spar-drag], [data-spar-no-drag]");
              result = marked && marked.hasAttribute("data-spar-drag") && !target.closest(interactive) ? "drag" : null;
            }
            regionCost.push(performance.now() - started);
            return result;
          };
          const edgeAt = (x, y) => {
            if (!config.edges) return -1;
            const e = config.edge, n = y < e, s = y >= innerHeight - e, w = x < e, east = x >= innerWidth - e;
            if (n && w) return 0; if (n && east) return 2; if (s && w) return 5; if (s && east) return 7;
            if (n) return 1; if (s) return 6; if (w) return 3; if (east) return 4;
            return -1;
          };
          let pending = null;
          addEventListener("mousedown", event => {
            if (event.button === 0) {
              const edge = edgeAt(event.clientX, event.clientY);
              if (edge >= 0) {
                event.preventDefault();
                post({ t: "resize", edge, x: event.clientX, y: event.clientY, ts: event.timeStamp });
                return;
              }
            }
            if (event.button !== 0 || config.drag === "off" || region(event.target) !== "drag") return;
            event.preventDefault();
            if (event.detail === 2) {
              pending = null;
              post({ t: "toggle-maximize", via: "double-press", ts: event.timeStamp });
              return;
            }
            if (event.detail > 2) return;
            if (config.drag === "press") {
              post({ t: "drag", mode: "press", x: event.clientX, y: event.clientY, ts: event.timeStamp });
              return;
            }
            pending = { x: event.clientX, y: event.clientY, ts: event.timeStamp };
          }, true);
          addEventListener("mousemove", event => {
            if (!pending) return;
            if (!(event.buttons & 1)) { pending = null; return; }
            if (Math.hypot(event.clientX - pending.x, event.clientY - pending.y) < config.threshold) return;
            const start = pending;
            pending = null;
            post({ t: "drag", mode: "threshold", x: start.x, y: start.y, ts: start.ts, moveTs: event.timeStamp });
          }, true);
          addEventListener("mouseup", () => { pending = null; }, true);
          addEventListener("contextmenu", event => {
            if (region(event.target) !== "drag") return;
            event.preventDefault();
            post({ t: "window-menu", x: event.clientX, y: event.clientY, ts: event.timeStamp });
          }, true);
        })();
        """;
}
