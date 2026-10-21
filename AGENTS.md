# Working on Spar

Read [product direction](docs/02-product-direction.md), then the relevant current guide in the
[documentation map](docs/README.md). Follow [contributor policy](docs/24-contributing.md) for validation,
C#/MSBuild style, test/dependency versions, GitHub identity, branches and PRs. Those linked rules apply
to work in this repository; this entry point does not replace them. Milestone plans and evidence are historical.

## Essential constraints

- Keep Spar, `Sotsera.Spar`, repository identity and the Windows-only scope. Previous Spar is reference evidence,
  not a source/build dependency; do not inherit its toolchain or completion claims.
- Prioritize framework-native SPA HMR, supported backend Hot Reload and coordinated restart, usable by editors
  and AI tools. Prioritize VS Code while preserving Visual Studio. One mechanism owns backend Hot Reload per session.
- Use CoreCLR for development and NativeAOT by default for production. Dependency-driven CoreCLR selection
  needs reproducible attribution; unrelated build failures and framework AOT regressions remain fatal.
- Use the smallest integrated change, ordinary .NET projects and public APIs. Keep the SPA framework-neutral
  and build, development and inspection tooling outside the production runtime.
- Preserve explicit permissions, native document authority, contract matching, bounded work and native ownership.
  Cancellation or timeout does not establish execution or cleanup completion. Never replay backend mutations automatically.
- Preserve unrelated working-tree changes. Do not incidentally alter sibling checkouts or remotes. Do not discard
  unfinished branches. Never recursively delete fixture junctions or rebuild shared tools while suites own processes.
- Follow the [validation map](docs/24-contributing.md#prepare-and-choose-checks). Actual native/editor/AOT behavior
  needs actual integration evidence. Record skipped/unavailable checks honestly; historical results do not verify a new change.
- Use `sotsera/spar` for issues and PR targets, `ghidello/spar` for development branches. Identify the target and
  run `gh auth status` before GitHub writes; ask before switching an incorrect account. Always request approval
  before publishing issues. Update existing issue checklists/evidence with implementation PRs and reconcile after merge.
- Start focused branches from latest upstream `main`; preserve changes, sync fork/local `main` after squash merge
  and retire finished branches. Never push directly to upstream `main` or bypass its required check.
- Follow the [PR and squash-message rules](docs/24-contributing.md#pull-requests-and-upstream-history).
  Use qualified `Refs` for partial work and `Fixes` only for completed scope.

Focused skills live in `.agents/skills/`; use the [skill guide](docs/20-agent-skills.md) for selection and provenance.
For upstream ideas or reuse, use [reference projects and licenses](docs/01-reference-projects.md).
Distinguish confirmed requirements, proposals and unresolved decisions.
