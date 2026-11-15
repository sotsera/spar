# Development loop

Part of the Spar design. [`docs/plan.md`](../plan.md) has the goal, the settled decisions, the open questions and the milestones, and lists every design file. Section numbers are global: "section 4" anywhere in the plan, the design files or `docs/decisions.md` means the heading of that number, wherever it lives.

## 4. Development loop

- **Debug embeds no bundle.** The app, desktop or loopback, starts or reuses the Vite dev server and serves the page from it. The desktop bridge stays in-process.
  - Vite records itself in `obj/spar-dev-server.json`. It is reused across backend restarts, ends with its launcher's standard input, and has a 60 s idle watchdog.
  - Measured: Fast Refresh inside WebView2 in 39 ms; a contract change restarts in about 5.4–6.1 s with Vite reused.
- **Node at run time in Debug is accepted** (user decision).
  - Without Node, the app stops with a clear error saying how to install it, or how to use the Release bundle; `spar doctor` checks the Node version.
  - `-p:SparDevServer=false` makes Debug embed the bundle through the same code as Release. It has no hot reload, and shows startup, theme, CSP and headers exactly as users will see them.
  - **Debug can keep `https://app.localhost`** (verified by `spikes/desktop-origin`). The host fetches each request from Vite in-process, so Debug and Release share storage, CSP and secure-context behaviour.
    - Medians, direct against proxied: Fast Refresh 25 and 23 ms, state kept 5 of 5 times, CSS 5 and 9 ms, error overlay 8 and 9 ms; a full page load costs about 35 ms more (117 against 153 ms).
    - Vite's `server.hmr` must point the client at `ws://127.0.0.1:<port>`. With the defaults it tries `wss://app.localhost`, which leaks and gets no updates. The Debug CSP adds that WebSocket to `connect-src`.
    - **Decided (user, 2026-11-14): this is the Debug default.** Serving straight from Vite stays an option. Still to check in M1: Vite's nonce under the strict CSP through the proxy, and the Linux path through the custom scheme.
    - **What proxies:** the host itself, with no proxy server or extra package. On Windows a `WebResourceRequested` filter for `https://app.localhost/*` takes each request, the host fetches it from Vite's loopback URL with `HttpClient` and answers with `CreateWebResourceResponse` through a deferral. On Linux the `spar://app` scheme handler does the same and answers with a `WebKitURISchemeResponse`. The HMR WebSocket is not proxied: the page connects to `ws://127.0.0.1:<port>` directly. All of it is Development-variant code, absent from Production.
    - **Proxy requirements** (`spikes/desktop-origin/Origin.Win/Host.cs`): forward `Accept` and the `Sec-Fetch-*` headers, pass `Content-Type` through, never cache; complete each request through a deferral on the STA, so a slow Vite holds the request but not the pump; `HttpClient` with `UseProxy=false` and no automatic decompression. Each proxied request took 0.5–15.5 ms.
    - **For HTML, drop `If-None-Match` and `ETag`.** The spike's proxy forwards `If-None-Match` to Vite. `spikes/csp-navigation` showed that a 304 then keeps a page whose nonce no longer matches the new header (2 violations, page stuck), so that proxy code as written would bring the bug back.
- **Hot Reload signal (decided, user, 2026-11-14), Debug only:**
  - After each C# edit that Hot Reload applies, the host's `MetadataUpdateHandler` sends a `backend-updated` event to the open pages. It carries a revision number, the time and the names of the updated types.
  - The SDK offers `onBackendUpdate(handler)`, for example to refetch the current view's data. In Release it is a no-op that the bundler removes.
  - **A toast by default:** Spar's development overlay shows a small, dismissable toast for each update, such as "Backend updated: CopyJobsService (revision 3)". An app can turn it off or show its own from `onBackendUpdate`.
  - **Limits:**
    - The event can arrive after the patched code already ran once.
    - Rejected or failed edits are known only to `dotnet watch`, so they stay in its terminal.
    - Contract and DTO edits still restart the app, as before.
- **Release and test variants embed the bundle.** Each variant has its own `obj`/`bin`.
- **Launch profiles:** `Desktop` (the default, first in `launchSettings.json`; user decision) and `Loopback`. If the desktop cannot start (no display), the app stops with a clear error suggesting `dotnet run -lp Loopback` and never falls back on its own; `spar doctor` reports the same. The loopback dev session keeps one port and token across restarts. An orchestrator such as Aspire may assign the port, token and dev-server URL through the environment.
- **`dotnet watch`** does not mark its builds, so the build never tries to detect it. The runtime decision above makes detection unnecessary.
- **Agents:** after external edits, the CLI build or `spar dev` is the loop. Start real processes directly, so their whole tree can be stopped.
