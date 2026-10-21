# Integration helper extraction validation

Local Windows validation for [issue #31](https://github.com/sotsera/spar/issues/31), based on
`f316fd9`. [The record](validation.json) binds these observations to SHA-256 hashes of the
validated implementation files, retained receipts and visually reviewed captures. Raw output
remains in the recorded local artifact/temporary directories; it is not copied into Git.

The [helper guide](../../22-integration-test-helpers.md) maps the six migrated drivers and
their explicit inputs/ownership. No runtime or dependency implementation changed. A comparison
against the baseline retained all 141 existing assertion and wait call sites.

| Check | Observed result |
| --- | --- |
| Solution build | Passed, zero warnings/errors |
| Managed tests | 65 passed, zero failed/skipped |
| CI selector/gate tests | 32 passed |
| Helper checks | Definition-only imports, independent scenarios, obsolete `-LibraryOnly` rejection before setup, forced process exit, unrelated survival and live-handle retention after timeout passed |
| SPA session, `-VerifyRender` | All 10 scenarios passed on the final driver; before/after captures reviewed |
| Backend updates, `-VerifyRender` | All 3 scenarios passed on the final driver; patched/recovered captures reviewed |
| Project launch, `-VerifyRender` | All 4 scenarios passed; each readiness capture reviewed (identical image hashes) |
| Native inspection | All 3 scenarios passed; edited/restarted WebView PNGs reviewed |
| VS Code debugger, `-VerifyRender` | Source fixture and a separate fresh package consumer passed; 45 retained processes exited in each run; both captures reviewed |
| Package consumer, `-VerifyRender` | Fresh build, HMR/restart, production boundary, copied AOT/CoreCLR execution and intentional strict-AOT dependency rejection passed; development and both production captures reviewed |

Runs were sequential so shared tool outputs were not rebuilt while native sessions owned
processes. The final SPA/backend runs followed the addition of strict parameter binding.
The package debugger used a second fresh consumer rather than the delivery fixture after its
intentional dependency/source modifications. See the record for the actual SDK/editor versions.

The first SPA attempt failed native readiness in the abrupt-owner scenario with
`application_ready_timeout`; its logs observed `owned-processes-ended`. The failed fixture and
log are retained. An unchanged fresh retry passed, as did the final driver. The cause was not
established and no runtime fix is claimed. The earlier project-launch cleanup race is also not
claimed fixed by this extraction.

Hosted CI had not run at the time of this local record. Actual Visual Studio walkthroughs and
clean-machine qualification were not rerun. Successful automated project launch does not
establish IDE debugger acceptance; the two executed VS Code runs provide their own evidence.
