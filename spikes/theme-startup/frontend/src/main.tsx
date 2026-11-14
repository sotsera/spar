import { useEffect, useState } from "react";
import { createRoot } from "react-dom/client";
import { appMounted, mark, ready, setColorScheme, useManualReady } from "./spar";
import "./app.css";

mark("script-start");

// Spike knobs (query string): work=<ms> busy start-up work before the first render; manual=1 with data=<ms>
// keeps the splash until the "first data" arrives.
const query = new URLSearchParams(location.search);
const work = Number(query.get("work") ?? 0);
const manual = query.has("manual");
const dataMs = Number(query.get("data") ?? 0);

if (work > 0) {
  const until = performance.now() + work;
  while (performance.now() < until) {
    // Simulated synchronous start-up work.
  }
  mark("work-done");
}

if (manual) useManualReady();

function App() {
  const [rows, setRows] = useState<string[] | null>(manual ? null : ["Alpha", "Bravo", "Charlie"]);

  useEffect(() => {
    appMounted();
    if (manual) {
      const timer = setTimeout(() => setRows(["Alpha", "Bravo", "Charlie"]), dataMs);
      return () => clearTimeout(timer);
    }
  }, []);

  useEffect(() => {
    if (manual && rows) void ready();
  }, [rows]);

  return (
    <>
      <header>Theme startup spike</header>
      <main>
        <div className="card">
          <p>{rows ? rows.join(" · ") : "Loading…"}</p>
          <button onClick={() => setColorScheme("light")}>Light</button>
          <button onClick={() => setColorScheme("dark")}>Dark</button>
          <button onClick={() => setColorScheme("system")}>System</button>
        </div>
      </main>
    </>
  );
}

createRoot(document.getElementById("root")!).render(<App />);
