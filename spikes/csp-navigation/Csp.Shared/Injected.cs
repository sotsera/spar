namespace Csp.Shared;

/// <summary>
/// The script both desktop hosts inject at document creation (WebView2 AddScriptToExecuteOnDocumentCreated, WebKit
/// user script at document start). It sets the value the page reads synchronously at boot, records that it ran first,
/// and listens for CSP violations before any page script exists, forwarding them over the native message channel.
/// </summary>
public static class Injected
{
    public const string Script = """
        (() => {
          const at = performance.now();
          const post = message => {
            const text = JSON.stringify(message);
            if (window.chrome && window.chrome.webview) window.chrome.webview.postMessage(text);
            else if (window.webkit && window.webkit.messageHandlers && window.webkit.messageHandlers.spar) window.webkit.messageHandlers.spar.postMessage(text);
          };
          const order = (window.__sparOrder = window.__sparOrder || []);
          order.push("injected");
          const early = (window.__sparEarlyViolations = []);
          document.addEventListener("securitypolicyviolation", e => {
            const v = { directive: e.effectiveDirective, blocked: e.blockedURI, source: e.sourceFile, line: e.lineNumber, sample: e.sample, disposition: e.disposition };
            early.push(v);
            post(Object.assign({ kind: "violation", via: "injected", url: location.href }, v));
          }, true);
          // Errors the page would never report itself (failed module loads, SRI failures, Trusted Types errors).
          window.addEventListener("error", e => post({ kind: "error", message: String(e.message || (e.target && (e.target.src || e.target.href)) || e.type) }), true);
          window.addEventListener("unhandledrejection", e => post({ kind: "rejection", message: String(e.reason && (e.reason.message || e.reason)) }));
          let evalInInjected = "not-tried";
          if (location.pathname.startsWith("/probe")) {
            try { (0, eval)("1"); evalInInjected = "ran"; } catch (e) { evalInInjected = "blocked:" + e.name; }
          }
          Object.defineProperty(window, "__spar", {
            value: Object.freeze({ injectedAt: at, evalInInjected, telemetry: Object.freeze({ serviceName: "csp-spike", delayMs: 500 }) }),
            writable: false, configurable: false, enumerable: false,
          });
          post({ kind: "injected", at, url: location.href, top: window === window.top, readyState: document.readyState });
        })();
        """;
}
