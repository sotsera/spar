import { send } from "./sparBoot";
import { createRoot } from "react-dom/client";
import App from "./App";

// Survives hot updates (this module is not re-run by Fast Refresh), so a full reload is detectable.
(window as unknown as { __bootId: string }).__bootId ??= Math.random().toString(36).slice(2);
window.addEventListener("error", e => void send({ kind: "page-error", message: e.message }));
createRoot(document.getElementById("root")!).render(<App />);
