// Spar's script in every document of the app's window (plan section 3b, "Host-injected scripts"). The Windows host adds
// it with AddScriptToExecuteOnDocumentCreated, so it runs when the document is created, before any page script and
// under no page policy. It does two things, in the top document of the app's own origin only:
// - answers the heartbeat's `ping` with a `pong` of the same seq, from its message handler, so the host sees a page that
//   hangs from its very first script on (plan section 3, "Hung pages");
// - listens for Content Security Policy violations, groups them by directive, blocked address and source location, and
//   posts at most one `csp` message per interval, with a bounded number of groups per document (decision "CSP
//   reporting: both channels, with flood limits"). After a bounded number of violations in one document it stops
//   listening: the engine creates and dispatches an event per violation only while a listener exists, which cost a
//   flooding page about 0.1 ms each (docs/evidence/m1b-page-security.md); the browser's own report-uri goes on.
// It defines nothing on `window` and keeps its own references to what it calls, taken before the page runs. Page code
// can post any message itself through the same port, fake reports included, and the host treats every message as
// untrusted; so this script gives the page nothing it did not have. `report-uri` is the backstop for what it misses.
// Written for every engine Spar supports, without eval, string timers or DOM sinks, so it works under any policy.
(() => {
  "use strict";

  const webview = globalThis.chrome?.webview;
  if (webview === undefined || location.origin !== "https://app.localhost" || window !== window.top) {
    return;
  }

  const post = webview.postMessage.bind(webview);
  const listen = webview.addEventListener.bind(webview);

  // The host writes `{"type":"ping","seq":N}` and a TAB; nothing else from the host starts like that.
  const pingStart = "{\"type\":\"ping\",\"seq\":";
  const pingEnd = "}\t";
  const seqPattern = /^[1-9][0-9]{0,15}$/;
  const test = RegExp.prototype.test;
  const startsWith = String.prototype.startsWith;
  const endsWith = String.prototype.endsWith;
  const slice = String.prototype.slice;
  const apply = Reflect.apply;

  listen("message", event => {
    const data = event.data;
    if (typeof data !== "string" || !apply(startsWith, data, [pingStart]) || !apply(endsWith, data, [pingEnd])) {
      return;
    }

    const seq = apply(slice, data, [pingStart.length, data.length - pingEnd.length]);
    if (apply(test, seqPattern, [seq])) {
      post("{\"type\":\"pong\",\"seq\":" + seq + "}\t");
    }
  });

  // Set by the host from spar.ContentSecurity(csp => csp.Reporting(listener: …)).
  if (!__SPAR_REPORT_VIOLATIONS__) {
    return;
  }

  // The flood limits: one message per interval at most, the first a moment after the first violation so a burst goes
  // in one message, and at most this many distinct groups per document; the rest are counted as overflow.
  const intervalMs = __SPAR_REPORT_INTERVAL_MS__;
  const firstDelayMs = __SPAR_REPORT_FIRST_DELAY_MS__;
  const maxGroups = __SPAR_REPORT_MAX_GROUPS__;
  const maxViolations = __SPAR_REPORT_MAX_VIOLATIONS__;
  const maxText = 256;
  const maxSample = 40;

  const stringify = JSON.stringify;
  const now = performance.now.bind(performance);
  const setTimer = setTimeout;
  const MapType = Map;
  const SetType = Set;
  const mapGet = Map.prototype.get;
  const mapSet = Map.prototype.set;
  const mapValues = Map.prototype.values;
  const setHas = Set.prototype.has;
  const setAdd = Set.prototype.add;
  const arrayFrom = Array.from;

  const seen = new SetType();
  const stopListening = window.removeEventListener.bind(window);
  let pending = new MapType();
  let overflow = 0;
  let counted = 0;
  let stopped = false;
  let timer = false;
  let lastPost = -Infinity;

  const text = (value, limit) => {
    const string = typeof value === "string" ? value : "";
    return string.length <= limit ? string : apply(slice, string, [0, limit]);
  };

  const flush = () => {
    timer = false;
    lastPost = now();
    const groups = arrayFrom(apply(mapValues, pending, []));
    const message = stringify(stopped ? { groups, overflow, stopped } : { groups, overflow });
    pending = new MapType();
    overflow = 0;
    post("{\"type\":\"csp\"}\t" + message);
  };

  const schedule = () => {
    if (timer) {
      return;
    }

    timer = true;
    const wait = lastPost + intervalMs - now();
    setTimer(flush, wait > firstDelayMs ? wait : firstDelayMs);
  };

  const onViolation = event => {
    if (++counted > maxViolations) {
      stopped = true;
      stopListening("securitypolicyviolation", onViolation, true);
      schedule();
      return;
    }

    const directive = text(event.effectiveDirective, maxText);
    const blocked = text(event.blockedURI, maxText);
    const source = text(event.sourceFile, maxText);
    const line = event.lineNumber > 0 ? event.lineNumber : 0;
    const column = event.columnNumber > 0 ? event.columnNumber : 0;
    const key = directive + "\n" + blocked + "\n" + source + ":" + line + ":" + column;
    const group = apply(mapGet, pending, [key]);
    if (group !== undefined) {
      group.count++;
    } else if (apply(setHas, seen, [key]) || seen.size < maxGroups) {
      apply(setAdd, seen, [key]);
      apply(mapSet, pending, [key, { directive, blocked, source, line, column, sample: text(event.sample, maxSample), count: 1 }]);
    } else {
      overflow++;
    }

    schedule();
  };

  // On the window, capturing, and first: the event reaches the window before any element or later listener.
  window.addEventListener("securitypolicyviolation", onViolation, true);
})();
