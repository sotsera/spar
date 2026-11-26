import { blobs } from "@spar/app/blobs";
import { connectSpar, hostChannel } from "@spar/app/sdk";
import { fingerprint } from "@spar/app/spar";

// The spike's measurements: each size, five times, three paths; the medians are taken from the saved report.
const connection = connectSpar(hostChannel(), fingerprint);
connection.ready();

const sizes = [16 * 1024, 256 * 1024, 1 << 20, 4 << 20, 12 << 20, 32 << 20];
const runs = 5;

interface Row {
  readonly size: number;
  readonly run: number;
  readonly bridge: number;
  readonly origin: number;
  readonly upload: number;
}

async function time(step: string, work: () => Promise<void>): Promise<number> {
  const start = performance.now();
  try {
    await work();
  } catch (error: unknown) {
    const detail = error instanceof Error ? `${error.message} ${JSON.stringify(error)}` : String(error);
    throw new Error(`${step}: ${detail}`);
  }

  return performance.now() - start;
}

function check(bytes: Uint8Array, size: number): void {
  if (bytes.length !== size) {
    throw new Error(`Expected ${size} bytes, got ${bytes.length}.`);
  }
}

async function measure(): Promise<void> {
  const rows: Row[] = [];
  for (const size of sizes) {
    const upload = new Uint8Array(size);
    for (let run = 0; run < runs; run++) {
      const bridge = await time(`get ${size}`, async () => check(await blobs.get(size).unwrap(), size));
      const origin = await time(`fetch ${size}`, async () => {
        const response = await fetch(`/bin/blob-${size}.webp`, { cache: "no-store" });
        check(new Uint8Array(await response.arrayBuffer()), size);
      });
      const sent = await time(`put ${size}`, async () => {
        const length = await blobs.put(upload).unwrap();
        if (length !== size) {
          throw new Error(`The backend got ${length} bytes of ${size}.`);
        }
      });
      rows.push({ size, run, bridge, origin, upload: sent });
    }
  }

  await blobs.report(JSON.stringify({ userAgent: navigator.userAgent, rows })).unwrap();
  document.getElementById("status")!.textContent = "Done";
}

measure().catch(async (error: unknown) => {
  await blobs.report(JSON.stringify({ error: String(error) })).unwrap();
});
