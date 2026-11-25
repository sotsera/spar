import { useEffect } from "react";
import { createRoot } from "react-dom/client";
import { connectSpar, hostChannel, setColorScheme } from "@spar/app/sdk";
import { fingerprint } from "@spar/app/spar";
import { CopyProgressView } from "./CopyProgressView";

const connection = connectSpar(hostChannel(), fingerprint);

function Root() {
  // The first view has rendered: the splash goes, then the host reports ui-ready.
  useEffect(() => connection.ready(), []);

  return <CopyProgressView jobId="job-1" />;
}

createRoot(document.getElementById("root")!).render(<Root />);

// The app's own theme choice: the window takes it at once, and the next start shows it from the first frame.
export const useDark = () =>
  setColorScheme("dark").match({
    success: () => undefined,
    errors: { notSaved: () => console.warn("Dark until the app closes: the choice could not be saved.") },
  });
