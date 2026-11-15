# M1a: the Notebook's page and the frontend build

Recorded 2026-11-15. What it shows: the Notebook sample has a real page (React, TypeScript, Vite, Tailwind CSS, a Radix menu) that the app build type-checks and bundles, and seven journeys in Chromium use it against the real backend in the test process, with no CSP violation and a clean console. Decision: "Notebook page and the frontend build".

## Environment

Windows 11 Pro 10.0.26320, SDK 11.0.100, Node 24.21.0, Playwright 1.64.1 (headless shell 1248). Frontend packages, exact versions: React 19.3.1, Vite 8.3.8, `@vitejs/plugin-react` 6.3.0, Tailwind CSS 4.3.5 with `@tailwindcss/vite` and `@tailwindcss/typography` 0.5.20, `@radix-ui/react-dropdown-menu` 2.2.1, `react-markdown` 10.1.0 with `remark-gfm` 4.0.1, `@fontsource-variable/inter` 5.3.0, `lucide-react` 1.73.0, TypeScript 7.0.2. Not run on Linux.

## What was added

| Path | Contents |
|---|---|
| `samples/Notebook/frontend/` | The page: a sidebar (search, newest first, relative times), the page view (editor, live preview, three view modes, autosave and Ctrl+S, a save indicator), a new-page dialog with the backend's title errors, a delete confirmation, a conflict banner for `changedElsewhere`, a "page is gone" state, and a notice for failures. Both themes follow the system. 839 lines of TypeScript. |
| `src/Spar.Client/src/vite/` | `@spar/app/vite`, compiled for Node: no asset inlined as a `data:` URL, no source maps, base `/`, target ES2022. |
| `src/Spar.Sdk.Tasks/SparFrontendInstall.cs` | `npm ci --ignore-scripts` under a lease file in `obj/spar/`, skipped by a build that waited and finds the install current. |
| `src/Spar.Sdk/Sdk/Sdk.targets` | `SparFrontendInstall`, `SparFrontendTypeCheck` (`tsc --noEmit`), `SparFrontendBuild` (`vite build` into `obj/<variant>/…/frontend/`), after the client step and before `CoreCompile`; design-time builds skip them. |
| `samples/Notebook.EndToEndTests/` | Seven journeys and five screenshots. |

## Results (observed)

| Check | Result |
|---|---|
| Notebook journeys in Chromium | 7 of 7, 5.4–6.7 s for the run: the empty notebook and the first page; typing autosaves and the preview follows (task lists, bold), checked in the Markdown file; a taken title and an empty one explained in the form; a page saved elsewhere shows the conflict banner and "Keep mine" saves over it; deleting asks first and removes the file; search narrows the list and `/` focuses it; the dark theme follows the system. Each asserts no CSP violation, no bridge problem and a clean console. |
| First run of the journeys | 5 failed on CSP: `style-src-elem`, `blocked-uri: inline`, from the bundle. Radix's modal Dialog and AlertDialog lock scrolling by injecting a `<style>` element. Rebuilt on the native modal `<dialog>`: 0 violations. |
| Bundle (Test variant) | main JS 354 KB (113 KB gzip), the Markdown preview in its own chunk 164 KB (49 KB gzip), CSS 43 KB (8.4 KB gzip), Inter in 7 `woff2` subsets; 777 KB on disk |
| Frontend build | `npm ci` 23 s cold, 152 MB `node_modules` (142 top-level packages); `tsc --noEmit` and `vite build` in a Test-variant build of 3.7 s in all; an unchanged build skips the client step, the install, the type-check and the Vite build |
| `npm audit` | 0 after an `overrides` pin of `postcss-selector-parser` to 6.1.6 in the sample (the typography plugin pins 6.0.10, with a moderate advisory) and Vitest 4.1.10 → 4.1.12 in the SDK (two moderate advisories in its mock server) |
| `dotnet test --solution Spar.slnx` | 236 of 236; NativeAOT lane 154 of 154; `-mt` build clean; size check 98 files clean |

Screenshots (1280 × 800): [empty notebook](m1a-notebook-page/empty-light.png), [a page, light](m1a-notebook-page/page-light.png), [a page, dark](m1a-notebook-page/page-dark.png), [conflict](m1a-notebook-page/conflict.png), [title error](m1a-notebook-page/title-taken.png).

## Found and fixed

- Radix's modal dialogs inject a `<style>` element (above).
- Relative times followed the machine's locale ("1 secondo fa") in an English page; the formatters now use `<html lang>`.
- The editor's font drew `|---|` as box-drawing ligatures; ligatures are off in the editor.
- Tailwind v4 puts the typography plugin in its utilities layer, so a components-layer rule could not hide the bullets of task lists.
- The client step re-ran on every build: two of its Outputs were written only when their content changed.

## Not done or not verified

- **Not built:** packing the page into the app (the browser tests serve the Test variant's Vite output), the development server, SRI and the CSP hash in the plugin, the HTML sink with the app's Trusted Types policy (the preview builds React elements), and how a packaged `Spar.Sdk` runs npm.
- **Not run:** Linux, CI, a desktop window. The look was reviewed only on the screenshots; the user's judgement is in the plan's checks at the PC.
- **Two builds installing at once** relies on the lease; no test starts two installs together.
