import { useEffect, useState } from "react";
import { copyJobs } from "@spar/app/copyJobs";

export function CopyProgressView({ jobId }: { readonly jobId: string }) {
  const [rows, setRows] = useState(0n);
  const [status, setStatus] = useState("connecting");

  useEffect(() => {
    const abort = new AbortController();
    void (async () => {
      for await (const item of copyJobs.progress(jobId, { signal: abort.signal })) {
        item.match({
          success: p => { setRows(p.rows); setStatus("live"); },
          // Status events: reset is required (drop stale state); the others are optional.
          events: {
            reset: () => setRows(0n),
            gap: e => setStatus(`${e.dropped} updates skipped`),
            reconnecting: e => setStatus(`reconnecting (attempt ${e.attempt})`),
          },
          // Ignore the failure our own abort causes (StrictMode runs effects twice in development).
          failure: e => { if (!abort.signal.aborted) setStatus(e.message); },
        });
      }
    })();
    // Leaving the view aborts the subscription; the backend's token fires.
    return () => abort.abort();
  }, [jobId]);

  return <p aria-live="polite">{status}: {rows.toString()} rows</p>;
}
