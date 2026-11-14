// Compiles the consumer against each variant with permissive and strictest settings, skipLibCheck on and off,
// bundler and nodenext resolution, with TypeScript 7.0.2 (this spike) and 6.0.3 (VS Code's bundled compiler, tools/tsc6.ts).
// Then injects one fault into each kind of generated module to show which settings report it.
// Usage: node tools/matrix.ts. Run tools/build.ts first. Writes obj/matrix.md.
import { spawnSync } from "node:child_process";
import { readFileSync, rmSync, writeFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import { tsgo } from "./locate.ts";

const spikeRoot = join(dirname(fileURLToPath(import.meta.url)), "..");
const variants = ["ts", "js", "js-csmap"];
const compilers = [
  { name: "7.0.2", command: tsgo().command, args: [] as string[], extra: ["--pretty", "false"] },
  { name: "6.0.3", command: process.execPath, args: [join(spikeRoot, "tools", "tsc6.ts")], extra: [] as string[] },
];

const strictest = {
  strict: true, exactOptionalPropertyTypes: true, noUncheckedIndexedAccess: true, verbatimModuleSyntax: true, erasableSyntaxOnly: true,
  isolatedModules: true, noUnusedLocals: true, noUnusedParameters: true, noImplicitReturns: true, noImplicitOverride: true,
  noFallthroughCasesInSwitch: true, noPropertyAccessFromIndexSignature: true, allowUnreachableCode: false, allowUnusedLabels: false,
  noUncheckedSideEffectImports: true,
};
const flagSets = { permissive: { strict: false }, strictest } as const;
const resolutions = {
  bundler: { module: "ESNext", moduleResolution: "bundler" },
  nodenext: { module: "NodeNext", moduleResolution: "NodeNext" },
} as const;

interface Run { readonly lines: readonly string[]; readonly codes: string; readonly ms: number; }

function compile(compiler: (typeof compilers)[number], frontend: string, options: object, files: readonly string[]): Run {
  const config = join(frontend, "tsconfig.matrix.json");
  writeFileSync(config, JSON.stringify({ compilerOptions: { target: "ES2022", lib: ["ES2022", "DOM", "DOM.Iterable"], noEmit: true, types: [], ...options }, files }, null, 2));
  const started = performance.now();
  const result = spawnSync(compiler.command, [...compiler.args, "-p", config, ...compiler.extra], { cwd: frontend, encoding: "utf8" });
  const ms = performance.now() - started;
  const lines = (result.stdout + result.stderr).split(/\r?\n/).filter(l => /error TS\d+/.test(l));
  if (result.status !== 0 && !lines.length) throw new Error(`${compiler.name} failed: ${result.stdout}${result.stderr}`);
  const codes = [...new Set(lines.map(l => /TS\d+/.exec(l)![0]))].sort().join(" ");
  return { lines, codes, ms };
}

const out: string[] = ["# tsc matrix", ""];
out.push("app = src/app.ts + src/main.ts (+ src/types.ts with strict): must have 0 errors. bad = src/bad.ts (11 mistakes).", "");
out.push("| variant | tsc | flags | skipLibCheck | resolution | app | bad (count: codes) | ms (app) |", "|---|---|---|---|---|---|---|---|");
const details: string[] = [];

for (const variant of variants) {
  const frontend = join(spikeRoot, "obj", "variants", variant, "frontend");
  for (const compiler of compilers) {
    for (const [flagName, flags] of Object.entries(flagSets)) {
      for (const skipLibCheck of [true, false]) {
        for (const [resolutionName, resolution] of Object.entries(resolutions)) {
          const options = { ...flags, skipLibCheck, ...resolution };
          const appFiles = ["src/app.ts", "src/main.ts", ...(flagName === "strictest" ? ["src/types.ts"] : [])];
          const app = compile(compiler, frontend, options, appFiles);
          const bad = compile(compiler, frontend, options, ["src/bad.ts"]);
          out.push(`| ${variant} | ${compiler.name} | ${flagName} | ${skipLibCheck} | ${resolutionName} | ${app.lines.length}${app.codes ? ` (${app.codes})` : ""} | ${bad.lines.length}: ${bad.codes} | ${app.ms.toFixed(0)} |`);
          if (app.lines.length) details.push(`### app: ${variant} ${compiler.name} ${flagName} skipLibCheck=${skipLibCheck} ${resolutionName}`, "```", ...app.lines, "```");
          if (skipLibCheck && resolutionName === "bundler") details.push(`### bad.ts: ${variant} ${compiler.name} ${flagName}`, "```", ...bad.lines, "```");
        }
      }
    }
  }
}

// ------------------------------------------------------------ fault injection

out.push("", "## Fault injection", "");
out.push("One fault the generator might emit, injected into one generated module; strictest settings, bundler resolution.");
out.push("`src/fault.ts` assigns the result of `copyJobs.plan(…)` to a `number`, so its TS2322 shows the type the app sees.", "");
out.push("| variant | fault | tsc | skipLibCheck | other errors | type the app sees |", "|---|---|---|---|---|---|");

const faults = [
  { variant: "ts", file: "copyJobs.ts", fault: "unused local in the generated .ts", inject: (t: string) => t + "\nconst unusedGeneratedLocal = 1;\n" },
  { variant: "ts", file: "copyJobs.ts", fault: "undeclared type in the generated .ts", inject: (t: string) => t.replace("Call<CopyPlan, PlanError> =>", "Call<CopyPlanX, PlanError> =>") },
  { variant: "js", file: "copyJobs.d.ts", fault: "undeclared type in the generated .d.ts", inject: (t: string) => t.replace("=> Call<CopyPlan, PlanError>;", "=> Call<CopyPlanX, PlanError>;") },
];
for (const f of faults) {
  const frontend = join(spikeRoot, "obj", "variants", f.variant, "frontend");
  const generated = join(spikeRoot, "obj", "variants", f.variant, "obj", "spar", "client", f.file);
  const original = readFileSync(generated, "utf8");
  try {
    const injected = f.inject(original);
    if (injected === original) throw new Error(`fault not injected: ${f.fault}`);
    writeFileSync(generated, injected);
    writeFileSync(join(frontend, "src", "fault.ts"), `import { copyJobs } from "@spar/app/copyJobs";\nexport const planned: number = copyJobs.plan({ source: "a", target: "b", tables: [] });\n`);
    for (const compiler of compilers) {
      for (const skipLibCheck of [true, false]) {
        const run = compile(compiler, frontend, { ...strictest, skipLibCheck, ...resolutions.bundler }, ["src/fault.ts"]);
        const probe = run.lines.find(l => l.includes("fault.ts(2,") && l.includes("TS2322")) ?? "";
        const type = /Type '(.*)' is not assignable to type 'number'/.exec(probe)?.[1] ?? "?";
        const others = run.lines.filter(l => l !== probe);
        out.push(`| ${f.variant} | ${f.fault} | ${compiler.name} | ${skipLibCheck} | ${others.length ? others.map(l => /TS\d+/.exec(l)![0]).join(" ") : "none"} | \`${type}\` |`);
        details.push(`### fault: ${f.fault}, ${compiler.name}, skipLibCheck=${skipLibCheck}`, "```", ...run.lines, "```");
      }
    }
  } finally {
    writeFileSync(generated, original);
    rmSync(join(frontend, "src", "fault.ts"), { force: true });
  }
}

out.push("", "## Details", "", ...details);
const text = out.join("\n") + "\n";
writeFileSync(join(spikeRoot, "obj", "matrix.md"), text);
console.log(text);
