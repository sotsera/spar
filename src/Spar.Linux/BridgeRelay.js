// Spar's bridge relay on Linux (plan section 3, "Origin policy"). WebKitGTK injects it at document start, into the
// top frame only and only on the app's origin, in the isolated script world "spar": the one world that has the
// script message handler `window.webkit.messageHandlers.spar`, so neither the page's own world nor any frame can post
// to the host directly. The page's SDK and this relay share only the top document's window: the page dispatches
// `spar:send` with one message string as the event's detail, and the relay posts it; the host's messages come back
// through `sparRelay.deliver`, which the host calls in this world, and reach the page as `spar:receive` events. A
// frame from another origin cannot dispatch events on the top window, and its own worlds have no relay and no handler.
// Strings only, both ways, in order. Nothing is defined on the page's window: `sparRelay` lives in this world's scope.
// The page's `eval` ban applies here too, so nothing in it builds code from strings.
const sparRelay = (() => {
  const page = window;
  const onAppOrigin = location.protocol === "spar:" && location.host === "app";
  const handler = page.webkit?.messageHandlers?.spar;
  if (!onAppOrigin || handler === undefined) {
    return undefined;
  }

  // Taken now, from this world's own objects, which the page's scripts cannot reach.
  const post = handler.postMessage.bind(handler);
  const Detail = CustomEvent;
  const dispatch = page.dispatchEvent.bind(page);

  page.addEventListener("spar:send", event => {
    const message = event.detail;
    if (typeof message === "string") {
      post(message);
    }
  });

  return Object.freeze({
    deliver(messages) {
      for (const message of messages) {
        dispatch(new Detail("spar:receive", { detail: message }));
      }
    },
  });
})();
