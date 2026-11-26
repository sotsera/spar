import { act } from "react";
import { createRoot } from "react-dom/client";
import { expect, test } from "vitest";
import { Preview } from "./components/Preview.js";

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

async function render(markdown: string): Promise<HTMLElement> {
  const host = document.createElement("div");
  const root = createRoot(host);
  await act(async () => root.render(<Preview markdown={markdown} />));
  return host;
}

test("a fenced block without a language renders", async () => {
  const host = await render("# Snippets\n\n```\ndu -ah . | sort -h\n```\n");
  expect(host.querySelector("pre")?.textContent).toContain("du -ah . | sort -h");
});

test("a fenced block with a language still renders", async () => {
  const host = await render("```sh\nsudo apt install git\n```\n");
  expect(host.querySelector("pre")?.textContent).toContain("sudo apt install git");
});
