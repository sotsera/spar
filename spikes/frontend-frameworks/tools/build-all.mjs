// Builds the stand-in client and every app variant into artifacts/frontend-frameworks/. Run from the spike folder.
import { execFileSync } from "node:child_process";

const node = process.execPath;
const out = "../../../artifacts/frontend-frameworks";
const run = (cwd, args, env = {}) => execFileSync(node, args, { cwd, stdio: "inherit", env: { ...process.env, NG_CLI_ANALYTICS: "false", ...env } });

run(".", ["tools/build-client.mjs"]);
const vite = "../node_modules/vite/bin/vite.js";
run("react", [vite, "build"]);
run("react", [vite, "build", "--mode", "development"], { NODE_ENV: "development", FRAMEWORK_OUT: `${out}/react-dev` });
run("vue", [vite, "build"]);
run("vue", [vite, "build"], { VUE_FULL: "1" });
const ng = "../node_modules/@angular/cli/bin/ng.js";
for (const configuration of ["", "zone", "jit", "default-optimization", "csp-clean"]) {
  run("angular", [ng, "build", ...(configuration ? ["--configuration", configuration] : [])]);
}
