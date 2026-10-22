# Development startup and diagnostics review

Reviewed on 2026-10-21 against merged PR #42, `e908e1a0aa5cbff9611a8848995ebc5d781055e0`.
C1, C2 and K1 are complete. This review scopes the next development-experience work; it implements no
runtime or editor change and publishes no new issue. Requirements come from the
[product direction](02-product-direction.md); commands remain in the current
[application](23-minimal-application.md), [terminal](11-development-session.md) and editor guides.

## Findings and recommended sequence

| Scope | Observed rough edge | Evidence | Proposed delivery |
| --- | --- | --- | --- |
| D1: predictable preflight failures | Missing/malformed configuration and invalid limits terminate with an unhandled exception and exit `-532462766`; no session event is produced. An unknown CLI option is silently accepted and startup proceeds. | Six isolated CLI cases in the [record](evidence/development-experience-review-2026-10-21/README.md); [CLI entry](../tools/Spar.Dev/Program.cs). | Validate the complete argument list before starting work. Return a stable nonzero result and a concise actionable error for expected input/filesystem failures. Cover missing option values, unknown switches, malformed configuration and unwritable/existing event-file destinations. Preserve the private project-owner arguments. |
| D2: failure points directly to its logs | An intentionally failing frontend writes a distinctive error to its stderr file and exits 23. The public failure only says `frontend_start_failed` and “inspect its stdout/stderr logs”; it omits the exit code and exact files. | Both frontend CLI cases; [startup wait](../src/Sotsera.Spar.Development/DevelopmentSession.cs) and [settings](../src/Sotsera.Spar.Development/DevelopmentSettings.cs). | Identify failed role, actual exit code when known, and exact retained stdout/stderr paths in the failure presentation. Give a next action appropriate to that phase. Preserve structured identity and cleanup reporting. |
| D3: truthful progress and withdrawn readiness | The terminal emits JSON records; VS Code shows `starting` through build/frontend/debugger setup. Its status handler updates on readiness/patch/restart but does not handle `application-not-ready` or a new `bridge-connected` that withdraws readiness, so it can retain `ready`/`updated` while admission is withdrawn. | Source trace through [session messages](../src/Sotsera.Spar.Development/DevelopmentSession.cs), [VS Code adapter](../tools/Spar.VSCode/extension.cjs), and [task matcher](../.vscode/tasks.json). No new desktop failure walkthrough was executed. | Present actual build/frontend/debugger/readiness phases, and clear the ready display when the coordinator withdraws readiness. Keep the existing coordinator as authority; add only the observations needed by presenters. Provide terminal-readable progress alongside the structured transport. |

Start with **D1**, then **D2**, then **D3**, in separate focused PRs. D1 removes the earliest opaque failure
without changing process sequencing. D2 makes the existing logs discoverable before introducing additional
presentation. D3 then consumes the same public facts across terminal and editor lanes.

The CLI reproductions verify diagnostic behavior only. Source inspection establishes D3's missing event
handling, not an executed IDE reproduction or a measured frequency. Existing native ownership, typed
readiness and debugger acceptance records remain bound to their original revisions.

## Boundaries and decisions

- Expected startup failures must not look like successful sessions. A failure before session creation has no
  session/generation identity; do not fabricate one. D1 should settle a small startup-error representation
  for automation while keeping public JSON events and private debugger handoffs separate.
- Human output must not corrupt the JSON owner channel. Choose a separate stream or explicit presentation
  mode; verify terminal, VS Code and Visual Studio consumers together. Unknown switches must fail before
  configuration loading, session-directory creation or child launch.
- D2 should link logs and identify exit status by default. A raw log tail can contain application secrets or
  private launch data; publishing arbitrary child output is not the default solution. Any preview needs an
  explicit bound and the existing session-capability redaction boundary.
- VS Code currently reveals its output for `session-failed` and process-spawn errors. Preflight stderr is
  appended, but a subsequent nonzero process `close` merely hides status. D1 must surface that failure even
  when no `session-failed` event exists; verify it in the actual editor before claiming acceptance.
- Readiness comes from the native document and matching bridge. Progress is not readiness; a debugger
  process, listening frontend, visible window or completed click cannot substitute for it. Stop/force and
  retirement semantics remain unchanged, and backend mutations are never replayed.

## Verification for implementation

| Scope | Required evidence |
| --- | --- |
| D1 | Process-level CLI cases for missing/malformed config, invalid limits/options, absent values and event-file failures. Verify stable exit, bounded useful messages, no unintended child/session creation and no raw stack trace for expected errors. Preserve no-argument usage and both private editor launch lanes. Actual VS Code/Visual Studio startup-failure presentation needs editor evidence. |
| D2 | Real child startup failure and timeout, selected build failure, frontend failure after readiness, invalid executable and retained cleanup. Verify exact log paths, actual exit code where available, bounded/redacted public output, and no release-boundary change. Use native/editor checks for the paths changed. |
| D3 | Real delayed build/frontend startup, breakpoint/Continue, readiness withdrawal/recovery and failed rebuild/correction. Review actual terminal/editor status and native captures. Re-run owner-loss/force/retirement checks if the event path changes; phase text cannot delay control processing. |

Use the [contributor validation map](24-contributing.md#prepare-and-choose-checks). Add tests for the
observable failure/transition, rather than assertions that mirror a formatter or new class shape.

## Later polish and deferred work

Early native waiting/error-window feedback is an existing product proposal. It would add another document
and startup lifetime to verify, so it should follow useful preflight errors, discoverable logs and truthful
phase reporting. Do not move the window earlier merely to hide an otherwise silent build.

No general lifecycle framework, additional Hot Reload owner, automatic rude-edit restart, arbitrary renderer
evaluation, MCP adapter, installer or dependency upgrade is needed for D1–D3. Clean-machine qualification
remains separate release-readiness work. These proposed scopes become implementation work after maintainer
selection; merging this review does not establish their acceptance checks.

## Selected implementation

The maintainer selected D1 after merging this review in PR #43 (`4e82ce4`). D1 adds strict complete CLI
argument validation, bounded public startup errors and exit `1`, expected preflight filesystem handling,
VS Code Output visibility and project-launch error propagation. Its
[execution record](evidence/startup-errors-2026-10-21/README.md) records the current implementation and
actual editor/package checks. Implementation acceptance belongs to that record and its PR; the original
review findings above remain observations of `e908e1a`.

After PR #47, the maintainer selected D2 followed by D3 in separate focused changes. D2 adds
process role, observed exit status, retained log locations and a next action to failure events. Its
[execution record](evidence/failure-diagnostics-2026-10-22/README.md) separates native, editor and boundary checks.
D3 follows with truthful progress and withdrawn-readiness presentation. Selection authorizes
implementation; current implementation PRs and their revision-bound checks establish acceptance.
