// Spar's script in every document of the app's window (plan section 3b, "Host-injected scripts"). The Windows host adds
// it with AddScriptToExecuteOnDocumentCreated, so it runs when the document is created, before any page script and
// under no page policy. It does three things, in the top document of the app's own origin only:
// - answers the heartbeat's `ping` with a `pong` of the same seq, from its message handler, so the host sees a page that
//   hangs from its very first script on (plan section 3, "Hung pages");
// - listens for Content Security Policy violations, groups them by directive, blocked address and source location, and
//   posts at most one `csp` message per interval, with a bounded number of groups per document (decision "CSP
//   reporting: both channels, with flood limits"). After a bounded number of violations in one document it stops
//   listening: the engine creates and dispatches an event per violation only while a listener exists, which cost a
//   flooding page about 0.1 ms each (docs/evidence/m1b-page-security.md); the browser's own report-uri goes on;
// - reports the page's errors for the app's log (plan M1b, build step 7): `console.error` calls, uncaught exceptions
//   (the window's `error` event) and unhandled rejections (`unhandledrejection`), grouped by kind, text and source
//   location and posted as `errors` messages with the same limits. `console.error` is wrapped, since no event reports
//   it: the wrapper calls the page's original with the same arguments and returns its result, and is taken out again
//   once the document reaches its bound. The events are only listened to, never cancelled.
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
  const exec = RegExp.prototype.exec;
  const startsWith = String.prototype.startsWith;
  const endsWith = String.prototype.endsWith;
  const slice = String.prototype.slice;
  const indexOf = String.prototype.indexOf;
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

  // The flood limits, the same for both reports: one message per interval at most, the first a moment after the first
  // report so a burst goes in one message.
  const intervalMs = __SPAR_REPORT_INTERVAL_MS__;
  const firstDelayMs = __SPAR_REPORT_FIRST_DELAY_MS__;

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
  const addListener = window.addEventListener.bind(window);
  const removeListener = window.removeEventListener.bind(window);

  const text = (value, limit) => {
    const string = typeof value === "string" ? value : "";
    return string.length <= limit ? string : apply(slice, string, [0, limit]);
  };

  // One report's grouping and batching: a group per key, at most `maxGroups` distinct groups per document (the rest
  // counted as overflow), and after `maxReports` reports `stop` is called and nothing more is counted. Returns the
  // function that counts one report; `fields` makes a new group's fields and is called only for a new group.
  const reporter = (type, maxGroups, maxReports, stop) => {
    const header = "{\"type\":\"" + type + "\"}\t";
    const seen = new SetType();
    let pending = new MapType();
    let overflow = 0;
    let counted = 0;
    let stopped = false;
    let timer = false;
    let lastPost = -Infinity;

    const flush = () => {
      timer = false;
      lastPost = now();
      const groups = arrayFrom(apply(mapValues, pending, []));
      const message = stringify(stopped ? { groups, overflow, stopped } : { groups, overflow });
      pending = new MapType();
      overflow = 0;
      post(header + message);
    };

    const schedule = () => {
      if (timer) {
        return;
      }

      timer = true;
      const wait = lastPost + intervalMs - now();
      setTimer(flush, wait > firstDelayMs ? wait : firstDelayMs);
    };

    return (key, fields) => {
      if (stopped) {
        return;
      }

      if (++counted > maxReports) {
        stopped = true;
        stop();
        schedule();
        return;
      }

      const group = apply(mapGet, pending, [key]);
      if (group !== undefined) {
        group.count++;
      } else if (apply(setHas, seen, [key]) || seen.size < maxGroups) {
        apply(setAdd, seen, [key]);
        const made = fields();
        made.count = 1;
        apply(mapSet, pending, [key, made]);
      } else {
        overflow++;
      }

      schedule();
    };
  };

  // Set by the host from spar.ContentSecurity(csp => csp.Reporting(listener: …)).
  if (__SPAR_REPORT_VIOLATIONS__) {
    const maxText = 256;
    const maxSample = 40;
    const onViolation = event => {
      const directive = text(event.effectiveDirective, maxText);
      const blocked = text(event.blockedURI, maxText);
      const source = text(event.sourceFile, maxText);
      const line = event.lineNumber > 0 ? event.lineNumber : 0;
      const column = event.columnNumber > 0 ? event.columnNumber : 0;
      const key = directive + "\n" + blocked + "\n" + source + ":" + line + ":" + column;
      reportViolation(key, () => ({ directive, blocked, source, line, column, sample: text(event.sample, maxSample) }));
    };
    const reportViolation = reporter("csp", __SPAR_REPORT_MAX_GROUPS__, __SPAR_REPORT_MAX_VIOLATIONS__,
      () => removeListener("securitypolicyviolation", onViolation, true));

    // On the window, capturing, and first: the event reaches the window before any element or later listener.
    addListener("securitypolicyviolation", onViolation, true);
  }

  // The page's errors. Reading a thrown value may run page code (a getter, Error.prepareStackTrace), which may itself
  // call console.error: `reporting` keeps that from being reported again, and a read that throws reports nothing.
  const maxMessage = 512;
  const maxSource = 256;
  const maxStack = 2048;
  const maxArguments = 10;
  const objectToString = Object.prototype.toString;
  const ErrorType = Error;
  const captureStack = typeof ErrorType.captureStackTrace === "function" ? ErrorType.captureStackTrace : undefined;
  const StringType = String;
  const eventTarget = Object.getOwnPropertyDescriptor(Event.prototype, "target")?.get;
  const framePattern = /(https?:\/\/[^\s()]+):(\d+):(\d+)/;
  const consoleObject = globalThis.console;
  const consoleError = typeof consoleObject?.error === "function" ? consoleObject.error : undefined;
  let reporting = false;
  let errorsStopped = false;
  let wrapper;

  // A thrown value or console argument as text: a string as it is, an error (anything with a string `message`) as its
  // name and message, any other object by its tag only, so no page toString or toJSON runs.
  const describe = value => {
    if (typeof value === "string") {
      return value;
    }

    if (value === null || (typeof value !== "object" && typeof value !== "function")) {
      return StringType(value);
    }

    const message = value.message;
    if (typeof message !== "string") {
      return apply(objectToString, value, []);
    }

    const name = value.name;
    const prefix = typeof name === "string" && name.length > 0 ? name : "Error";
    return message.length > 0 ? prefix + ": " + message : prefix;
  };

  const stackOf = value => {
    if (value === null || typeof value !== "object") {
      return "";
    }

    const stack = value.stack;
    return typeof stack === "string" ? stack : "";
  };

  // One group's fields. Without a source, the first frame of `where` with an address gives it: where the error was
  // raised, or what called console.error. `stack` is the one the log shows.
  const group = (kind, message, where, stack, source, line, column) => {
    const fields = { kind, message: text(message, maxMessage), source: text(source, maxSource), line, column, stack: text(stack, maxStack) };
    const match = fields.source.length === 0 ? apply(exec, framePattern, [where]) : null;
    if (match !== null) {
      fields.source = text(match[1], maxSource);
      fields.line = +match[2];
      fields.column = +match[3];
    }

    return fields;
  };

  const record = read => {
    if (reporting || errorsStopped) {
      return;
    }

    reporting = true;
    try {
      const fields = read();
      const key = fields.kind + "\n" + fields.message + "\n" + fields.source + ":" + fields.line + ":" + fields.column;
      reportError(key, () => fields);
    } catch {
      // A value that throws when read is not reported; the page's own behaviour is unchanged.
    } finally {
      reporting = false;
    }
  };

  const onError = event => {
    if (eventTarget !== undefined && apply(eventTarget, event, []) !== window) {
      // A resource that failed to load (an image, a script) reaches a capturing listener too: not an exception.
      return;
    }

    record(() => {
      const stack = stackOf(event.error);
      const line = event.lineno > 0 ? event.lineno : 0;
      const column = event.colno > 0 ? event.colno : 0;
      return group("error", text(event.message, maxMessage), stack, stack, text(event.filename, maxSource), line, column);
    });
  };

  const onRejection = event => {
    record(() => {
      const reason = event.reason;
      const stack = stackOf(reason);
      return group("rejection", describe(reason), stack, stack, "", 0, 0);
    });
  };

  // The arguments as text, and the stack of the first error among them, which says more than where it was logged.
  const onConsole = args => {
    let message = "";
    let stack = "";
    const count = args.length < maxArguments ? args.length : maxArguments;
    for (let index = 0; index < count && message.length < maxMessage; index++) {
      const value = args[index];
      message += (index > 0 ? " " : "") + describe(value);
      if (stack.length === 0) {
        stack = stackOf(value);
      }
    }

    const caller = {};
    if (captureStack !== undefined) {
      apply(captureStack, ErrorType, [caller, wrapper]);
    }

    // A captured stack starts with an "Error" line of its own, which names no error here.
    const captured = stackOf(caller);
    const firstBreak = apply(indexOf, captured, ["\n"]);
    const headed = apply(startsWith, captured, ["Error"]) && firstBreak > 0;
    const where = headed ? apply(slice, captured, [firstBreak + 1]) : captured;
    return group("console", message, where, stack.length > 0 ? stack : where, "", 0, 0);
  };

  const reportError = reporter("errors", __SPAR_ERROR_MAX_GROUPS__, __SPAR_ERROR_MAX_ERRORS__, () => {
    errorsStopped = true;
    removeListener("error", onError, true);
    removeListener("unhandledrejection", onRejection, true);
    if (wrapper !== undefined && consoleObject.error === wrapper) {
      consoleObject.error = consoleError;
    }
  });

  if (consoleError !== undefined) {
    // A method named `error`, so stacks still read console.error; it returns what the page's original returns.
    wrapper = {
      error(...args) {
        record(() => onConsole(args));
        return apply(consoleError, this, args);
      },
    }.error;
    consoleObject.error = wrapper;
  }

  // On the window and capturing, so a page listener cannot stop the event before it; neither is ever cancelled here.
  addListener("error", onError, true);
  addListener("unhandledrejection", onRejection, true);
})();
