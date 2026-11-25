# Styles added at run time under the strict CSP: spike results

> A spike. Its code was deleted with `spikes/` at the start of M2; it is in git history at [`spikes/csp-style-injection/`](https://github.com/sotsera/spar/tree/24f5db0942833047c81fa692908410596f3b8960/spikes/csp-style-injection) (commit `24f5db0`), and this record and its `results/` moved here. Paths under `spikes/` below name that history.

Recorded 2026-11-15 on the development machine: Windows 11 Pro 10.0.26320, SDK 11.0.100, Node 24.21.0. Engines: Chromium 156.0.8078.4 (Playwright's headless shell, through the loopback host), WebView2 Runtime 157.0.4341.46 (offscreen window, never activated), WebKitGTK 6.0 2.52.6 (container `spar-test-host:webkit6`, Xvfb, uid 1000). React 19.3.1, `@radix-ui/react-dialog` 1.3.1 (through `react-remove-scroll` 2.7.2, `react-style-singleton` 2.2.3, `get-nonce` 1.0.1), Vite 8.3.8.

**Question** (user, 2026-11-15, after the Notebook's dialogs moved off Radix): a library that adds a `<style>` element at run time, such as Radix's modal Dialog locking page scrolling, is blocked by Spar's base policy (`style-src 'self'`). Can a per-document nonce, or stylesheets built through the CSSOM, let it work without `'unsafe-inline'`, on all three engines?

**Short answer.** Both work, identically on Chromium, WebView2 and WebKitGTK 6.0.
- **A nonce:** with `style-src 'self' 'nonce-…'`, a `<style>` element carrying the nonce applies, one with a wrong nonce or none stays blocked, and Radix's Dialog locks and releases scrolling with no violation once the page passes the nonce to `get-nonce`. The nonce is readable from `<meta property="csp-nonce">` as `meta.nonce`, while `getAttribute("nonce")` returns an empty string on all three engines (nonce hiding).
- **Constructed stylesheets:** `new CSSStyleSheet()` with `replaceSync`, `replace` or `insertRule`, adopted by the document or by a shadow root, applies under the unchanged base policy with no violation. So does `insertRule` into the page's own linked stylesheet, and `element.style.setProperty`. With `react-style-singleton` replaced by a module that adopts a constructed sheet (`frontend/src/cssom-singleton.ts`, swapped in by a Vite alias), Radix's Dialog locks and releases scrolling with no violation and no policy change.
- **Still blocked under both:** a `style` attribute set from script (`style-src-attr`), and any `<style>` without the right nonce.
- **What blocked styles cost Radix:** the dialog still opens and keeps focus inside; only the scroll lock is missing (the page behind can scroll), with one `style-src-elem` violation per opening.

## Layout

| Path | Contents |
|---|---|
| `pages/profiles.json` | `strict` (Spar's base policy with `require-trusted-types-for 'script'`) and `nonce` (the same with `'nonce-{NONCE}'` in `style-src`). |
| `pages/probes.html`, `probes.js`, `probes.css` | Ten probes, each colouring one element by one route: a `<style>` element with no nonce, the right nonce and a wrong one; a constructed sheet with `replaceSync`, `replace` and `insertRule`; `insertRule` into the linked sheet; `style.setProperty`; `setAttribute("style")`; a constructed sheet in a shadow root. Every `securitypolicyviolation` is recorded. |
| `frontend/` | A React page with one Radix modal Dialog, taller than the window. It opens the dialog, measures (opened, focus inside, `data-scroll-locked`, the body's computed `overflow`), closes it, measures again, and reports. Two builds into `frontend/obj/`: `dist` (Radix as published; `setNonce` from the meta tag when one is there) and `dist-cssom` (the helper replaced). |
| `tools/chromium.mjs`, `tools/webview2.sh`, `linux/run-all.sh` | Runners. They reuse `spikes/csp-navigation`'s hosts with this spike's folders served first. |
| `results/<engine>/` | The raw logs (Xvfb's EGL and Mesa warnings removed), and the WebKitGTK package version. |

**Change to `spikes/csp-navigation`:** its shared host code (`Csp.Shared/AppRoutes.cs`) now understands `{NONCE}` in a profile's policy: a fresh 16-byte nonce per response, also inserted into an HTML response as `<meta property="csp-nonce" nonce="…">`, the convention of Vite's `html.cspNonce`. The WebView2 host was republished (NativeAOT, 3,039,232 B, 0 warnings) and the WebKitGTK 6.0 host published in the container (NativeAOT, 2,882,704 B, 0 errors, 192 s with a cold package cache).

## How it was run

```sh
# from spikes/csp-style-injection
(cd frontend && npm install --ignore-scripts && node node_modules/vite/bin/vite.js build --outDir obj/dist --emptyOutDir \
  && node node_modules/vite/bin/vite.js build --mode cssom --outDir obj/dist-cssom --emptyOutDir)
export MSYS_NO_PATHCONV=1
node tools/chromium.mjs <label> <profile> <page> [dist]      # probes-strict strict /probes.html, radix-nonce nonce / dist, …
bash tools/webview2.sh <label> <profile> <page> [dist]
# WebKitGTK: a container from spar-test-host:webkit6 with the repository at /src (read-only), the logs at /out and HOME at
# /home/run (artifacts/linux-homes/csp-style-injection), Xvfb :99; then, as uid 1000:
bash /src/spikes/csp-style-injection/linux/run-all.sh
```

## Results

The same on all three engines (every cell observed on Chromium, WebView2 and WebKitGTK 6.0):

| Route | `strict` | `nonce` |
|---|---|---|
| `<style>` element, no nonce | blocked (`style-src-elem`) | blocked |
| `<style>` element, the page's nonce | blocked (no nonce in the policy) | **applied** |
| `<style>` element, a wrong nonce | blocked | blocked |
| constructed sheet, `replaceSync` | **applied** | applied |
| constructed sheet, `replace` | **applied** | applied |
| constructed sheet, `insertRule` | **applied** | applied |
| constructed sheet in a shadow root | **applied** | applied |
| `insertRule` into the linked stylesheet | applied | applied |
| `element.style.setProperty` | applied | applied |
| `setAttribute("style", …)` | blocked (`style-src-attr`) | blocked |
| The nonce from `<meta property="csp-nonce">` | — | readable as `meta.nonce`; `getAttribute("nonce")` is empty |

| Radix modal Dialog | opened, focus inside | scroll locked while open | released after closing | `<style>` elements | violations |
|---|---|---|---|---|---|
| as published, `strict` | yes | **no** (`overflow: visible`, though `data-scroll-locked` is set) | — | 1 (blocked) | 1 `style-src-elem` |
| as published, `nonce`, `setNonce` at start-up | yes | yes (`overflow: hidden`) | yes | 1 (applied) | 0 |
| helper replaced by constructed sheets, `strict` | yes | yes | yes | 0 | 0 |

## What this means for Spar (for the M1b page-security decisions; nothing decided here)

- **Neither route needs `'unsafe-inline'`.**
- **Constructed stylesheets need nothing from Spar:** the base policy is unchanged, and the style rules a script adds this way are as trusted as the script itself, which `script-src 'self'` and Trusted Types already limit. A `<style>` or `style` attribute brought in by injected markup stays blocked. The cost is on the library side: one that appends `<style>` elements needs a replacement module, as here, or its own option.
- **A nonce needs host support:** a fresh nonce per document in the policy, and a way to hand it to the page (here the `<meta property="csp-nonce">` convention, which the hosts would have to write into each HTML response, or the host's injected start-up script). It also covers libraries that already read a nonce (`get-nonce`, and CSS-in-JS libraries with a nonce option) with no patching. As the security design says, a nonce proves the host issued it, not that what uses it is safe: any script on the page can read it.
- **The Notebook's native `<dialog>`** remains the simplest choice for a sample: no library, no policy change, no host support.

## Not covered

- Dialogs and other Radix parts beyond the modal Dialog (Popover, Select, a modal DropdownMenu), and other libraries that inject styles.
- A nonce through the hosts' injected start-up script instead of a meta tag, and a nonce on loopback through the Vite dev server.
- A real Linux desktop (only Xvfb in a container), and WebKitGTK 4.1.
- Performance: adopting and removing a sheet per opening was not timed.
