// Reports "ready" to the host after the second animation frame (the first frame is then on screen).
"use strict";
function sparPost(text) {
  if (window.webkit && window.webkit.messageHandlers && window.webkit.messageHandlers.spar) {
    window.webkit.messageHandlers.spar.postMessage(text);
  }
}
requestAnimationFrame(() => requestAnimationFrame(() => sparPost("SPAR_READY " + location.pathname)));
// Pointer events the page sees (for the drag question): does a mousedown get its mouseup?
for (const type of ["mousedown", "mouseup", "click", "pointercancel"]) {
  document.addEventListener(type, (event) => sparPost("event " + type + " buttons=" + event.buttons), true);
}
