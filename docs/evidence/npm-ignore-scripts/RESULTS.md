# `npm ci --ignore-scripts` with Vite 8

> A spike. Its code was deleted with `spikes/` at the start of M2; it is in git history at [`spikes/npm-ignore-scripts/`](https://github.com/sotsera/spar/tree/24f5db0942833047c81fa692908410596f3b8960/spikes/npm-ignore-scripts) (commit `24f5db0`), and this record and its `results/` moved here. Paths under `spikes/` below name that history.

**Question.** Plan section 3c proposes that the build run `npm ci --ignore-scripts`. Do Vite 8, its native bindings and
the TypeScript 7 compiler work when no package install script has run? Which installed packages declare install
scripts, and what do they do?

**Answer.** Yes, for both frontends. `npm ci --ignore-scripts` produced a `node_modules` tree that is file-for-file
identical to a normal `npm ci`, because **no package that gets installed on Windows declares an install script**. The
type check, `vite build`, the Vite dev server (API and CLI), Rolldown, Lightning CSS and the native `tsc` all worked.
The only install script in either lockfile belongs to `fsevents`, a macOS-only optional dependency of Vite that npm never
installs on Windows or Linux. `--ignore-scripts` works as the default.

One trap: `--ignore-scripts` also suppresses `pre`/`post` hooks of `npm run` when it comes from `.npmrc` or is passed
to `npm run`. The build should pass it on the `npm ci` command line only.

## Setup

Node v24.21.0, npm 11.19.0, Windows 11 Pro 10.0.26320 (x64). Other agents were running spikes at the same time, so times
are indicative only.

| Path | Contents |
|---|---|
| `react/frontend/` | Copy of `spikes/react/React.App/frontend`: `package.json`, `package-lock.json`, `index.html`, `bench.html`, `tsconfig.json`, `vite.config.ts`, `src/`. No `node_modules` copied. |
| `react/obj/spar/client/packages/@spar/` | Copy of the generated client packages that the `file:` dependencies point to (`../obj/spar/client/packages/@spar/*`). |
| `telemetry/` | The same for `spikes/browser-telemetry/react/React.App/frontend` (adds 10 OpenTelemetry dependencies). |
| `control/` | Log of a normal `npm ci` (scripts allowed) of the React lockfile, for comparison. Its `node_modules` was deleted afterwards. |
| `probe/` | A tiny app (`index.html`, `src/main.ts`, `src/style.css` with nesting and `rgb(255 0 0)`) so that a build exercises Lightning CSS and the TypeScript transform. The spike apps themselves contain no CSS. |
| `run-probe/` | `package.json` with `preinstall`, `prebuild` and `build` scripts, to see what `--ignore-scripts` suppresses. |
| `scan-scripts.mjs` | Lists every installed package with `preinstall`/`install`/`postinstall`/`prepare` or a `binding.gyp`, every `.node`/`.exe` at a package root, and every lockfile entry with `hasInstallScript`. |
| `vite-check.mjs` | Through Vite's API: builds `probe/`, then starts the app's dev server with its own `vite.config.ts`, fetches `/`, `/src/main.tsx` and `/@vite/client`, and closes it. |
| `vite-cli-check.mjs` | Starts `node_modules/vite/bin/vite.js` as a child process, fetches `/` and `/src/main.tsx`, then stops the child and waits for its exit. |
| `*/scan.json`, `*/vite-check.json`, `*/vite-cli-check.json`, `*/npm-ci.log`, `*/build*.log` | Raw outputs. |

Commands, from each `frontend/` folder:

```powershell
npm ci --ignore-scripts --prefer-offline --no-audit --no-fund --loglevel http
npm run build                                   # tsc --noEmit -p . && vite build
node ../../scan-scripts.mjs .
node ../../vite-check.mjs . 5291                # 5292 for telemetry
node ../../vite-cli-check.mjs . 5293            # 5294 for telemetry
```

## Results

### Install

| Frontend | Lockfile entries | Installed packages | Tarballs | Network fetches | `npm ci --ignore-scripts` |
|---|---:|---:|---:|---:|---:|
| React | 81 | 27 (incl. 3 `file:` links) | 24, all `(cache hit)` | 0 | 2.0 s |
| Telemetry | 106 | 51 (incl. 4 `file:` links) | 47, all `(cache hit)` | 0 | 2.8 s |

- **Same tree as with scripts.** A normal `npm ci` (scripts allowed, `--foreground-scripts --loglevel verbose`) of the
  React lockfile ran **no lifecycle script at all** and installed the same 937 files. The `--ignore-scripts` trees of both
  frontends also have exactly the file lists of the original spike installs (937 and 3,517 files), and the React
  install's Rolldown binding is byte-identical to the original's.
- The `file:` dependencies on the generated client became links to `../obj/spar/client/packages/@spar/*`, as before.

### Native code in the installed tree (Windows x64)

| Package | Native file | How it is selected |
|---|---|---|
| `@rolldown/binding-win32-x64-msvc` 1.2.19 | `rolldown-binding.win32-x64-msvc.node` | optional dependency of `rolldown`, filtered by `os`/`cpu` |
| `lightningcss-win32-x64-msvc` 1.33.0 | `lightningcss.win32-x64-msvc.node` | optional dependency of `lightningcss`, filtered by `os`/`cpu` |
| `@typescript/typescript-win32-x64` 7.0.2 | `lib/tsc.exe` | optional dependency of `typescript`, filtered by `os`/`cpu` |

There is **no esbuild** in either lockfile: Vite 8.3.8 does not depend on it here. All three native packages ship
prebuilt binaries in their tarballs and are chosen by npm's `os`/`cpu` filtering, which does not need scripts. The
lockfiles list the same packages for Linux (`*-linux-x64-gnu`, `*-linux-x64-musl`, …) the same way.

### Build and dev server

| Check | React | Telemetry |
|---|---|---|
| `npm run build` (native `tsc --noEmit`, then `vite build`) | pass, 1.28 s; 19 modules, 226.90 kB JS | pass; 23 modules, 228.25 kB JS |
| `SPAR_TELEMETRY=1 vite build` | — | pass; separate `telemetry` chunk 94.49 kB |
| Probe build: Lightning CSS (`css.transformer: "lightningcss"`, target `chrome100`) | `.card{color:red;inset:0}.card>span{user-select:none}`: nesting lowered and colour minified, so the binding ran | same output |
| Probe build: TypeScript transform | type annotation stripped | same |
| Rolldown and Lightning CSS loaded directly from Node | `rolldown` exports a function; `lightningcss.transform` minified `rgb(255 0 0)` to `red` | — |
| Dev server through the API, the app's `vite.config.ts` | listening in 77 ms; `/` 200, `/src/main.tsx` 200 (transformed, React pre-bundled by the dependency optimizer), `/@vite/client` 200; closed | 48 ms; same three 200s; closed |
| Dev server through the CLI (`vite.js` child process) | ready in 170 ms; `/` 200, `/src/main.tsx` 200; stopped (SIGTERM), exit observed | ready in 170 ms; same; stopped |

After every run no listener remained on ports 5291–5294, and no `node` process with these folders on its command line
was left.

### Install scripts

| Package | Where | Script | What it does | Runs during `npm ci`? |
|---|---|---|---|---|
| `fsevents` 2.3.3 | lockfile only (`hasInstallScript: true`), optional dependency of `vite`, `os: ["darwin"]` | `install: node-gyp rebuild` (`gypfile: true`) | Compiles the macOS FSEvents file-watching addon | Never on Windows or Linux: npm skips the package by its `os` field. Not tested on macOS, which Spar does not target. |
| `lightningcss` 1.33.0 | installed | `prepare: patch-package` | A development step of the Lightning CSS repository | No. npm runs `prepare` only for the root project and for git dependencies; the control run with scripts allowed did not run it (`patch-package` is not even installed). |

No installed package in either frontend has `preinstall`, `install`, `postinstall` or a `binding.gyp`. The telemetry
frontend's extra packages (`@opentelemetry/*`, `import-in-the-middle`, `require-in-the-middle`,
`module-details-from-path`, `cjs-module-lexer`, `es-module-lexer`, `debug`, `ms`, `zone.js`) declare none.

### What `--ignore-scripts` suppresses (`run-probe/`)

| Command | Root `preinstall` | `prebuild` | `build` |
|---|---|---|---|
| `npm install` | ran | — | — |
| `npm install --ignore-scripts` | **skipped** | — | — |
| `npm run build` | — | ran | ran |
| `npm run build --ignore-scripts` | — | **skipped** | ran |
| `npm run build` with `ignore-scripts=true` in `.npmrc` | — | **skipped** | ran |

So the flag belongs on the `npm ci` command line that the build runs. Putting it in a committed `.npmrc` would also
silently drop the app's own `pre`/`post` hooks, which the app author may rely on.

## Recommendation

- **Keep `npm ci --ignore-scripts` as the build's default**, passed on the command line, with the planned property to
  allow scripts. Nothing in the current template needs a script.
- **Make a skipped script visible.** After `npm ci`, the build can read `package-lock.json` and warn for each entry with
  `hasInstallScript: true` whose `os`/`cpu` match the current machine, naming the package and the opt-in property. For
  the current lockfiles that list is empty on Windows and Linux, so there is no noise. This is a proposal; it is not
  implemented or measured here.
- Packages an app might add that do rely on install scripts were **not tested**. They typically compile a native addon
  with `node-gyp`, download a binary or browser, or validate a platform binary. Such a package would fail at run time,
  not during `npm ci`, which is why the warning above matters.

## Observed, skipped, not tested

**Observed:** both installs (cache only, 0 fetches), the control install with scripts, the file-list and binding
comparisons, both `npm run build` runs and the telemetry build, the probe builds, both dev servers through the API and
the CLI, the script scans, and the `run-probe` matrix.

**Skipped:** a cold-cache install (it would download about 25–50 tarballs over a slow connection); `npm ci` through the
real Spar MSBuild targets (they do not exist yet).

**Not tested:** Linux and macOS installs (the lockfile analysis covers them, but nothing was run there); `fsevents`
behaviour on macOS; packages that need install scripts; Vite's file watcher and hot update (the dev server was only
fetched from); `npm` versions other than 11.19.0.
