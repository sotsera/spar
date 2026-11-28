// @vitest-environment happy-dom
// The page-drawn title bar (docs/app-model.md 6.9): the host's first `titleBar` event, `{ maximized }`, starts the
// SDK's work for the document: an empty `data-spar-caption` gets the standard buttons with their names, `<html>` gets
// `data-spar-window`, a click on a caption button runs its action through `spar.windowAction`, the layout goes to the
// host as `spar.titleBar`, and the event `caption` gives the buttons their hover and pressed look. happy-dom lays
// nothing out, so the tests that measure give the elements their rectangles.
import { afterEach, describe, expect, test, vi } from "vitest";
import { connectSpar } from "../src/sdk.js";
import { fillCaptions, measureTitleBar, noDragSelector, receiveCaption } from "../src/titleBar.js";
import { addSdkStyle, noDragSelector as styledNoDrag, sdkStyleSheet } from "../src/vite/sdkStyle.js";
import { FakeHost } from "./fakeHost.js";

afterEach(() => {
  vi.restoreAllMocks();
});

function connected(): FakeHost {
  const host = new FakeHost();
  connectSpar(host, "0123456789abcdef");
  host.open();
  return host;
}

function titleBarEvent(host: FakeHost, maximized: boolean): void {
  host.deliver({ type: "event", name: "titleBar", payload: JSON.stringify({ maximized }) });
}

function captionButtons(): HTMLElement[] {
  return [...document.querySelectorAll<HTMLElement>("[data-spar-caption-button]")];
}

/** The calls the page sent to the host's own contract, by command. */
function hostCalls(host: FakeHost, command: string): unknown[] {
  return host.sent
    .filter(message => message.type === "call" && message.contract === "spar" && message.command === command)
    .map(message => JSON.parse(message.payload));
}

function place(element: Element, x: number, y: number, width: number, height: number): void {
  Object.defineProperty(element, "getBoundingClientRect", { value: () => ({ x, y, width, height }), configurable: true });
}

describe("the page-drawn title bar", () => {
  test("the first titleBar event fills an empty caption with the named buttons and marks the window's state", async () => {
    document.body.innerHTML =
      "<header data-spar-titlebar><span>Tool</span><input aria-label=\"Search\">" +
      "<div data-spar-caption data-spar-label-close=\"Chiudi\" data-spar-label-restore=\"Ripristina\"></div></header>";
    const host = connected();

    titleBarEvent(host, false);
    await vi.waitFor(() => expect(captionButtons()).toHaveLength(3));
    const normal = captionButtons().map(button => button.getAttribute("aria-label"));
    const state = document.documentElement.getAttribute("data-spar-window");
    titleBarEvent(host, true);
    await vi.waitFor(() => expect(document.documentElement.getAttribute("data-spar-window")).toBe("maximized"));
    const maximized = captionButtons().map(button => button.getAttribute("aria-label"));

    expect(normal).toEqual(["Minimize", "Maximize", "Chiudi"]);
    expect(state).toBe("normal");
    expect(maximized).toEqual(["Minimize", "Ripristina", "Chiudi"]);
    expect(document.documentElement.getAttribute("data-spar-platform")).toBe("windows");
    expect(captionButtons().every(button => button.tabIndex === -1 && button.getAttribute("type") === "button")).toBe(true);
    expect(captionButtons()[1]!.querySelectorAll("svg[data-spar-icon]")).toHaveLength(2);
  });

  test("a click on a caption button sends its action: maximize or restore by the window's state, and an app's own", async () => {
    document.body.innerHTML =
      "<header data-spar-titlebar><div data-spar-caption></div><button data-spar-caption=\"close\">X</button></header>";
    const host = connected();
    titleBarEvent(host, false);
    fillCaptions();
    await vi.waitFor(() => expect(captionButtons()).toHaveLength(3));

    const [minimize, maximize] = captionButtons();
    minimize!.click();
    maximize!.click();
    titleBarEvent(host, true);
    await vi.waitFor(() => expect(document.documentElement.getAttribute("data-spar-window")).toBe("maximized"));
    maximize!.click();
    document.querySelector<HTMLElement>("button[data-spar-caption=\"close\"]")!.click();
    document.querySelector<HTMLElement>("[data-spar-caption=\"\"]")!.click();

    await vi.waitFor(() => expect(hostCalls(host, "windowAction")).toHaveLength(4));
    expect(hostCalls(host, "windowAction")).toEqual([
      { action: "minimize" },
      { action: "maximize" },
      { action: "restore" },
      { action: "close" },
    ]);
  });

  test("the layout holds the bars, their controls and the buttons that take space, in CSS pixels", () => {
    document.body.innerHTML =
      "<header data-spar-titlebar id=\"bar\"><input id=\"search\"><a id=\"hidden\" href=\"#\">x</a>" +
      "<span data-spar-no-drag id=\"tabs\"></span><div data-spar-caption id=\"caption\"></div></header>";
    fillCaptions();
    const [minimize, maximize, close] = captionButtons();
    place(document.getElementById("bar")!, 0, 0, 800, 32);
    place(document.getElementById("search")!, 100, 4, 160.333, 24);
    place(document.getElementById("hidden")!, 0, 0, 0, 0);
    place(document.getElementById("tabs")!, 300, 0, 120, 32);
    place(document.getElementById("caption")!, 662, 0, 138, 32);
    place(minimize!, 662, 0, 46, 32);
    place(maximize!, 708, 0, 46, 32);
    place(close!, 754, 0, 46, 32);

    const layout = measureTitleBar();

    expect(layout).toEqual({
      bars: [[0, 0, 800, 32]],
      holes: [[100, 4, 160.33, 24], [300, 0, 120, 32], [662, 0, 138, 32], [662, 0, 46, 32], [708, 0, 46, 32], [754, 0, 46, 32]],
      minimize: [662, 0, 46, 32],
      maximize: [708, 0, 46, 32],
      close: [754, 0, 46, 32],
    });
  });

  test("the layout goes to spar.titleBar when it changes, and not again when it does not", async () => {
    document.body.innerHTML = "<header data-spar-titlebar id=\"bar\"><div data-spar-caption></div></header>";
    const host = connected();
    place(document.getElementById("bar")!, 0, 0, 640, 30);

    titleBarEvent(host, false);
    await vi.waitFor(() => expect(hostCalls(host, "titleBar").length).toBeGreaterThan(0));
    globalThis.dispatchEvent(new Event("resize"));
    await new Promise(resolve => setTimeout(resolve, 50));
    place(document.getElementById("bar")!, 0, 0, 700, 30);
    globalThis.dispatchEvent(new Event("resize"));
    await vi.waitFor(() => expect(hostCalls(host, "titleBar").length).toBe(2));

    const [first, second] = hostCalls(host, "titleBar") as { bars: number[][] }[];
    expect(first!.bars).toEqual([[0, 0, 640, 30]]);
    expect(second!.bars).toEqual([[0, 0, 700, 30]]);
  });

  test("the caption event gives the host's hover and press to the buttons", () => {
    document.body.innerHTML = "<header data-spar-titlebar><div data-spar-caption></div></header>";
    fillCaptions();
    const [minimize, maximize, close] = captionButtons();

    receiveCaption('{"hover":"close","pressed":null}');
    const hovered = [minimize, maximize, close].map(button => button!.hasAttribute("data-spar-hover"));
    receiveCaption('{"hover":"maximize","pressed":"maximize"}');

    expect(hovered).toEqual([false, false, true]);
    expect(maximize!.hasAttribute("data-spar-hover") && maximize!.hasAttribute("data-spar-pressed")).toBe(true);
    expect(close!.hasAttribute("data-spar-hover")).toBe(false);
  });

  test("a page that answers data-spar-window with a class on <html> settles instead of looping with the SDK", async () => {
    document.body.innerHTML = "<header data-spar-titlebar><div data-spar-caption></div></header>";
    const host = connected();
    let answers = 0;
    const page = new MutationObserver(() => {
      answers += 1;
      document.documentElement.classList.add("titled");
    });
    page.observe(document.documentElement, { attributes: true, attributeFilter: ["data-spar-window"] });

    titleBarEvent(host, false);
    titleBarEvent(host, true);
    await new Promise(resolve => setTimeout(resolve, 100));
    page.disconnect();

    expect(document.documentElement.getAttribute("data-spar-window")).toBe("maximized");
    expect(answers).toBeLessThanOrEqual(2);
  });

  test("an event that cannot be read is ignored with a warning", () => {
    const warn = vi.spyOn(console, "warn").mockImplementation(() => undefined);
    const host = connected();

    host.deliver({ type: "event", name: "titleBar", payload: "{\"maximized\":\"yes\"}" });
    host.deliver({ type: "event", name: "caption", payload: "not json" });

    expect(warn).toHaveBeenCalledTimes(2);
  });
});

describe("the SDK's stylesheet", () => {
  test("marks the same controls no-drag as the layout measures", () => {
    expect(styledNoDrag).toBe(noDragSelector);
    expect(sdkStyleSheet).toContain(`:where([data-spar-titlebar]) :where(${noDragSelector}){-webkit-app-region:no-drag;app-region:no-drag}`);
  });

  test("is linked right after the theme style, or at the head's start without one", () => {
    const themed = "<html><head><style id=\"spar-theme\">:root{}</style><link rel=\"stylesheet\" href=\"/app.css\"></head></html>";
    const plain = "<html><head><title>t</title></head></html>";

    expect(addSdkStyle(themed)).toBe(
      "<html><head><style id=\"spar-theme\">:root{}</style><link rel=\"stylesheet\" href=\"/spar.css\"><link rel=\"stylesheet\" href=\"/app.css\"></head></html>");
    expect(addSdkStyle(plain)).toBe("<html><head><link rel=\"stylesheet\" href=\"/spar.css\"><title>t</title></head></html>");
  });

  test("has only rules without specificity, so the app's own win", () => {
    const selectors = sdkStyleSheet
      .split("\n")
      .filter(line => line.length > 0 && !line.startsWith("/*") && !line.startsWith("@media"))
      .map(line => line.slice(0, line.indexOf("{")));

    expect(selectors.every(selector => selector.startsWith(":where("))).toBe(true);
  });
});
