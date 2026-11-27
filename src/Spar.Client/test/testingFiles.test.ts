// @vitest-environment happy-dom
// Page tests of files (@spar/app/testing): a scripted dialog gives a file the fake keeps, the page reads and writes
// it through the fake, and a drop on an area is answered with the files the test dropped.
import { describe, expect, test, vi } from "vitest";
import { files } from "../src/files.js";
import type { Result } from "../src/results.js";
import { createFakeSpar } from "../src/testing.js";

const fakeSpar = () => createFakeSpar(new Map(), "0123456789abcdef");

describe("files in page tests", () => {
  test("a scripted dialog gives a file whose text the page reads and replaces", async () => {
    const spar = fakeSpar();
    const notes = spar.file("notes.md", "# Notes", "readWrite");
    spar.on(files.open).succeed(notes);

    const opened = (await files.open({ title: "Open notes" })).unwrap();
    const text = (await opened.readText()).unwrap();
    (await opened.writeText("# Changed")).unwrap();

    expect([opened.name, opened.size, text]).toEqual(["notes.md", 7, "# Notes"]);
    expect(new TextDecoder().decode(spar.contents(opened))).toBe("# Changed");
    expect(spar.calls(files.open)).toEqual([{ title: "Open notes" }]);
  });

  test("a dialog can be scripted to be dismissed", async () => {
    const spar = fakeSpar();
    spar.on(files.save).fail("dismissed");

    const saved = await files.save().match({
      success: () => "saved",
      errors: { dismissed: () => "dismissed", accessDenied: () => "", unavailable: () => "", busy: () => "" },
    });

    expect(saved).toBe("dismissed");
  });

  test("a drop on an area gives the dropped files", async () => {
    const spar = fakeSpar();
    const area = document.createElement("div");
    const results: Result<{ readonly files: readonly { readonly name: string }[] }, never>[] = [];
    files.dropTarget(area, { accept: [".md"] }, result => results.push(result as never));

    spar.drop(area, [spar.file("a.md", "# A"), spar.file("b.md", "# B")]);
    await vi.waitFor(() => expect(results).toHaveLength(1));

    expect(results[0]!.unwrap().files.map(file => file.name)).toEqual(["a.md", "b.md"]);
  });
});
