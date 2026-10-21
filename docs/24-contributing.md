# Contributing to Spar

Start with the [documentation map](README.md) and [product direction](02-product-direction.md).
This is the authoritative contributor workflow, coding/dependency policy and validation entry point.
[AGENTS.md](../AGENTS.md) supplies the short agent entry point; topic guides own their integration procedures.

## Prepare and choose checks

Confirm the working tree and preserve unrelated changes. Use the selected prerequisites in the
[minimal application guide](23-minimal-application.md#prerequisites); do not inherit tools or acceptance claims
from the previous Spar checkout. For an implementation change, build before tests:

```powershell
dotnet build Spar.slnx --disable-build-servers
dotnet test --solution Spar.slnx --no-build
```

The tests start real watchers and edit isolated fixtures. They do not establish native rendering,
debugger Hot Reload or published AOT behavior. Choose additional checks by the behavior changed:

| Change | Authoritative procedure and evidence requirement |
| --- | --- |
| Documentation or skill guidance | Run `node eng/ci/Test-Documentation.mjs`, `node eng/Test-AgentSkills.mjs` and `node --test eng/ci/selection.test.mjs`. Inspect changed commands against the named script/configuration; the link check does not validate remote URLs, anchors or executable examples. No unrelated integration run is required. |
| Native lifetime, navigation or bridge | [Native host](09-native-host.md) and [typed bridge](10-typed-bridge.md): actual CoreCLR and copied AOT drivers, plus `-VerifyRender` and image review where rendering changes. Build before TypeScript checks. |
| SPA, watcher or restart ownership | [SPA sessions](11-development-session.md), [backend updates](12-backend-updates.md) and [helper ownership](22-integration-test-helpers.md): real processes, retained state and actual retirement/cleanup. The task-only VS Code checks do not prove debugger ownership. |
| VS Code debugger or project launch | [VS Code](13-vscode-development.md) and [Visual Studio](14-visual-studio-development.md): actual editor acceptance. `Test-ProjectLaunch.ps1` proves lifecycle only; it cannot replace a Visual Studio walkthrough. |
| Inspection | [Inspection](15-development-inspection.md): actual WebView operations and PNG review; Release metadata/resource exclusion after building Release. Click completion does not prove asynchronous backend completion. |
| Assets, publication or compatibility | [Production](16-production-delivery.md) and [compatibility](17-dependency-compatibility.md): production boundary before a separate explicit AOT probe build; copied payload execution and capture review. Preserve attributable compatibility and fatal unrelated failures. |
| Ordinary sample or package consumer | [Minimal app](23-minimal-application.md#verification) and [packages](18-package-consumer.md): fresh consumer outside the checkout, isolated feed/cache and public APIs. Recheck affected source/package editor lanes separately. |
| Shared test helpers | [Helper checks and standalone drivers](22-integration-test-helpers.md#driver-map-and-commands). Preserve caller-owned handles and unfinished work. |

Run native suites sequentially. Do not rebuild or pack shared tools while a suite owns running
processes. Never recursively delete fixture junctions as routine cleanup. Keep build-only generator
references private and tooling out of the production payload. Preserve one backend Hot Reload owner,
immutable contract assemblies, and actual retirement before replacement; do not enable SDK auto-restart.
Keep private launch capabilities out of public logs. A timeout or cancellation never establishes cleanup.

Record observed results and unavailable checks using the [evidence retention policy](21-continuous-integration.md#evidence-retention-policy).
Use the [CI coverage policy](21-continuous-integration.md) to understand hosted selection; hosted checks
do not replace desktop/editor evidence. The [status page](README.md#status-and-deferred-work) identifies
the acceptance baseline and deferred qualification. Never promote skipped or historical checks to a current pass.

## Scope and provenance

Use ordinary .NET projects, public consumer APIs and generated contract glue. Add abstractions or
dependencies only for concrete needs. Keep the runtime SPA-framework-neutral and development/build/inspection
tools outside production. Upstream adaptations need source revision, paths, changes and applicable notices;
design inspiration alone does not require importing code. See [reference projects and licenses](01-reference-projects.md).
Use [repository skills](20-agent-skills.md) only within their stated scopes; they do not authorize publication,
dependency changes or extra acceptance claims.

## C# readability and lifetime rules

- Prefer clear statements, named intermediate values, and small methods over compressed expressions. Use LINQ when it describes one coherent transformation clearly.
- Use fluent chains for one coherent configuration, with one call per line when wrapping. Separate unrelated steps.
- Use `var` for local variables wherever C# can infer the intended type. Use explicit types only when inference is unavailable or would change the intended semantics.
- Use braces, one statement/declaration per line, and blank lines between logical steps. Aim around 120 characters without mechanically worsening readability.
- Use expression bodies only for trivial accessors or forwarding members. Keep simple expression callbacks inline and multi-step callbacks as blocks or named methods.
- Use `_camelCase` for private instance and static fields, including readonly fields; constants use `PascalCase`. Types/members use normal C# PascalCase and parameters/locals camelCase.
- Group members consistently: constants, static fields, instance fields, properties/indexers/events, constructors, public/protected/internal/private methods, nested types. Never reorder fields for style when declaration order affects initialization, memory layout, or an interop/ABI contract.
- Make ownership and disposal explicit for native handles, COM objects, subscriptions, and asynchronous operations. Keep resources alive until their actual use ends, including after cancellation or timeout.
- Apply style rules to authored code; change generators when generated output needs improvement, and preserve upstream formatting in vendored code.

## MSBuild XML readability

- In `.props`, `.targets`, and `.csproj` files, separate main sections such as property groups, item groups, imports, and targets with blank lines.
- Within each parent element, group children by purpose or semantic meaning. Keep framework and compiler settings together, analysis settings together, and package metadata together.
- Separate distinct groups within a parent with blank lines, while keeping closely related elements adjacent.
- Preserve evaluation order and conditions when organizing elements; formatting must not change build behavior.

## Testing conventions

- For .NET tests, use xUnit v3 through the `xunit.v3` package, version 4.0.1 or newer. Declare a compatible stable version using the dependency version policy below.
- Select `Microsoft.Testing.Platform` through `test.runner` in the root `global.json`, alongside the SDK configuration. This runner selection requires the .NET 10 SDK or later.
- The xUnit 4.x package supplies Microsoft.Testing.Platform v2 integration; `global.json` selects the runner for `dotnet test`. Recheck compatibility when upgrading the SDK or test packages.
- Use AwesomeAssertions for .NET test assertions, with an explicit compatible stable package version.
- Keep test dependencies outside shipping runtime packages. SPA and editor automation may use tooling appropriate to their environment; the .NET test conventions do not replace native integration or published AOT checks.

## Dependency versions

- Declare explicit stable versions for direct NuGet dependencies centrally in `Directory.Packages.props` when scaffolding the solution. Avoid floating versions such as `4.*`.
- Upgrade dependencies deliberately and run checks relevant to the change, including AOT or development-loop checks when affected. Explicit versions establish a tested baseline; they do not prohibit upgrades.
- Distinguish declared versions from a locked dependency graph. A normal NuGet version such as `4.0.2` expresses a minimum requirement; a lock file records the resolved direct and transitive versions. Where a committed lock file is used for repeatable builds, use locked restore in CI and update it as part of dependency changes.
- For published Spar libraries, allow compatible dependency resolution by consuming applications. Use exact-version constraints or upper bounds only for a demonstrated compatibility requirement; a repository lock file does not constrain consumer restores.

## GitHub identity and issue management

- Use `sotsera/spar` as the canonical repository for issues, milestones, and pull request targets. Use `ghidello/spar` as the development fork. Resolve repository identity explicitly; the local remote named `origin` currently points to the fork.
- Before any GitHub write operation, identify the target repository.
- Verify the active GitHub CLI account using `gh auth status`.
- Do not assume that the active account matches the Git repository.
- If the account is incorrect, ask for approval before switching.
- Never create issues, pull requests, or other GitHub resources
  using an unintended account.
- Always request approval before publishing issues.
- With each implementation PR, update the related existing issue's checklist, evidence links, and remaining work.
  After merge, reconcile the milestone tracker with closed issues. Use `Refs` for partial deliveries and `Fixes` only
  for completed issue scope; never infer integrated acceptance from a precursor probe.

## Branches and working directories

- Keep `main` as the long-lived integration branch. Use short-lived, descriptive branches for focused pull requests; do not maintain a permanent `dev`/`develop` branch for the current workflow.
- Start independent work from the latest upstream `main`, syncing the fork/local baseline as needed. Use a dependent branch only when the dependency is deliberate and documented in the PR.
- Use names such as `docs/project-foundation`, `feat/2-hot-reload-probe`, `fix/6-restart-race`, or `chore/update-tooling`. Include the upstream issue number when useful. One issue may span several focused branches/PRs.
- Commit during development as needed. After squash merging, sync the fork/local `main`, retire the finished branch, and start subsequent work on a fresh branch.
- One checkout is sufficient for sequential work. Use separate Git worktrees when concurrent tasks need isolated files, each on its own branch. Coordinate shared tooling, ports, and application profiles when running multiple development sessions.
- Preserve uncommitted and staged work when switching or renaming branches. Do not discard changes or delete unfinished branches as routine cleanup.

## Pull requests and upstream history

- Make changes on feature branches in the development fork and submit pull requests to `sotsera/spar` targeting `main`. Never push changes directly to upstream `main` or bypass its protection rules.
- Merge accepted pull requests using squash merge. Keep each pull request focused so its resulting commit represents one coherent change.
- Prefer **PR title + description** as the default squash commit message. Keep relevant implementation/commit details and validation in the PR description so the final commit is understandable without the branch's intermediate history.
- Use a descriptive PR title and explain the problem, resulting behavior, relevant implementation decisions, validation, and known limitations in the PR description. Include useful commit details, but remove temporary/WIP history from the final summary.
- Before merging, review the final squash commit title and body. The title should describe the final change; the body should retain the useful PR description, relevant commit details, validation, and issue references. GitHub's default message format may omit the PR description and must not be assumed to include it.
- Use qualified upstream issue references, such as `Fixes sotsera/spar#2`, when closing a completed implementation issue. Do not close the milestone tracking issue until its integrated acceptance criteria have evidence.
- Repository guidance records the workflow; GitHub branch rules enforce it. Upstream `main` requires a pull request and **Windows managed probe** from GitHub Actions, with an up-to-date branch, force-push/deletion protection, and no bypass entries. See the [CI gate proof](08-ci-merge-gate.md). Keep required check names stable and verify replacements before changing the rule; extend coverage through the [milestone checklist](03-milestone-1.md#ci-and-branch-protection-follow-up). Required external review approvals depend on reviewer availability and must not make a sole maintainer unable to merge their own PRs.
