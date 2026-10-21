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

The minimal app is under `samples/`. Detailed acceptance drivers use the instrumented app under
`fixtures/`; see the [migration map](23-minimal-application.md#scenario-migration-map). A fixture command
is not the recommended starting application. Each topic owns its commands; this index and README link to them.

## Status and deferred work

Status reconciled after [PR #37](https://github.com/sotsera/spar/pull/37), merged as `9025ee2` on 2026-10-21.
This section owns the current repository status; individual evidence records remain bound to their tested sources.

- Milestone 1 tracker #1 and implementation issues #2–#12 are closed. The [acceptance matrix](19-milestone-1-acceptance.md)
  records A01–A18 on the selected Windows developer baseline, including both IDEs with the instrumented consumer.
- Simplification [#30](https://github.com/sotsera/spar/issues/30), [#31](https://github.com/sotsera/spar/issues/31)
  and [#32](https://github.com/sotsera/spar/issues/32) are merged and closed: proportional CI, explicit test helpers,
  and the ordinary application/fixture separation. [#33](https://github.com/sotsera/spar/issues/33) is the current
  documentation cleanup. [#34](https://github.com/sotsera/spar/issues/34) is a subsequent consumer/coordinator
  design review; it does not authorize a broad implementation rewrite.
- The [minimal-app record](evidence/minimal-application-2026-10-21/README.md) covers actual source/package
  development, copied AOT and VS Code checks. Visual Studio was **not rerun for the new minimal app**.
  Earlier fixture IDE acceptance does not establish that new pass.
- Clean Windows/no-.NET-machine execution is unverified. Local packages are not a public NuGet release.
  Supported-machine/editor/WebView2 coverage, distribution/license review, installers/signing/updates and
  performance budgets remain release-readiness work. See the [original acceptance limits](19-milestone-1-acceptance.md#release-readiness-follow-up).
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
