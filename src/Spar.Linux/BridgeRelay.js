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

  // The Content Security Policy listener (plan section 3b, "Violations become diagnostics"), the Windows script's with
  // the same flood limits, written in by the host: violations grouped by directive, blocked address and source location,
  // at most one `csp` message per interval, a bounded number of groups per document, and after a bounded number of
  // violations it stops listening, since the engine dispatches an event per violation only while a listener exists.
  // It sits on the window, capturing, from document start, so it comes before any listener of the page. Violations in
  // workers raise no event on WebKitGTK and send no report (docs/evidence/linux-gpu-csp): nothing here can see them.
  if (__SPAR_REPORT_VIOLATIONS__) {
    const intervalMs = __SPAR_REPORT_INTERVAL_MS__;
    const firstDelayMs = __SPAR_REPORT_FIRST_DELAY_MS__;
    const maxGroups = __SPAR_REPORT_MAX_GROUPS__;
    const maxViolations = __SPAR_REPORT_MAX_VIOLATIONS__;
    const maxText = 256;
    const maxSample = 40;
    const header = "{\"type\":\"csp\"}\t";
    const now = performance.now.bind(performance);
    const seen = new Set();
    let pending = new Map();
    let overflow = 0;
    let counted = 0;
    let stopped = false;
    let timer = false;
    let lastPost = -Infinity;

    const text = (value, limit) => {
      const string = typeof value === "string" ? value : "";
      return string.length <= limit ? string : string.slice(0, limit);
    };

    const flush = () => {
      timer = false;
      lastPost = now();
      const groups = Array.from(pending.values());
      const message = JSON.stringify(stopped ? { groups, overflow, stopped } : { groups, overflow });
      pending = new Map();
      overflow = 0;
      post(header + message);
    };

    const schedule = () => {
      if (timer) {
        return;
      }

      timer = true;
      const wait = lastPost + intervalMs - now();
      setTimeout(flush, wait > firstDelayMs ? wait : firstDelayMs);
    };

    const onViolation = event => {
      if (stopped) {
        return;
      }

      if (++counted > maxViolations) {
        stopped = true;
        page.removeEventListener("securitypolicyviolation", onViolation, true);
        schedule();
        return;
      }

      const directive = text(event.effectiveDirective, maxText);
      const blocked = text(event.blockedURI, maxText);
      const source = text(event.sourceFile, maxText);
      const line = event.lineNumber > 0 ? event.lineNumber : 0;
      const column = event.columnNumber > 0 ? event.columnNumber : 0;
      const key = directive + "\n" + blocked + "\n" + source + ":" + line + ":" + column;
      const group = pending.get(key);
      if (group !== undefined) {
        group.count++;
      } else if (seen.has(key) || seen.size < maxGroups) {
        seen.add(key);
        pending.set(key, { directive, blocked, source, line, column, sample: text(event.sample, maxSample), count: 1 });
      } else {
        overflow++;
      }

      schedule();
    };

    page.addEventListener("securitypolicyviolation", onViolation, true);
  }

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
