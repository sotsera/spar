# Spar documentation

Use the current guides below for commands and requirements. Numbered filenames are stable links,
not a reading order. Historical records remain at their existing paths.

## Current guidance

| Task | Authoritative home |
| --- | --- |
| Run an app; understand its source and prerequisites | [Minimal application](23-minimal-application.md) |
| Start outside this checkout with local packages | [Package consumer](18-package-consumer.md) |
| Terminal/AI development, ownership and restart | [Session configuration](11-development-session.md), [backend updates](12-backend-updates.md) |
| Debug in an editor | [VS Code](13-vscode-development.md), [Visual Studio](14-visual-studio-development.md) |
| Inspect the actual WebView | [Development inspection](15-development-inspection.md) |
| Define contracts, permissions and native lifetime | [Typed bridge](10-typed-bridge.md), [Windows host](09-native-host.md) |
| Publish and choose an attributable compatibility mode | [Production delivery](16-production-delivery.md), [dependency compatibility](17-dependency-compatibility.md) |
| Contribute, validate, manage dependencies and submit a PR | [Contributor policy](24-contributing.md) |
| Select checks and retain evidence | [CI and evidence policy](21-continuous-integration.md) |
| Work on test helpers or agent skills | [Integration helpers](22-integration-test-helpers.md), [repository skills](20-agent-skills.md) |
| Understand product constraints or upstream provenance | [Product direction](02-product-direction.md), [reference/license review](01-reference-projects.md) |

Accepted review: [consumer setup and coordinator review](25-consumer-coordinator-review.md).
C1 trims redundant scaffold settings. C2 supplies the optional development startup helper;
its [acceptance record](evidence/development-startup-2026-10-21/README.md) includes passing source/package
VS Code and actual Visual Studio walkthroughs. K1 adds one internal watcher resource owner while retaining
restart/build/admission decisions in the coordinator; its
[verification record](evidence/watcher-owner-2026-10-21/README.md) is separate.

The accepted [development startup and diagnostics review](26-development-experience-review.md), merged
in PR #43, records reproduced preflight/frontend failures and source-based presentation gaps. The
maintainer selected D1 startup errors; its [execution record](evidence/startup-errors-2026-10-21/README.md)
tracks implementation validation. The maintainer subsequently selected D2 log discovery followed by
D3 phase/readiness presentation in separate changes. D2 merged in [PR #48](https://github.com/sotsera/spar/pull/48)
with [failure-diagnostic evidence](evidence/failure-diagnostics-2026-10-22/README.md). D3 merged in
[PR #49](https://github.com/sotsera/spar/pull/49), with [terminal/native and VS Code evidence](evidence/progress-readiness-2026-10-22/README.md).
Implementation PRs and exact-run results establish acceptance without changing milestone 1's historical requirements.

The [CI reliability review](27-ci-reliability-review.md) traces the later D1 run's timeout to slow
repeated npm installs and records the advisory-network/timing repair. The current
[test suite policy](21-continuous-integration.md) replaces path classification with five fixed
automatic suites on isolated Windows jobs behind the existing required gate. Editor and reviewed
desktop acceptance remain explicit. Precursor probes and repeated scenarios retain their stated scope.

The minimal app is under `samples/`. Detailed acceptance drivers use the instrumented app under
`fixtures/`; see the [migration map](23-minimal-application.md#scenario-migration-map). A fixture command
is not the recommended starting application. Each topic owns its commands; this index and README link to them.

## Status and deferred work

Status reconciled after [PR #50](https://github.com/sotsera/spar/pull/50), merged as `9277267` on 2026-10-22.
This section owns the current repository status; individual evidence records remain bound to their tested sources.

- Milestone 1 tracker #1 and implementation issues #2–#12 are closed. The [acceptance matrix](19-milestone-1-acceptance.md)
  records A01–A18 on the selected Windows developer baseline, including both IDEs with the instrumented consumer.
- Simplification [#30](https://github.com/sotsera/spar/issues/30), [#31](https://github.com/sotsera/spar/issues/31),
  [#32](https://github.com/sotsera/spar/issues/32) and [#33](https://github.com/sotsera/spar/issues/33) are merged
  and closed: proportional CI, explicit test helpers, ordinary application/fixture separation and current guidance.
  [#34](https://github.com/sotsera/spar/issues/34) is also closed after acceptance of the
  [consumer/coordinator review](25-consumer-coordinator-review.md). Implementation is separately scoped:
  C1's scaffold defaults are merged in PR #40; C2's optional startup helper and its completed local acceptance
  are merged in PR #41. K1's watcher resource ownership is merged in PR #42. The agreed C1/C2/K1 sequence is complete.
- The [minimal-app record](evidence/minimal-application-2026-10-21/README.md) covers actual source/package
  development, copied AOT and VS Code checks. The later
  [startup-helper record](evidence/development-startup-2026-10-21/README.md) adds actual source/package
  minimal-app Visual Studio walkthroughs.
  Earlier fixture IDE acceptance does not establish that new pass.
- Clean Windows/no-.NET-machine execution is unverified. Local packages are not a public NuGet release.
  Supported-machine/editor/WebView2 coverage, distribution/license review, installers/signing/updates and
  performance budgets remain release-readiness work. See the [original acceptance limits](19-milestone-1-acceptance.md#release-readiness-follow-up).
- D1 startup errors and the focused npm-network/timing repair are merged in PR #45. Its full required
  [repair run](https://github.com/sotsera/spar/actions/runs/39743204775) passed before merge. Fixed suites
  merged in PR #46; PR #47 skips automatic fork push jobs. Its [upstream main run](https://github.com/sotsera/spar/actions/runs/39810559292)
  passed, and the [fork sync run](https://github.com/ghidello/spar/actions/runs/39812901885) skipped both jobs.
  D2 log/exit discovery merged in PR #48 (`20dc5a5`); D3 phase/readiness presentation and the startup-observer
  regression repair merged in PR #49 (`0f65de5`). The agreed D1–D3 sequence is complete. D3's
  [final PR run](https://github.com/sotsera/spar/actions/runs/39835883681) passed all five suites and the required
  aggregate: 49 catalog checks and 75 managed cases, none skipped. Its
  [integrated-main run](https://github.com/sotsera/spar/actions/runs/39838538030) also passed all six checks.
- After D3, the maintainer selected startup/save-to-change measurement. The
  [baseline](evidence/development-latency-2026-10-22/README.md) retains five real terminal sessions,
  individual samples and actual cleanup. PR #50 merged as `9277267`; its
  [integrated-main run](https://github.com/sotsera/spar/actions/runs/39842709784) passed all six checks.
  The [full-build/watcher investigation](evidence/watcher-startup-cost-2026-10-22/README.md) records the
  cold consumer restore, rejected framework hint and controlled incremental-generation comparison.
  Current implementation qualification remains revision-bound; there is no automatic timing budget.
- The [product direction](02-product-direction.md#deferred-scope) defines deferred features; MCP and optional
  memory observation are proposals, not shipped capabilities or prerequisites retroactively added to milestone 1.

CI is revision-specific: use the [PR/main run results](https://github.com/sotsera/spar/actions/workflows/managed-probe.yml).
A merged issue or this status page does not assert that every later commit passed. Retain a first failure
even when a rerun succeeds; follow the [evidence policy](21-continuous-integration.md#evidence-retention-policy).

## History and research

The [milestone plan](03-milestone-1.md), [original issue specifications](04-milestone-1-issues.md),
[kickoff](05-implementation-kickoff.md), [backend research](06-backend-reload-research.md),
[managed precursor experiment](07-managed-probe.md), [merge-gate proof](08-ci-merge-gate.md)
and [milestone acceptance](19-milestone-1-acceptance.md) explain decisions and past execution.
Their dates, old paths and unexecuted-at-the-time statements describe those revisions, not current setup.
[Historical evidence](evidence/README.md) stays linked in place, including source/license provenance and first failures.
