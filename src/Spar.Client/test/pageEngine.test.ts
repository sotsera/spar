// @vitest-environment happy-dom
// The window-test engine (src/Spar.Testing/Window/PageEngine.js), run here against happy-dom's document: roles,
// accessible names, the label, text and attribute queries, chains, filters, strict mode, element state and the failure
// explanations. Layout is not here (happy-dom lays nothing out), so visibility, stability, hit tests and scrolling are
// checked in a real window by tests/Spar.WindowTests. The engine is plain JavaScript in Spar.Testing, embedded there.
import { readFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import { beforeAll, beforeEach, describe, expect, test } from "vitest";

// The path from this file, not a URL: happy-dom replaces the global URL.
const source = readFileSync(join(dirname(fileURLToPath(import.meta.url)), "../../Spar.Testing/Window/PageEngine.js"), "utf8");

interface Engine {
  run(request: string): Promise<Record<string, unknown>>;
  roleOf(element: Element): string | null;
  accessibleName(element: Element): string;
}

type Step = Record<string, unknown>;

let engine: Engine;

beforeAll(() => {
  // The engine installs itself on its world's global object, as a document-start script does.
  new Function(source)();
  engine = (globalThis as unknown as { __sparAutomation: Engine }).__sparAutomation;
});

beforeEach(() => {
  document.body.innerHTML = "";
});

function page(html: string): void {
  document.body.innerHTML = html;
}

async function run(op: string, fields: Record<string, unknown>): Promise<Record<string, unknown>> {
  return engine.run(JSON.stringify({ op, ...fields }));
}

async function count(...chain: Step[]): Promise<number> {
  const result = await run("count", { chain });
  expect(result["error"]).toBeUndefined();
  return result["count"] as number;
}

async function texts(...chain: Step[]): Promise<string[]> {
  const result = await run("state", { chain, all: true });
  if (result["count"] === 1) {
    return [result["text"] as string];
  }

  return (result["texts"] as string[] | undefined) ?? [];
}

const role = (name: string, more: Step = {}): Step => ({ kind: "role", role: name, ...more });

function byId(id: string): Element {
  const element = document.getElementById(id);
  if (element === null) {
    throw new Error("no element " + id);
  }

  return element;
}

describe("roles", () => {
  test("come from the HTML element when there is no role attribute", () => {
    page(`
      <a id="link" href="/x">x</a><a id="anchor">x</a><button id="button">b</button>
      <input id="text"><input id="check" type="checkbox"><input id="search" type="search"><input id="range" type="range">
      <input id="number" type="number"><input id="listed" list="l"><input id="file" type="file"><textarea id="area"></textarea>
      <select id="combo"><option id="option">o</option></select><select id="box" multiple></select>
      <ul id="list"><li id="item">i</li></ul><nav id="nav"></nav><main id="main"></main><h3 id="heading">h</h3>
      <dialog id="dialog"></dialog><section id="plain"></section><section id="named" aria-label="n"></section>
      <table id="table"><tr id="row"><th id="th">h</th><td id="td">d</td></tr></table><img id="decor" alt=""><img id="pic" alt="p">
      <output id="output"></output><progress id="progress"></progress><hr id="rule"><form id="form"></form>`);

    const expected: Record<string, string | null> = {
      link: "link", anchor: null, button: "button", text: "textbox", check: "checkbox", search: "searchbox",
      range: "slider", number: "spinbutton", listed: "combobox", file: "button", area: "textbox", combo: "combobox",
      option: "option", box: "listbox", list: "list", item: "listitem", nav: "navigation", main: "main",
      heading: "heading", dialog: "dialog", plain: null, named: "region", table: "table", row: "row", th: "rowheader",
      td: "cell", decor: "presentation", pic: "img", output: "status", progress: "progressbar", rule: "separator",
      form: "form",
    };
    for (const [id, wanted] of Object.entries(expected)) {
      expect(engine.roleOf(byId(id)), id).toBe(wanted);
    }
  });

  test("take the first known token of the role attribute, but never make a focusable element presentational", () => {
    page(`<div id="a" role="unknown switch checkbox"></div><button id="b" role="presentation">x</button><span id="c" role="none"></span>`);

    expect(engine.roleOf(byId("a"))).toBe("switch");
    expect(engine.roleOf(byId("b"))).toBe("button");
    expect(engine.roleOf(byId("c"))).toBe("none");
  });

  test("header and footer are landmarks only outside sectioning content", () => {
    page(`<header id="top"></header><article><header id="inner"></header><footer id="foot"></footer></article>`);

    expect(engine.roleOf(byId("top"))).toBe("banner");
    expect(engine.roleOf(byId("inner"))).toBeNull();
    expect(engine.roleOf(byId("foot"))).toBeNull();
  });
});

describe("accessible names", () => {
  test("follow aria-labelledby first, even to hidden content", () => {
    page(`<span id="first" hidden>Billing</span><span id="second">address</span>
      <input id="field" aria-labelledby="first second" aria-label="ignored">`);

    expect(engine.accessibleName(byId("field"))).toBe("Billing address");
  });

  test("use aria-label, then the label, then the placeholder and the title", () => {
    page(`<input id="aria" aria-label="Search"><label for="for">Email</label><input id="for">
      <label>Phone <input id="nested"></label><input id="placeholder" placeholder="Name"><input id="title" title="Age">`);

    expect(engine.accessibleName(byId("aria"))).toBe("Search");
    expect(engine.accessibleName(byId("for"))).toBe("Email");
    expect(engine.accessibleName(byId("nested"))).toBe("Phone");
    expect(engine.accessibleName(byId("placeholder"))).toBe("Name");
    expect(engine.accessibleName(byId("title"))).toBe("Age");
  });

  test("leave out the labelled control's own value, and take an embedded control's value inside another label", () => {
    page(`<label><span class="sr-only">Markdown of Groceries</span><textarea id="editor"># Groceries</textarea></label>
      <label id="label" for="target">Quantity <input id="inner" value="3"> per day</label><input id="target">`);

    expect(engine.accessibleName(byId("editor"))).toBe("Markdown of Groceries");
    expect(engine.accessibleName(byId("target"))).toBe("Quantity 3 per day");
  });

  test("come from content for buttons, links and headings, without hidden parts", () => {
    page(`<button id="save"><svg aria-hidden="true"></svg>Save <span hidden>secret</span>page</button>
      <a id="link" href="#"><img alt="Home"> page</a><h2 id="heading">Recent <em>orders</em></h2>
      <button id="icon" title="Close"></button><div id="div">Not named from content</div>`);

    expect(engine.accessibleName(byId("save"))).toBe("Save page");
    expect(engine.accessibleName(byId("link"))).toBe("Home page");
    expect(engine.accessibleName(byId("heading"))).toBe("Recent orders");
    expect(engine.accessibleName(byId("icon"))).toBe("Close");
    expect(engine.accessibleName(byId("div"))).toBe("");
  });

  test("of fieldsets, tables and figures come from their legend and captions", () => {
    page(`<fieldset id="set"><legend>Shipping</legend></fieldset><table id="table"><caption>Totals</caption></table>
      <figure id="figure"><img alt="x"><figcaption>Chart</figcaption></figure><input id="submit" type="submit">`);

    expect(engine.accessibleName(byId("set"))).toBe("Shipping");
    expect(engine.accessibleName(byId("table"))).toBe("Totals");
    expect(engine.accessibleName(byId("figure"))).toBe("Chart");
    expect(engine.accessibleName(byId("submit"))).toBe("Submit");
  });
});

describe("role queries", () => {
  beforeEach(() => {
    page(`<h1>Orders</h1><h2>Recent orders</h2><button>Save</button><button disabled>Save draft</button>
      <button aria-pressed="true">Bold</button><button aria-expanded="false">Menu</button>
      <input type="checkbox" aria-label="Agree" checked><div role="checkbox" aria-checked="mixed" aria-label="All"></div>
      <button hidden>Hidden</button><div aria-hidden="true"><button>Inside hidden</button></div>`);
  });

  test("match names as substrings without case by default, and whole with exact", async () => {
    expect(await count(role("heading", { name: "orders" }))).toBe(2);
    expect(await count(role("heading", { name: "Orders", exact: true }))).toBe(1);
    expect(await count(role("button", { name: "save" }))).toBe(2);
  });

  test("match names by regular expression", async () => {
    expect(await count(role("heading", { name: { source: "^Recent", flags: "" } }))).toBe(1);
    expect(await count(role("button", { name: { source: "^save$", flags: "i" } }))).toBe(1);
  });

  test("filter by state and level", async () => {
    expect(await count(role("heading", { level: 2 }))).toBe(1);
    expect(await count(role("button", { disabled: true }))).toBe(1);
    expect(await count(role("button", { pressed: true }))).toBe(1);
    expect(await count(role("button", { expanded: false }))).toBe(1);
    expect(await count(role("checkbox", { checked: true }))).toBe(1);
    expect(await count(role("checkbox", { checked: "mixed" }))).toBe(1);
  });

  test("leave out hidden elements unless asked", async () => {
    expect(await count(role("button", { name: "hidden" }))).toBe(0);
    expect(await count(role("button", { name: "hidden", includeHidden: true }))).toBe(2);
  });
});

describe("label, text and attribute queries", () => {
  test("find controls by label, aria-labelledby and aria-label, but not by the label's nested controls", async () => {
    page(`<label for="a">Title</label><input id="a"><label>Amount <input id="b" value="Title"></label>
      <span id="t">Note</span><textarea aria-labelledby="t"></textarea><input aria-label="Title of the page">`);

    expect(await count({ kind: "label", text: "Title" })).toBe(2);
    expect(await count({ kind: "label", text: "Title", exact: true })).toBe(1);
    expect(await count({ kind: "label", text: "note" })).toBe(1);
  });

  test("find the smallest elements holding a text", async () => {
    page(`<main><section><p>Nothing matches “rezzz”.</p></section><p>Saved <b>today</b></p></main>`);

    expect(await texts({ kind: "text", text: "nothing matches" })).toEqual(["Nothing matches “rezzz”."]);
    expect(await count({ kind: "text", text: "today", exact: true })).toBe(1);
    expect(await count({ kind: "text", text: "Saved today", exact: true })).toBe(1);
    expect(await count({ kind: "text", text: { source: "^Saved", flags: "" } })).toBe(1);
  });

  test("find elements by placeholder, alt text and title", async () => {
    page(`<input placeholder="Search pages"><img alt="Logo"><span title="Last saved">2 min</span>`);

    expect(await count({ kind: "placeholder", text: "search" })).toBe(1);
    expect(await count({ kind: "alt", text: "Logo", exact: true })).toBe(1);
    expect(await count({ kind: "title", text: "saved" })).toBe(1);
  });
});

describe("chains, filters and strict mode", () => {
  beforeEach(() => {
    page(`<nav aria-label="Pages"><ul><li><button>Reading list</button></li><li><button>Recipes</button></li>
      <li><button>Travel</button></li></ul></nav><div role="dialog" aria-label="New page"><button>Create page</button>
      <button>Cancel</button></div><button>New page</button>`);
  });

  test("search inside the elements of the step before", async () => {
    expect(await count(role("navigation", { name: "Pages" }), role("listitem"))).toBe(3);
    expect(await count(role("dialog", { name: "New page" }), role("button"))).toBe(2);
  });

  test("pick by position, from the end too", async () => {
    expect(await texts(role("listitem"), { kind: "nth", index: 0 })).toEqual(["Reading list"]);
    expect(await texts(role("listitem"), { kind: "nth", index: -1 })).toEqual(["Travel"]);
    expect(await count(role("listitem"), { kind: "nth", index: 5 })).toBe(0);
  });

  test("filter by text, by its absence, and by what an element holds", async () => {
    expect(await count(role("listitem"), { kind: "filter", hasText: "re" })).toBe(2);
    expect(await count(role("listitem"), { kind: "filter", hasNotText: "re" })).toBe(1);
    expect(await count(role("listitem"), { kind: "filter", has: [role("button", { name: "Travel" })] })).toBe(1);
  });

  test("an action on several elements fails at once and lists them", async () => {
    const result = await run("focus", { chain: [role("button", { name: "page" })] });

    expect(result["strict"]).toBe(2);
    expect(result["matches"]).toEqual(['button <button> "Create page"', 'button <button> "New page"']);
  });

  test("an element that is not there yet asks to be tried again, and says what is near", async () => {
    const result = await run("focus", { chain: [role("button", { name: "Save" })] });

    expect(result["retry"]).toBe("no element matches");
    expect(result["step"]).toBe(0);
    expect(result["near"]).toEqual([
      'button <button> "Reading list"', 'button <button> "Recipes"', 'button <button> "Travel"',
      'button <button> "Create page"', 'button <button> "Cancel"',
    ]);
  });
});

describe("state and options", () => {
  test("reads text, value, checked and focus of one element", async () => {
    page(`<label>Title <input value="Groceries"></label><input type="checkbox" aria-label="Done" checked>`);
    const field = await run("state", { chain: [{ kind: "label", text: "Title" }] });
    const box = await run("state", { chain: [role("checkbox")] });

    expect(field).toMatchObject({ count: 1, value: "Groceries", role: "textbox", name: "Title", enabled: true, editable: true });
    expect(box).toMatchObject({ count: 1, checked: true, value: "on" });
  });

  test("a request the engine does not know answers with an error", async () => {
    expect(await run("nothing", {})).toEqual({ error: "Unknown operation: nothing" });
    expect(await engine.run("{")).toHaveProperty("error");
  });
});
