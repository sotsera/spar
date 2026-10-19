# Structured development inspection

This implements the AI portion of M1.6 / A12 using the existing development coordinator and the actual native WebView. It requires a Debug host and explicit owner opt-in. The [acceptance evidence](evidence/development-inspection-2026-10-19/README.md) records executed scenarios and limits.

## Start and identify a target

Build the solution and install the sample frontend dependencies as described in the [development guide](11-development-session.md). Launch the CLI with redirected stdin/stdout:

```powershell
./tools/Spar.Dev/bin/Debug/net10.0-windows/Spar.Dev.exe samples/Spar.NativeSample/spar.dev.json --owner-stdin --inspect
```

Starting this process starts one owned session. Keep stdin open; owner EOF ends the session and cleans up its children. Each stdout line is a JSON event. Wait for `application-ready`, which includes `session`, `generation`, `processId`, `processStartedAtUtc`, `document`, and `contract`. A process start or listening port alone is not readiness. `--probe` is unnecessary: inspection works without the sample's fixed test actions.

Send one JSON command per line. Copy the identity from the latest readiness event; the following placeholders must be replaced:

```json
{"command":"status","id":"status-1"}
{"command":"inspect","id":"snapshot-1","detail":"snapshot","session":"SESSION","generation":1,"document":"DOCUMENT","contract":"CONTRACT"}
{"command":"inspect","id":"type-1","detail":"type","selector":"input","text":"Keep this text","session":"SESSION","generation":1,"document":"DOCUMENT","contract":"CONTRACT"}
{"command":"inspect","id":"click-1","detail":"click","selector":"#backend","session":"SESSION","generation":1,"document":"DOCUMENT","contract":"CONTRACT","timeoutMilliseconds":5000}
{"command":"inspect","id":"image-1","detail":"screenshot","session":"SESSION","generation":1,"document":"DOCUMENT","contract":"CONTRACT"}
{"command":"inspect","id":"logs-1","detail":"logs","session":"SESSION","generation":1,"document":"DOCUMENT","contract":"CONTRACT"}
{"command":"diagnostics","id":"diagnostics-1"}
{"command":"restart","id":"restart-1"}
{"command":"stop","id":"stop-1"}
```

Inspection IDs must be unique ASCII letters/digits/`-`/`_`, 1–64 characters. At most 1,024 inspection requests are admitted per session. Reusing an admitted ID returns `duplicate_id`; it never replays the action. Requests for another session, generation, contract, or document return `stale_target`. A reload retires the document even when the backend process and generation remain unchanged. Native execution also checks document identity and navigation source before dispatch and completion; late results from retired documents are discarded.

## Acceptance, completion, and timeout

`command-accepted` means the coordinator admitted the operation. `command-completed` carries its correlation ID and outcome. Inspect completion's `detail` contains a JSON string with `{ "code": "ok", "result": ... }`; parse that string to obtain the DOM/log data or screenshot path. Status and successful restart completion include the current application identity. Restart completes only after the replacement reaches readiness; a failed rebuild reports `build_failed` and leaves the application stopped. Stop completes through `session-stopped` after actual cleanup, with the same correlation ID. Forced cleanup is labeled separately.

Click/type completion means the DOM event dispatch returned. It does **not** mean an asynchronous backend operation initiated by that event finished. Observe a subsequent snapshot or application-specific result before claiming its outcome. Mutations are never automatically retried.

Inspection accepts timeouts from 50 to 30,000 ms (default 5,000). A `command-timeout` with `unfinished` means the operation may still execute. Its slot remains occupied; another inspection receives `inspection_busy`. Actual late completion is still emitted. Native callbacks and COM objects remain alive until completion. If the application exits first, `process_ended` reports an unknown action outcome. A failed delivery likewise does not establish non-execution. A hung renderer can therefore block inspection and graceful retirement; the existing explicit forced Stop ends the session, not a replacement generation.

## Available inspection

- `snapshot`: bounded top-document DOM text and up to 32 elements with selectors, tags, explicit roles, text/value, and disabled state. Traversal visits at most 1,000 nodes and collects at most 2,500 text characters; `truncated` identifies limits. This is a DOM snapshot, not a full accessibility tree. Password values are redacted. Frames and shadow roots are outside this initial surface.
- `screenshot`: actual WebView viewport PNG using a fixed native protocol operation, at most 4 MiB. The application writes a generated filename beneath its profile's `.inspection` sibling directory and returns the path. The caller cannot choose an output path. This captures WebView content, not desktop chrome or other applications.
- `click`: one uniquely matched, visible, enabled, non-inert HTML element. This invokes DOM `click()`; it is not an OS mouse event or a trusted browser gesture.
- `type`: replaces a text/search/email/url/tel input or textarea value and dispatches input/change events. It rejects read-only fields and unsupported controls. Selectors are at most 512 characters; input text is at most 1,024 characters. This is not keyboard emulation, file upload, or password entry.
- `logs`: the latest 12 frontend console/error events, each at most 400 characters, with a dropped-event count. `observedDocument` labels the document current when the host received the event; it is not a claim that an asynchronous error originated in that document. Truncated protocol details are strings, not necessarily complete JSON objects.
- `diagnostics`: up to eight fixed coordinator-owned log tails of 2 KiB each, followed by correlated completion. These include backend/compiler output. Neither arbitrary log paths nor arbitrary filesystem access are accepted.

Commands and private control frames retain the existing 16 KiB limit. Nested inspection results are capped before transport; oversized data returns `result_too_large` rather than killing the session. The owner command queue is bounded; rejection is structured. Application content and diagnostics remain untrusted data. Public event details redact the coordinator's private credentials and scoped HTTP path. Screenshots and application text can still contain the developer's own data, so inspect evidence before sharing it.

## Boundary and implementation

The owner uses the existing stdin/stdout channel and authenticated application pipe. No remote-debugging port, new renderer API, arbitrary JavaScript evaluation, protocol-method selection, shell command, or filesystem-path operation is exposed. Fixed native WebView operations and an embedded fixed script implement the allowlist; ordinary bridge permissions remain unchanged. Runtime logging uses fixed `Runtime.consoleAPICalled` / `Runtime.exceptionThrown` subscriptions. Screenshot capture uses fixed `Page.captureScreenshot` parameters through WebView2's native API.

The host option, inspection API, callback projections, and embedded script are compiled only in Debug. `eng/Test-InspectionBoundary.ps1` examines the Release host's metadata and resources to establish their absence. The development coordinator remains a separate tooling dependency; the copied NativeAOT payload is also checked. Full packaged production-SPA acceptance remains issue #10. An MCP adapter and optional process-memory sampling are follow-ups, not implemented here. Existing editor adapters do not yet expose an inspection toggle.

The callback ABI projections follow the pinned Microsoft.Web.WebView2 `1.0.4258.31` header, with the existing [provenance and license](09-native-host.md). Operation semantics are documented by [WebView2](https://learn.microsoft.com/en-us/microsoft-edge/webview2/reference/win32/icorewebview2) and [Page.captureScreenshot](https://chromedevtools.github.io/devtools-protocol/tot/Page/#method-captureScreenshot).

## Repeat the acceptance run

```powershell
dotnet build Spar.slnx --disable-build-servers
dotnet test --solution Spar.slnx --no-build
./eng/Test-DevelopmentInspection.ps1 -EvidenceDirectory artifacts/inspection/probe
dotnet build src/Sotsera.Spar.Windows -c Release --disable-build-servers
./eng/Test-InspectionBoundary.ps1
```

The test edits isolated fixtures, drives generic inspection without `--probe`, verifies React/backend changes and retained state, captures frontend and compiler failures/recovery, tests unfinished timeout without replay, rejects stale targets after reload/restart, and observes actual owned-process cleanup while an unrelated process survives. It also verifies Stop during unfinished native work, default-disabled admission, and correlated failed-rebuild completion. Commands, public events, snapshots, logs, and PNGs remain beneath the evidence directory on success or failure. PNG capture alone does not establish visual correctness; review both edited/restarted images. The required CI job executes this same native scenario and retains its artifacts.
