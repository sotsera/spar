// Opens a Radix modal Dialog on a page taller than the window, measures what the policy let through, closes it, and
// reports: whether the dialog opened and held focus, whether page scrolling was locked and released, and every CSP
// violation. The default build passes the host's nonce to Radix's style helper; the cssom build replaces the helper.
import * as Dialog from "@radix-ui/react-dialog";
import { setNonce } from "get-nonce";
import { useEffect } from "react";
import { createRoot } from "react-dom/client";
import "./app.css";

declare const __SPAR_BUILD__: string;

const violations: Array<{ directive: string; blocked: string }> = [];
document.addEventListener("securitypolicyviolation", e => violations.push({ directive: e.effectiveDirective, blocked: e.blockedURI }));

const meta = document.querySelector<HTMLMetaElement>('meta[property="csp-nonce"]');
if (meta?.nonce) {
  setNonce(meta.nonce);
}

const wait = (ms: number) => new Promise(resolve => setTimeout(resolve, ms));

async function send(message: unknown): Promise<void> {
  const text = JSON.stringify(message);
  console.log("SPAR_PAGE " + text);
  await fetch("/_spar/log", { method: "POST", body: text }).catch(() => undefined);
}

function locked(): { attribute: boolean; overflow: string } {
  return { attribute: document.body.hasAttribute("data-scroll-locked"), overflow: getComputedStyle(document.body).overflow };
}

async function run(): Promise<void> {
  await wait(200);
  const before = locked();
  document.getElementById("open")!.click();
  await wait(400);

  const dialog = document.querySelector('[role="dialog"]');
  const open = { opened: dialog !== null, focusInside: dialog?.contains(document.activeElement) ?? false, ...locked() };
  const styleElements = document.querySelectorAll("style").length;
  const adopted = document.adoptedStyleSheets.length;

  document.getElementById("close")!.click();
  await wait(400);
  const after = { closed: document.querySelector('[role="dialog"]') === null, ...locked() };

  await send({ kind: "radix", build: __SPAR_BUILD__, nonceOffered: meta !== null, before, open, after, styleElements, adopted, violations });
  await send({ kind: "done" });
}

function App() {
  useEffect(() => void run(), []);
  return (
    <main className="page">
      <h1>Radix Dialog under a strict CSP</h1>
      <Dialog.Root>
        <Dialog.Trigger id="open">Open</Dialog.Trigger>
        <Dialog.Portal>
          <Dialog.Overlay className="overlay" />
          <Dialog.Content className="content" aria-describedby={undefined}>
            <Dialog.Title>A modal dialog</Dialog.Title>
            <input aria-label="Name" />
            <Dialog.Close id="close">Close</Dialog.Close>
          </Dialog.Content>
        </Dialog.Portal>
      </Dialog.Root>
    </main>
  );
}

createRoot(document.getElementById("root")!).render(<App />);
