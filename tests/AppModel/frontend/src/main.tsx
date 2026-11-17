import { createRoot } from "react-dom/client";
import { ready, setColorScheme } from "@spar/app/sdk";
import { CopyProgressView } from "./CopyProgressView";

createRoot(document.getElementById("root")!).render(<CopyProgressView jobId="job-1" />);

// Manual splash mode only: by default the SDK reports ui-ready, and removes the splash, by itself.
ready();

// The app's own theme choice; the host saves it and applies it before the next start.
export const useDark = () => setColorScheme("dark");
