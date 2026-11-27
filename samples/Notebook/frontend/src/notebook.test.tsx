// The notebook's state against a fake backend: every outcome the page handles, from the declared errors to a lost
// connection, with the values crossing the generated encoders and decoders as they would from the real host.
import { images } from "@spar/app/images";
import { pages, type Page, type PageSummary } from "@spar/app/pages";
import { preferences } from "@spar/app/preferences";
import { files } from "@spar/app/sdk";
import { fakeSpar, type FakeSpar } from "@spar/app/testing";
import { transfer } from "@spar/app/transfer";
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

test("a backend update fetches the list again, with the new code", async () => {
  await start();
  expect(spar.calls(pages.list)).toHaveLength(1);
  const renamed: PageSummary = { ...newer, title: "Welcome back" };
  spar.on(pages.list).succeed([older, renamed]);

  await act(async () => spar.backendUpdate({ types: ["Notebook.Pages.PagesService"] }));

  await vi.waitFor(() => expect(notebook.summaries).toEqual([renamed, older]));
  expect(spar.calls(pages.list)).toHaveLength(2);
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

test("a save that starts before React applied the last keystroke keeps that keystroke", async () => {
  // The autosave's timer can fire between a key's edit and the render that applies it (seen in the CI container: the
  // window journeys' last characters were lost). The save must not put the older draft it read back over the newer one.
  spar.on(pages.save).answer(async (request, reply) => reply.succeed({ ...welcome, markdown: request.markdown, revision: 4n }));
  await start();
  await openWelcome();

  act(() => {
    notebook.edit("# Hello");
    notebook.save();
  });

  await vi.waitFor(() => expect(notebook.current).toMatchObject({ kind: "open", draft: "# Hello" }));
  await vi.waitFor(() => expect(saveState()).toEqual({ kind: "saved" }));
  expect(spar.calls(pages.save).at(-1)).toEqual({ id: "welcome", markdown: "# Hello", baseRevision: expect.any(BigInt) });
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

test("closing the window saves the draft first, then lets it close", async () => {
  spar.on(pages.save).succeed({ ...welcome, markdown: "# Before closing", revision: 4n });
  await start();
  await openWelcome();

  act(() => notebook.edit("# Before closing"));
  let answer: unknown;
  await act(async () => {
    answer = await spar.requestClose();
  });

  expect(answer).toBe("close");
  expect(spar.calls(pages.save)).toEqual([{ id: "welcome", markdown: "# Before closing", baseRevision: 3n }]);
});

test("closing waits for a save on its way", async () => {
  let release: () => void = () => {};
  const arrived = new Promise<void>(resolve => (release = resolve));
  spar.on(pages.save).answer(async (request, reply) => {
    await arrived;
    return reply.succeed({ ...welcome, markdown: request.markdown, revision: 4n });
  });
  await start();
  await openWelcome();
  act(() => notebook.edit("# Saving"));
  await act(async () => notebook.save());

  const answer = spar.requestClose();
  await act(async () => release());

  expect(await answer).toBe("close");
  expect(spar.calls(pages.save)).toHaveLength(1);
});

test("a draft that cannot be saved makes closing ask, unless nobody can answer", async () => {
  spar.on(pages.save).failWith({ kind: "transport", code: "lost", execution: "unknown" });
  await start();
  await openWelcome();
  act(() => notebook.edit("# Unsure"));

  let asked: unknown;
  let automated: unknown;
  let sessionEnd: unknown;
  await act(async () => {
    asked = await spar.requestClose();
    automated = await spar.requestClose({ automated: true });
    sessionEnd = await spar.requestClose({ reason: "sessionEnd" });
  });

  expect(asked).toEqual({ ask: "Not saved: the notebook could not be reached. Close without your last changes?" });
  expect([automated, sessionEnd]).toEqual(["close", "close"]);
  expect(notebook.current).toMatchObject({ draft: "# Unsure" });
});

test("with nothing to save, closing closes at once", async () => {
  await start();
  await openWelcome();

  expect(await spar.requestClose()).toBe("close");
  expect(spar.calls(pages.save)).toEqual([]);
});

test("a page saved in another window shows its new text when nothing here is unsaved", async () => {
  await start();
  await openWelcome();
  const changed: Page = { ...welcome, markdown: "# Welcome\n\nFrom the other window", revision: 4n };
  spar.on(pages.get).succeed(changed);

  await act(async () => spar.stream(pages.changes).push({ id: "welcome", kind: "Saved", revision: 4n }));

  await vi.waitFor(() => expect(notebook.current).toMatchObject({ kind: "open", draft: changed.markdown, save: { kind: "saved" } }));
});

test("a page saved in another window while this one has unsaved typing shows the conflict at once", async () => {
  await start();
  await openWelcome();
  act(() => notebook.edit("# Welcome\n\nmine"));

  await act(async () => spar.stream(pages.changes).push({ id: "welcome", kind: "Saved", revision: 4n }));

  await vi.waitFor(() => expect(saveState()).toEqual({ kind: "conflict", currentRevision: 4n }));
  expect(notebook.current).toMatchObject({ draft: "# Welcome\n\nmine" });
});

test("the list is read again after a change made elsewhere", async () => {
  await start();
  const added: PageSummary = { id: "new", title: "New", updatedAt: "2026-11-27T09:00:00Z" };
  spar.on(pages.list).succeed([older, newer, added]);

  await act(async () => spar.stream(pages.changes).push({ id: "new", kind: "Created", revision: 1n }));

  await vi.waitFor(() => expect(notebook.summaries).toEqual([added, newer, older]));
});

test("the view mode and the last page come back from the last run, and a change is remembered", async () => {
  spar.on(preferences.get).succeed({ viewMode: "Read", lastPage: "welcome" });
  spar.on(preferences.setViewMode).succeed();
  spar.on(preferences.setLastPage).succeed();
  await start();

  await vi.waitFor(() => expect(notebook.current.kind).toBe("open"));
  expect(notebook.viewMode).toBe("Read");
  await act(async () => notebook.setViewMode("Write"));
  await act(async () => notebook.close());

  await vi.waitFor(() => expect(spar.calls(preferences.setLastPage)).toEqual([null]));
  expect(spar.calls(preferences.setViewMode)).toEqual(["Write"]);
});

test("a remembered page that is gone opens nothing", async () => {
  spar.on(preferences.get).succeed({ viewMode: "Split", lastPage: "gone" });
  spar.on(pages.get).fail("notFound", { id: "gone" });
  await start();

  await vi.waitFor(() => expect(spar.calls(pages.get)).toEqual([{ id: "gone" }]));
  await vi.waitFor(() => expect(notebook.current.kind).toBe("none"));
});

test("an image added to the open page gives the Markdown that shows it, and a refusal becomes the notice", async () => {
  spar.on(images.add).succeed({ pageId: "welcome", imageId: "0123456789abcdef0123456789abcdef" });
  await start();
  await openWelcome();
  const png = new File([new Uint8Array([0x89, 0x50, 0x4e, 0x47])], "diagram.png", { type: "image/png" });

  const markdown = await notebook.addImage(png);
  spar.on(images.add).fail("notAnImage", { contentType: "image/svg+xml" });
  const refused = await act(async () => notebook.addImage(png));

  expect(markdown).toBe("![diagram](image:0123456789abcdef0123456789abcdef)");
  expect(spar.calls(images.add)[0]).toEqual({ pageId: "welcome", contentType: "image/png", bytes: new Uint8Array([0x89, 0x50, 0x4e, 0x47]) });
  expect(refused).toBeUndefined();
  expect(notebook.notice).toBe("Only PNG, JPEG, GIF and WebP images can go in a page.");
});

test("files chosen in the open dialog are imported, and the first new page opens", async () => {
  spar.on(files.openMany).succeed([spar.file("Ideas.md", "# Ideas"), spar.file("empty.md", "")]);
  spar.on(transfer.import).answer((request, reply) => reply.succeed(request.files.map(file => (
    file.name === "Ideas.md"
      ? { fileName: file.name, pageId: "ideas", problem: null }
      : { fileName: file.name, pageId: null, problem: "Its name has no letter or digit to title a page with." }
  ))));
  spar.on(pages.get).succeed({ ...welcome, id: "ideas", title: "Ideas", markdown: "# Ideas" });
  await start();

  await act(async () => notebook.importFiles());

  await vi.waitFor(() => expect(notebook.current.kind === "open" && notebook.current.page.id).toBe("ideas"));
  expect(spar.calls(files.openMany)).toEqual([{ title: "Import Markdown", filters: [{ name: "Markdown", extensions: ["md", "markdown", "txt"] }] }]);
  expect(spar.calls(transfer.import)[0]!.files.map(file => file.name)).toEqual(["Ideas.md", "empty.md"]);
  expect(notebook.notice).toBe("Imported 1; not empty.md: Its name has no letter or digit to title a page with.");
});

test("a closed import dialog imports nothing and says nothing", async () => {
  spar.on(files.openMany).fail("dismissed");
  await start();

  await act(async () => notebook.importFiles());

  expect(spar.calls(transfer.import)).toEqual([]);
  expect(notebook.notice).toBeUndefined();
});

test("files dropped on the list are imported as they are", async () => {
  spar.on(transfer.import).succeed([{ fileName: "Dropped.md", pageId: "dropped", problem: null }]);
  spar.on(pages.get).succeed({ ...welcome, id: "dropped", title: "Dropped" });
  await start();

  await act(async () => notebook.importFiles([spar.file("Dropped.md", "# Dropped")]));

  await vi.waitFor(() => expect(notebook.current.kind === "open" && notebook.current.page.id).toBe("dropped"));
  expect(spar.calls(files.openMany)).toEqual([]);
});

test("the open page exports through the save dialog, and copies to the clipboard", async () => {
  spar.on(transfer.export).succeed("welcome.md");
  spar.on(transfer.copy).fail("busy");
  await start();
  await openWelcome();

  await act(async () => notebook.exportPage());
  const exported = notebook.notice;
  await act(async () => notebook.copyPage());

  expect(spar.calls(transfer.export)).toEqual([{ id: "welcome" }]);
  expect(exported).toBe("Exported to welcome.md.");
  expect(notebook.notice).toBe("Another program is using the clipboard. Try again.");
});

test("an image file dropped on the editor is added from its grant", async () => {
  spar.on(images.addFile).succeed({ pageId: "welcome", imageId: "0123456789abcdef0123456789abcdef" });
  await start();
  await openWelcome();

  const markdown = await notebook.addImageFile(spar.file("Sunset.png", new Uint8Array([0x89, 0x50])));

  expect(markdown).toBe("![Sunset](image:0123456789abcdef0123456789abcdef)");
  expect(spar.calls(images.addFile)[0]!.file.name).toBe("Sunset.png");
});
