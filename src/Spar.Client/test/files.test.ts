// @vitest-environment happy-dom
// Files the user chooses (decisions "M3c's decide-first questions", items 2 and 3, and "The drop design, from the
// file-drop spike"): the dialogs as the host's own commands, grants that show the page only names, reads and writes
// through the host, the grant in a generated request, and drop areas.
import { describe, expect, test, vi } from "vitest";
import type { BridgeMessage } from "../src/protocol.js";
import { files, readFile, readFolder, writeFile, type DroppedFiles, type PickedFile } from "../src/files.js";
import { connectSpar } from "../src/sdk.js";
import type { Call } from "../src/results.js";
import { FakeHost } from "./fakeHost.js";

const fileId = `fg_${"0123456789abcdef".repeat(2)}`;
const folderId = `fd_${"0123456789abcdef".repeat(2)}`;

/** A host that also takes messages with files, as WebView2's channel does; it records the files of each. */
class FilesHost extends FakeHost {
  readonly attached: (readonly File[])[] = [];

  sendWithFiles(message: string, sent: readonly File[]): void {
    this.attached.push(sent);
    this.send(message);
  }
}

function connected<T extends FakeHost>(host: T): T {
  connectSpar(host, "0123456789abcdef");
  host.open();
  return host;
}

async function lastCall(host: FakeHost): Promise<BridgeMessage> {
  await vi.waitFor(() => expect(host.last("call")).toBeDefined());
  return host.last("call")!;
}

function answer(host: FakeHost, call: BridgeMessage, ok: boolean, payload: string): void {
  host.deliver({ type: "result", id: call.id!, ok, payload });
}

function grant(name = "notes.md", size = 7, access = "read"): string {
  return JSON.stringify({ id: fileId, name, size, access });
}

describe("dialogs", () => {
  test("open asks the host's openFile with the title and filters, and gives a file with its name only", async () => {
    const host = connected(new FakeHost());

    const opening = files.open({ title: "Open notes", filters: [{ name: "Markdown", extensions: ["md", "markdown"] }] });
    const call = await lastCall(host);
    answer(host, call, true, grant());
    const file = (await opening).unwrap();

    expect([call.contract, call.command]).toEqual(["spar", "openFile"]);
    expect(JSON.parse(call.payload)).toEqual({ title: "Open notes", filters: [{ name: "Markdown", extensions: ["md", "markdown"] }] });
    expect([file.name, file.size, file.access]).toEqual(["notes.md", 7, "read"]);
    expect(JSON.stringify(file)).not.toContain(fileId);
    expect(Object.keys(file)).not.toContain("id");
  });

  test("a closed dialog is dismissed, a declared error", async () => {
    const host = connected(new FakeHost());
    const dismissed = vi.fn();

    const opening = files.save({ suggestedName: "out.md" }).match({
      success: () => "saved",
      errors: { dismissed, accessDenied: () => "denied", unavailable: () => "unavailable", busy: () => "busy" },
    });
    const call = await lastCall(host);
    answer(host, call, false, '{"code":"dismissed"}');
    await opening;

    expect(call.command).toBe("saveFile");
    expect(JSON.parse(call.payload)).toEqual({ suggestedName: "out.md" });
    expect(dismissed).toHaveBeenCalledOnce();
  });

  test("unavailable carries the host's reason", async () => {
    const host = connected(new FakeHost());

    const opening = files.openFolder({ access: "readWrite" }).match({
      success: () => "",
      errors: { dismissed: () => "", accessDenied: () => "", unavailable: e => e.reason, busy: () => "" },
    });
    const call = await lastCall(host);
    answer(host, call, false, '{"code":"unavailable","reason":"no file-chooser portal"}');

    expect(await opening).toBe("no file-chooser portal");
    expect(JSON.parse(call.payload)).toEqual({ access: "readWrite" });
  });

  test("openMany gives several files", async () => {
    const host = connected(new FakeHost());

    const opening = files.openMany();
    const call = await lastCall(host);
    answer(host, call, true, `[${grant("a.md")},${JSON.stringify({ id: `fg_${"f".repeat(32)}`, name: "b.md", size: 1, access: "read" })}]`);

    expect((await opening).unwrap().map(file => file.name)).toEqual(["a.md", "b.md"]);
  });
});

describe("reads and writes", () => {
  test("readText sends the grant's id and gives the text", async () => {
    const host = connected(new FakeHost());
    const file = readFile(JSON.parse(grant()));

    const reading = file.readText();
    const call = await lastCall(host);
    answer(host, call, true, '"# notes"');

    expect(call.command).toBe("readFile");
    expect(JSON.parse(call.payload)).toEqual({ grant: fileId, as: "text" });
    expect((await reading).unwrap()).toBe("# notes");
  });

  test("readBytes gives the bytes the host sent as base64", async () => {
    const host = connected(new FakeHost());
    const file = readFile(JSON.parse(grant()));

    const reading = file.readBytes();
    answer(host, await lastCall(host), true, '"AQID"');

    expect(Array.from((await reading).unwrap())).toEqual([1, 2, 3]);
  });

  test("a read past the limit is tooLarge, with the limit", async () => {
    const host = connected(new FakeHost());
    const file = readFile(JSON.parse(grant()));

    const reading = file.readText().match({
      success: () => -1,
      errors: {
        notFound: () => 0, accessDenied: () => 0, inUse: () => 0, tooLarge: e => e.limit, grantExpired: () => 0,
        outsideFolder: () => 0, readOnlyGrant: () => 0, failed: () => 0,
      },
    });
    answer(host, await lastCall(host), false, '{"code":"tooLarge","limit":4194304}');

    expect(await reading).toBe(4194304);
  });

  test("writeText sends the text, a lone surrogate replaced", async () => {
    const host = connected(new FakeHost());
    const file = readFile(JSON.parse(grant("out.md", 0, "readWrite")));

    const writing = file.writeText("a\ud800b");
    const call = await lastCall(host);
    answer(host, call, true, "null");
    await writing;

    expect(JSON.parse(call.payload)).toEqual({ grant: fileId, text: "a�b" });
  });

  test("a folder's calls carry the path inside it", async () => {
    const host = connected(new FakeHost());
    const folder = readFolder({ id: folderId, name: "notes", access: "readWrite" });

    const listing = folder.list("inner");
    const call = await lastCall(host);
    answer(host, call, true, '[{"name":"inner/a.md","isFolder":false,"size":3,"modified":"2026-11-27T10:00:00Z"}]');

    expect([call.command, JSON.parse(call.payload)]).toEqual(["listFolder", { grant: folderId, path: "inner" }]);
    expect((await listing).unwrap()).toEqual([{ name: "inner/a.md", isFolder: false, size: 3, modified: "2026-11-27T10:00:00Z" }]);
  });

  test("blob gives a handle on the origin", async () => {
    const host = connected(new FakeHost());
    const file = readFile(JSON.parse(grant("photo.png", 16)));

    const making = file.blob();
    answer(host, await lastCall(host), true, JSON.stringify({ id: "0123456789abcdef".repeat(2), contentType: "image/png", size: 16, name: "photo.png" }));

    expect((await making).unwrap().url).toBe(`/_spar/blob/${"0123456789abcdef".repeat(2)}`);
  });
});

describe("grants on the wire", () => {
  test("a request's grant is its id alone", () => {
    const file = readFile(JSON.parse(grant()));

    expect(writeFile(file)).toBe(`{"id":"${fileId}"}`);
  });

  test("only a grant the host gave can be sent", () => {
    const madeUp = { name: "x", size: 1, access: "read" } as unknown as PickedFile;

    expect(() => writeFile(madeUp)).toThrow(TypeError);
  });

  test.each([
    [{ id: "fg_x", name: "a", size: 1, access: "read" }],
    [{ id: folderId, name: "a", size: 1, access: "read" }],
    [{ id: fileId, name: "a", size: -1, access: "read" }],
    [{ id: fileId, name: "a", size: 1, access: "write" }],
  ])("a malformed grant is refused: %j", value => {
    expect(() => readFile(value)).toThrow(TypeError);
  });
});

describe("drop areas", () => {
  /** A drag event as the browser makes one, with a DataTransfer of these files. */
  function drag(type: string, dropped: readonly File[], types = ["Files"]): Event {
    const event = new Event(type, { bubbles: true, cancelable: true });
    Object.defineProperty(event, "dataTransfer", { value: { types, files: dropped, dropEffect: "none" } });
    return event;
  }

  test("a drag of files over the area is taken and marks it; leaving unmarks it", () => {
    connected(new FilesHost());
    const area = document.createElement("div");
    const stop = files.dropTarget(area, {}, () => {});

    const entered = drag("dragenter", []);
    area.dispatchEvent(entered);
    const marked = area.getAttribute("data-spar-drop");
    area.dispatchEvent(drag("dragleave", []));

    expect(entered.defaultPrevented).toBe(true);
    expect(marked).toBe("over");
    expect(area.hasAttribute("data-spar-drop")).toBe(false);
    stop();
  });

  test("a drag of the page's own content is left alone", () => {
    connected(new FilesHost());
    const area = document.createElement("div");
    const handler = vi.fn();
    files.dropTarget(area, {}, handler);

    const over = drag("dragover", [], ["text/plain"]);
    area.dispatchEvent(over);
    area.dispatchEvent(drag("drop", [], ["text/plain"]));

    expect(over.defaultPrevented).toBe(false);
    expect(handler).not.toHaveBeenCalled();
  });

  test("a drop sends the area's options with the event's files, and gives the grants", async () => {
    const host = connected(new FilesHost());
    const area = document.createElement("div");
    const results: Call<DroppedFiles, { notAccepted: { name: string }; tooMany: { limit: number } }>[] = [];
    files.dropTarget(area, { accept: [".csv"], limit: 10 }, result => results.push(result));
    const dropped = [new File(["1,2"], "a.csv")];

    const event = drag("drop", dropped);
    area.dispatchEvent(event);
    const call = await lastCall(host);
    answer(host, call, true, `{"files":[${grant("a.csv", 3)}],"folders":[]}`);
    const result = (await results[0]!).unwrap();

    expect(event.defaultPrevented).toBe(true);
    expect([call.command, JSON.parse(call.payload)]).toEqual(["dropped", { accept: [".csv"], limit: 10 }]);
    expect(host.attached).toEqual([dropped]);
    expect(result.files.map(file => file.name)).toEqual(["a.csv"]);
    expect(result.folders).toEqual([]);
  });

  test("a drop the area does not take says why", async () => {
    const host = connected(new FilesHost());
    const area = document.createElement("div");
    const results: Call<DroppedFiles, { notAccepted: { name: string }; tooMany: { limit: number } }>[] = [];
    files.dropTarget(area, { limit: 1 }, result => results.push(result));

    area.dispatchEvent(drag("drop", [new File([""], "a"), new File([""], "b")]));
    answer(host, await lastCall(host), false, '{"code":"tooMany","limit":1}');
    const why = await results[0]!.match({ success: () => 0, errors: { notAccepted: () => -1, tooMany: e => e.limit } });

    expect(why).toBe(1);
  });

  test("on a channel without files the drop goes as an ordinary call", async () => {
    const host = connected(new FakeHost());
    const area = document.createElement("div");
    files.dropTarget(area, {}, () => {});

    area.dispatchEvent(drag("drop", [new File([""], "a.md")]));

    expect((await lastCall(host)).command).toBe("dropped");
  });

  test("stopping removes the listeners and the mark", () => {
    connected(new FilesHost());
    const area = document.createElement("div");
    const handler = vi.fn();
    const stop = files.dropTarget(area, {}, handler);
    area.dispatchEvent(drag("dragenter", []));

    stop();
    area.dispatchEvent(drag("drop", [new File([""], "a.md")]));

    expect(area.hasAttribute("data-spar-drop")).toBe(false);
    expect(handler).not.toHaveBeenCalled();
  });
});
