# Development experience review evidence — 2026-10-21

This is diagnostic-review evidence for [the proposed D1–D3 scopes](../../26-development-experience-review.md),
against merged PR #42, `e908e1a0aa5cbff9611a8848995ebc5d781055e0`. It is not an implementation or new native/editor
acceptance pass. Existing local coordinator binaries were used; [the receipt](receipt.json) records their
hashes and the reviewed source hashes. No code changed, build ran, dependency changed or IDE was operated.

## Executed CLI cases

| Case | Observed result |
| --- | --- |
| No arguments | Usage on stderr, exit 1, no stdout. |
| Missing configuration | Unhandled `FileNotFoundException`, exit `-532462766`, no stdout/session event. |
| Malformed JSON | Unhandled `JsonException`, exit `-532462766`, no stdout/session event. |
| Startup limit 0 | Unhandled `InvalidDataException`, exit `-532462766`, no stdout/session event. |
| Frontend exits 23 | CLI exits 1; six JSON events include `frontend_start_failed`, generic log advice and `session-stopped/graceful`. The marker is in the retained frontend stderr file, absent from public output. |
| Unknown switch | `--review-unknown-option` is accepted. The same deliberately failing frontend starts; the CLI emits the same failure type rather than rejecting the option. |

The review supervisor waited for each actual coordinator process exit and retained its stdout/stderr.
No native application or debugger launched. Child-exit/cleanup events were observed, but descendant handles
were not independently retained; these cases do not replace the K1 lifetime acceptance evidence.

## Reproduction

From the checkout root with an already built development tool:

```powershell
$tool = './tools/Spar.Dev/bin/Debug/net10.0-windows/Spar.Dev.exe'
& $tool
& $tool '<nonexistent-config.json>'
# Save a file containing only {, then pass its path to reproduce malformed JSON.
# Set startupTimeoutSeconds to 0 in an otherwise valid copied config to reproduce invalid limits.
```

The frontend/unknown-option probes used a fresh directory under `artifacts/development-experience-review`.
Their configuration used absolute paths to the built ordinary sample, project and generated client;
`watchBackend` and `debuggerBackend` were omitted (false), timeout 5 seconds, shutdown timeout 2 seconds,
and the directory itself as the frontend working directory. The frontend command was:

```powershell
pwsh -NoProfile -Command "[Console]::Error.WriteLine('SPAR_REVIEW_FRONTEND_FAILURE'); exit 23"
```

Run the copied configuration with `--no-build`, then with `--no-build --review-unknown-option`.
Both stop before application launch. Retained `frontend-1.stderr.log` contains the marker; the public
`session-failed` record contains only:

```json
{"code":"frontend_start_failed","detail":"The process exited before readiness; inspect its stdout/stderr logs."}
```

This excerpt intentionally omits session/process/time fields. Raw logs, fixture configurations and the
supervisor script remain in ignored `artifacts/`. No raw private debugger stream or browser profile is retained.
Expected failing commands above are observations of the reviewed behavior, not passing implementation tests.
