// Host-injected script (stand-in for the Spar bridge). Runs before every page script on both hosts. It answers the
// host's heartbeat from its message handler, forwards every other host message to the page as a "spar-host" event,
// and reports document creation and visibility changes. Messages are short strings: "kind:field:field".
(() => {
  const webview = window.chrome && window.chrome.webview;
  const post = webview
    ? (text) => webview.postMessage(text)
    : (text) => window.webkit.messageHandlers.spar.postMessage(text);
  const fromHost = (data) => {
    if (typeof data !== 'string') {
      return;
    }
    // ping:<seq> and probe:<seq> are answered at once, with the page's visibility at that moment.
    if (data.startsWith('ping:') || data.startsWith('probe:')) {
      const colon = data.indexOf(':');
      post(`${data.slice(0, colon)}-pong:${data.slice(colon + 1)}:${document.visibilityState}`);
      return;
    }
    window.dispatchEvent(new CustomEvent('spar-host', { detail: data }));
  };
  if (webview) {
    webview.addEventListener('message', (event) => fromHost(event.data));
  } else {
    // WebKitGTK: the host calls this through webkit_web_view_evaluate_javascript.
    window.__sparHost = fromHost;
  }
  window.__sparPost = post;
  document.addEventListener('visibilitychange', () => post(`vis:${document.visibilityState}:${performance.now().toFixed(1)}`));
  window.addEventListener('pagehide', () => post('pagehide'));
  post(`doc:${location.pathname}:${document.visibilityState}`);
})();
