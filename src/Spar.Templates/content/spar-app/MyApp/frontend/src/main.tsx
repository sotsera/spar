import { connectSpar, hostChannel } from "@spar/app/sdk";
import { fingerprint } from "@spar/app/spar";
import { StrictMode, useEffect } from "react";
import { createRoot } from "react-dom/client";
import { App } from "./App.js";
import "./styles.css";

// Connects the page to the app's backend: through the window in the app, through loopback in browser tests.
const connection = connectSpar(hostChannel(), fingerprint);

function Root() {
  // The page is usable once its first render is on screen: the splash, if index.html has one, goes, then ui-ready.
  useEffect(() => connection.ready(), []);

  return <App />;
}

createRoot(document.getElementById("root")!).render(
  <StrictMode>
    <Root />
  </StrictMode>,
);
