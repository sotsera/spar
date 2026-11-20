// Spar's bridge relay on Linux (plan section 3, "Origin policy"). WebKitGTK injects it at document start, into the
// top frame only and only on the app's origin, in the isolated script world "spar": the one world that has the
// script message handler `window.webkit.messageHandlers.spar`, so neither the page's own world nor any frame can post
// to the host directly. The page's SDK and this relay share only the top document's window: the page dispatches
// `spar:send` with one message string as the event's detail, and the relay posts it; the host's messages come back
// through `sparRelay.deliver`, which the host calls in this world, and reach the page as `spar:receive` events. A
// frame from another origin cannot dispatch events on the top window, and its own worlds have no relay and no handler.
// Strings only, both ways, in order. Nothing is defined on the page's window: `sparRelay` lives in this world's scope.
// The page's `eval` ban applies here too, so nothing in it builds code from strings.
//
// The relay also answers the heartbeat (plan section 3, "Hung pages"): the host's `ping` never reaches the page; the
// relay answers it at once with a `pong` of the same seq. This world runs on the page's main thread, so a page that
// hangs, from its very first script on, leaves the ping unanswered. The pong's payload is `{"dialog":true}` while the
// page shows a modal dialog of its own (an open modal `<dialog>`, or a visible `aria-modal="true"` element), so a close
// guard waiting for the person is not taken for a silent one. This world's own objects are not the page's, so the page
// cannot change what the relay calls.
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

  // The host writes `{"type":"ping","seq":N}` and a TAB; nothing else from the host starts like that.
  const pingStart = "{\"type\":\"ping\",\"seq\":";
  const pingEnd = "}\t";
  const seqPattern = /^[1-9][0-9]{0,15}$/;
  const dialogSelector = "dialog:modal, [aria-modal=\"true\"]";

  // Whether a modal dialog of the page's own is open: a few elements at most, checked once per ping.
  const showsDialog = () => {
    try {
      for (const element of document.querySelectorAll(dialogSelector)) {
        if (typeof element.checkVisibility !== "function" || element.checkVisibility()) {
          return true;
        }
      }
    } catch {
      // A selector this engine does not know shows no dialog the host can see.
    }

    return false;
  };

  const answer = message => {
    if (!message.startsWith(pingStart) || !message.endsWith(pingEnd)) {
      return false;
    }

    const seq = message.slice(pingStart.length, message.length - pingEnd.length);
    if (seqPattern.test(seq)) {
      post("{\"type\":\"pong\",\"seq\":" + seq + "}\t" + (showsDialog() ? "{\"dialog\":true}" : ""));
    }

    return true;
  };

  page.addEventListener("spar:send", event => {
    const message = event.detail;
    if (typeof message === "string") {
      post(message);
    }
  });

  return Object.freeze({
    deliver(messages) {
      for (const message of messages) {
        if (!answer(message)) {
          dispatch(new Detail("spar:receive", { detail: message }));
        }
      }
    },
  });
})();
