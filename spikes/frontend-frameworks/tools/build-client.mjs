// Builds the stand-in @spar/app package into obj/spar/client (as the real client step would), then copies the
// generated copyJobs module next to it. Run from the spike folder: node tools/build-client.mjs
import { execFileSync } from "node:child_process";
import { copyFileSync, mkdirSync, readFileSync, existsSync } from "node:fs";

const tsc = "node_modules/typescript7/bin/tsc";
execFileSync(process.execPath, [tsc, "-p", "spar-app-src/tsconfig.json"], { stdio: "inherit" });
mkdirSync("obj/spar/client", { recursive: true });
for (const f of ["copyJobs.js", "copyJobs.d.ts", "package.json"]) {
  const from = `spar-app-src/${f}`, to = `obj/spar/client/${f}`;
  if (!existsSync(to) || !readFileSync(to).equals(readFileSync(from))) copyFileSync(from, to);
}
console.log("client built: obj/spar/client");
