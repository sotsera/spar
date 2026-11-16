// The notebook's state against a fake backend: every outcome the page handles, from the declared errors to a lost
// connection, with the values crossing the generated encoders and decoders as they would from the real host.
import { pages, type Page, type PageSummary } from "@spar/app/pages";
import { fakeSpar, type FakeSpar } from "@spar/app/testing";
import { act } from "react";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, expect, test, vi } from "vitest";
import { useNotebook, type Notebook } from "./notebook.js";

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

const welcome: Page = { id: "welcome", title: "Welcome", markdown: "# Welcome", updatedAt: "2026-11-16T09:00:00Z", revision: 3n };
const older: PageSummary = { id: "old", title: "Old", updatedAt: "2026-11-01T09:00:00Z" };
const newer: PageSummary = { id: "welcome", title: "Welcome", updatedAt: "2026-11-16T09:00:00Z" };

let spar: FakeSpar;
let root: Root | undefined;
let notebook: Notebook;

function Probe() {
  notebook = useNotebook();
  return null;
}

beforeEach(() => {
  spar = fakeSpar();
  spar.on(pages.list).succeed([older, newer]);
  spar.on(pages.get).succeed(welcome);
});

afterEach(() => {
  act(() => root?.unmount());
  root = undefined;
});

async function start(): Promise<void> {
  root = createRoot(document.createElement("div"));
  await act(async () => root!.render(<Probe />));
  await vi.waitFor(() => expect(notebook.summaries).toBeDefined());
}

async function openWelcome(): Promise<void> {
  await act(async () => notebook.open("welcome"));
  await vi.waitFor(() => expect(notebook.current.kind).toBe("open"));
}

function saveState() {
  const current = notebook.current;
  if (current.kind !== "open") {
    throw new Error(`No page is open: ${current.kind}`);
  }

  return current.save;
}

test("the list shows the newest page first", async () => {
  await start();

  expect(notebook.summaries).toEqual([newer, older]);
});

test("a page deleted elsewhere opens as missing", async () => {
  spar.on(pages.get).fail("notFound", { id: "gone" });
  await start();

  await act(async () => notebook.open("gone"));

  await vi.waitFor(() => expect(notebook.current).toEqual({ kind: "missing", id: "gone" }));
});

test("saving sends the draft over the page's revision, and the saved page comes back", async () => {
  spar.on(pages.save).succeed({ ...welcome, markdown: "# Hello", revision: 4n });
  await start();
  await openWelcome();

  act(() => notebook.edit("# Hello"));
  await act(async () => notebook.save());

  await vi.waitFor(() => expect(saveState()).toEqual({ kind: "saved" }));
  expect(spar.calls(pages.save)).toEqual([{ id: "welcome", markdown: "# Hello", baseRevision: 3n }]);
  expect(notebook.current).toMatchObject({ kind: "open", page: { revision: 4n }, draft: "# Hello" });
});

test("a page changed elsewhere keeps the draft, and keeping it saves over the other revision", async () => {
  spar.on(pages.save).fail("changedElsewhere", { currentRevision: 7n });
  await start();
  await openWelcome();

  act(() => notebook.edit("# Mine"));
  await act(async () => notebook.save());
  await vi.waitFor(() => expect(saveState()).toEqual({ kind: "conflict", currentRevision: 7n }));
  expect(notebook.current).toMatchObject({ draft: "# Mine" });

  spar.on(pages.save).succeed({ ...welcome, markdown: "# Mine", revision: 8n });
  await act(async () => notebook.keepMine());

  await vi.waitFor(() => expect(saveState()).toEqual({ kind: "saved" }));
  expect(spar.calls(pages.save).map(call => call.baseRevision)).toEqual([3n, 7n]);
});

test("a save the connection lost is reported, and the draft stays", async () => {
  spar.on(pages.save).failWith({ kind: "transport", code: "lost", execution: "unknown" });
  await start();
  await openWelcome();

  act(() => notebook.edit("# Unsure"));
  await act(async () => notebook.save());

  await vi.waitFor(() => expect(saveState()).toEqual({ kind: "failed", message: "Not saved: the notebook could not be reached." }));
  expect(notebook.current).toMatchObject({ draft: "# Unsure" });
});

test("typing while a save is on its way leaves the newer draft unsaved", async () => {
  let release: () => void = () => {};
  const arrived = new Promise<void>(resolve => (release = resolve));
  spar.on(pages.save).answer(async (request, reply) => {
    await arrived;
    return reply.succeed({ ...welcome, markdown: request.markdown, revision: 4n });
  });
  await start();
  await openWelcome();

  act(() => notebook.edit("# First"));
  await act(async () => notebook.save());
  act(() => notebook.edit("# First and more"));
  await act(async () => release());

  await vi.waitFor(() => expect(notebook.current).toMatchObject({ page: { revision: 4n }, draft: "# First and more" }));
  expect(saveState()).toEqual({ kind: "unsaved" });
});

test("a taken title is explained to the person creating the page", async () => {
  spar.on(pages.create).fail("titleTaken", { title: "Welcome" });
  await start();

  let message: string | undefined;
  await act(async () => {
    message = await notebook.create("Welcome");
  });

  expect(message).toBe("There is already a page called “Welcome”.");
  expect(spar.calls(pages.create)).toEqual([{ title: "Welcome", markdown: null }]);
});
