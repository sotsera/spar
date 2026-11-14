// Reports to the host after the second animation frame (the first frame is then on screen). Same page as
// spikes/linux-memory/web/small.html; the report goes through whichever bridge the engine has.
"use strict";
function sparPost(text) {
  if (window.chrome && window.chrome.webview) {
    window.chrome.webview.postMessage(text);
  } else if (window.webkit && window.webkit.messageHandlers && window.webkit.messageHandlers.spar) {
    window.webkit.messageHandlers.spar.postMessage(text);
  }
}
requestAnimationFrame(() => requestAnimationFrame(() => sparPost("SPAR_READY " + location.pathname + location.search)));
