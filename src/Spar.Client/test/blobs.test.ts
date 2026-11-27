// Blob handles and resource addresses (decision "M3b's decide-first questions", item 2): the handle a result carries,
// its release through the host's own command, the loopback key in every address, and page-test blobs.
import { afterEach, describe, expect, test, vi } from "vitest";
import { readBlob, resourceUrl, writeBlob } from "../src/blobs.js";
import { connectSpar } from "../src/sdk.js";
import { createFakeSpar } from "../src/testing.js";
import { FakeHost } from "./fakeHost.js";

const id = "0123456789abcdef0123456789abcdef";

afterEach(() => {
  vi.restoreAllMocks();
});

/** A host whose channel has an address key, as a loopback channel does; `undefined` for a desktop host. */
function connected(key?: string): FakeHost {
  const host = new FakeHost();
  if (key !== undefined) {
    Object.assign(host, { addressKey: () => key });
  }

  connectSpar(host, "0123456789abcdef");
  host.open();
  return host;
}

describe("handles", () => {
  test("a handle reads as an object with its address on the origin", () => {
    connected();

    const blob = readBlob({ id, contentType: "image/png", size: 12, name: "a.png" });

    expect([blob.url, blob.contentType, blob.size, blob.name]).toEqual([`/_spar/blob/${id}`, "image/png", 12, "a.png"]);
  });

  test("a handle without a name has null", () => {
    connected();

    expect(readBlob({ id, contentType: "text/csv", size: 0 }).name).toBeNull();
  });

  test.each([
    [{ id: "../x", contentType: "a/b", size: 1 }],
    [{ id, contentType: "a/b", size: -1 }],
    [{ id, contentType: "a/b", size: 1.5 }],
    [{ id, size: 1 }],
  ])("a malformed handle is refused: %j", value => {
    connected();

    expect(() => readBlob(value)).toThrow(TypeError);
  });

  test("on loopback the address carries the channel's key", () => {
    connected("feedfacefeedfacefeedfacefeedface");

    const blob = readBlob({ id, contentType: "image/png", size: 1 });

    expect(blob.url).toBe(`/_spar/blob/${id}?k=feedfacefeedfacefeedfacefeedface`);
  });

  test("release calls the host's own releaseBlob with the id", async () => {
    const host = connected();
    const blob = readBlob({ id, contentType: "image/png", size: 1 });

    const released = blob.release();
    await vi.waitFor(() => expect(host.last("call")).toBeDefined());
    const sent = host.last("call")!;
    host.deliver({ type: "result", id: sent.id!, ok: true, payload: "" });
    await released;

    expect([sent.contract, sent.command, sent.payload]).toEqual(["spar", "releaseBlob", `"${id}"`]);
  });

  test("bytes reads the address and fails on a released handle", async () => {
    connected();
    const fetched = vi.spyOn(globalThis, "fetch").mockResolvedValueOnce(new Response(new Uint8Array([1, 2, 3])))
      .mockResolvedValueOnce(new Response("not-found", { status: 404, statusText: "Not Found" }));
    const blob = readBlob({ id, contentType: "application/octet-stream", size: 3 });

    expect(Array.from(await blob.bytes())).toEqual([1, 2, 3]);
    await expect(blob.bytes()).rejects.toThrow("404");
    expect(fetched.mock.calls[0]![0]).toBe(`/_spar/blob/${id}`);
  });
});

describe("resources", () => {
  test("the request travels as JSON in r=, with the key on loopback", () => {
    connected("feedfacefeedfacefeedfacefeedface");

    const url = resourceUrl("photos", "photo", () => '{"album":1,"number":2}');

    expect(url).toBe("/_spar/resource/photos/photo?r=%7B%22album%22%3A1%2C%22number%22%3A2%7D&k=feedfacefeedfacefeedfacefeedface");
  });

  test("a resource without a request has no r=", () => {
    connected();

    expect(resourceUrl("photos", "latest", undefined)).toBe("/_spar/resource/photos/latest");
  });
});

describe("page tests", () => {
  test("a fake blob reads its bytes from the fake and writes as a handle", async () => {
    const spar = createFakeSpar(new Map(), "0123456789abcdef");

    const blob = spar.blob(new Uint8Array([7, 8]), "text/csv", "a.csv");
    const handle = JSON.parse(writeBlob(blob)) as Record<string, unknown>;
    const read = readBlob(handle);

    expect(Array.from(await read.bytes())).toEqual([7, 8]);
    expect([handle["contentType"], handle["size"], handle["name"]]).toEqual(["text/csv", 2, "a.csv"]);
  });
});
