// Type checks and lint, printing each exit code. Expected failures (the bad configurations) are recorded in results/.
import { spawnSync } from "node:child_process";
import { writeFileSync } from "node:fs";

const node = process.execPath;
const run = (label, cwd, args, env = {}, save) => {
  const r = spawnSync(node, args, { cwd, encoding: "utf8", env: { ...process.env, ...env } });
  const text = (r.stdout + r.stderr).replace(/\x1b\[[0-9;]*m/g, "");
  if (save) writeFileSync(save, text);
  console.log(`${label}: exit ${r.status}${save ? ` (output: ${save})` : ""}`);
  if (!save && r.status !== 0) console.log(text);
};

for (const ts of ["typescript", "typescript7"]) {
  for (const config of ["tsconfig.json", "tsconfig.strictest.json"]) run(`react ${ts} ${config}`, "react", [`../node_modules/${ts}/bin/tsc`, "-p", config]);
}
run("vue vue-tsc tsconfig.json", "vue", ["../node_modules/vue-tsc/bin/vue-tsc.js", "-p", "tsconfig.json"]);
run("vue vue-tsc tsconfig.bad.json (expected to fail)", "vue", ["../node_modules/vue-tsc/bin/vue-tsc.js", "-p", "tsconfig.bad.json"], {}, "results/vue-tsc-bad.txt");
run("vue vue-tsc tsconfig.bail.json (RefUnwrapBailTypes augmented)", "vue", ["../node_modules/vue-tsc/bin/vue-tsc.js", "-p", "tsconfig.bail.json"]);
run("angular ngc tsconfig.bad.json (expected to fail)", "angular", ["../node_modules/@angular/compiler-cli/bundles/src/bin/ngc.js", "-p", "tsconfig.bad.json"], {}, "results/angular-ngc-bad.txt");
const lintFiles = ["src/lint-cases.tsx", "src/lint-cases-promise.tsx"];
run("react eslint (strictTypeChecked)", "react", ["../node_modules/eslint/bin/eslint.js", "-f", "json", "-o", "../../../artifacts/frontend-frameworks/react-eslint.json", ...lintFiles]);
run("react eslint (checkThenables)", "react", ["../node_modules/eslint/bin/eslint.js", "-f", "json", "-o", "../../../artifacts/frontend-frameworks/react-eslint-thenables.json", ...lintFiles], { SPAR_LINT_THENABLES: "1" });
run("lint report", ".", ["tools/lint-report.mjs", "../../artifacts/frontend-frameworks/react-eslint.json"], {}, "results/react-eslint.txt");
run("lint report (checkThenables)", ".", ["tools/lint-report.mjs", "../../artifacts/frontend-frameworks/react-eslint-thenables.json"], {}, "results/react-eslint-thenables.txt");
