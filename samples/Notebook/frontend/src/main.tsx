import { StrictMode, useEffect } from "react";
import { createRoot } from "react-dom/client";
import { App } from "./App.js";
import { useNotebook } from "./notebook.js";
import { describeFailure, startSpar } from "./spar.js";
import "./styles.css";

let report: (message: string) => void = () => undefined;
const connection = startSpar(error => report(describeFailure(error)));

function Root() {
  const notebook = useNotebook();
  report = notebook.showNotice;

  // The page is usable once the first render is on screen: tell the host (plan section 2, "Readiness").
  useEffect(() => connection.ready(), []);

  return <App notebook={notebook} />;
}

createRoot(document.getElementById("root")!).render(
  <StrictMode>
    <Root />
  </StrictMode>,
);
