import { lazy, Suspense, useEffect, useState } from "react";
import { hostSettings, send, violations } from "./sparBoot";
import logo from "./logo.svg";
import "./App.css";

const Lazy = lazy(() => import("./Lazy"));

export default function App() {
  const [count, setCount] = useState(0);
  const [lazyShown, setLazyShown] = useState(false);

  useEffect(() => {
    setLazyShown(true);
  }, []);

  useEffect(() => {
    if (!lazyShown) return;
    const timer = setTimeout(async () => {
      const button = document.getElementById("count") as HTMLButtonElement;
      button.click();
      await new Promise(resolve => setTimeout(resolve, 100));
      const app = document.querySelector(".app")!;
      const image = document.getElementById("logo") as HTMLImageElement;
      const results = {
        now: Date.now(),
        title: document.getElementById("title")?.textContent,
        cssColor: getComputedStyle(app).color,
        bootId: (window as unknown as { __bootId?: string }).__bootId,
        cssApplied: getComputedStyle(app).color === "rgb(20, 30, 40)",
        stylePropApplied: getComputedStyle(document.getElementById("styled")!).paddingLeft === "7px",
        lazyLoaded: document.getElementById("lazy") !== null,
        logoLoaded: image.complete && image.naturalWidth > 0,
        logoSrc: image.src.slice(0, 40),
        clickUpdated: button.textContent === "count 1",
        hostSettings,
        order: (window.__sparOrder ?? []).join(" > "),
        sdkViolations: violations.length,
        earlyViolations: window.__sparEarlyViolations?.length ?? "no-injected-script",
        trustedTypes: typeof (window as unknown as { trustedTypes?: unknown }).trustedTypes,
      };
      await send({ kind: "result", page: "react", results });
      if (!new URL(location.href).searchParams.has("stay")) await send({ kind: "done" });
    }, 300);
    return () => clearTimeout(timer);
  }, [lazyShown]);

  return (
    <div className="app">
      <h1 id="title">Spar CSP spike v1</h1>
      <img id="logo" src={logo} alt="logo" width={24} height={24} />
      <p id="styled" style={{ paddingLeft: 7 }}>style prop</p>
      <button id="count" onClick={() => setCount(c => c + 1)}>count {count}</button>
      {lazyShown && (
        <Suspense fallback={<p>loading</p>}>
          <Lazy />
        </Suspense>
      )}
    </div>
  );
}
