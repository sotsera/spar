# Working on Spar

Start with [product direction](docs/02-product-direction.md), then the relevant step in [milestone 1](docs/03-milestone-1.md). Use [reference projects and licenses](docs/01-reference-projects.md) when evaluating upstream ideas or reuse. Distinguish confirmed requirements from proposals and unresolved decisions.

## Direction

- Keep the Spar name, `Sotsera.Spar` identity, and existing repository identity. Target Windows only for now.
- Write the implementation afresh. Previous Spar is evidence and scenario reference, not a source/build dependency. Do not inherit its toolchain pins or completion claims.
- Prioritize the development loop: framework-native SPA HMR, supported backend Hot Reload, and coordinated restart when needed.
- Use CoreCLR during development and NativeAOT by default for production. Dependency-driven CoreCLR compatibility must be explained and reproducible; unrelated build failures or framework AOT regressions must not silently trigger fallback.
- Make shared development controls usable by AI tools and editors. Prioritize VS Code while preserving a useful Visual Studio workflow. One mechanism owns backend Hot Reload per session.

## Implementation and verification

- Build the smallest integrated milestone slice. Use ordinary .NET projects, public consumer APIs, and generated contract glue. Add abstractions and dependencies only for concrete needs.
- Keep build, inspection, and development tools outside the production runtime. Keep the SPA framework-neutral.
- Preserve explicit permissions, native document authority, contract matching, bounded work, and native ownership. Cancellation or timeout does not prove execution or cleanup ended. Never replay backend mutations automatically.
- Confirm the working-tree state and preserve unrelated changes. Keep changes within the requested scope; do not alter sibling checkouts or repository remotes incidentally.
- Pin and verify tooling during milestone kickoff. Build with `dotnet build Spar.slnx --disable-build-servers`, then run `dotnet test --solution Spar.slnx --no-build`. The tests launch real watcher processes and edit isolated fixtures. See [the managed probe](docs/07-managed-probe.md) for editor verification; these commands do not verify native rendering or AOT.
- Run checks appropriate to the change. Native rendering, debugger Hot Reload, and published AOT behavior require actual integration evidence; mocks and historical results do not establish them.
- Record observed outcomes and unavailable checks. Keep milestone status and the README accurate; never mark skipped or unexecuted acceptance scenarios complete.
- For upstream code adaptations, record source revision, paths, changes, and applicable license notices. Design inspiration alone does not require importing upstream code or dependencies.

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
- Repository guidance records the workflow; GitHub branch rules enforce it. Upstream `main` requires a pull request and **Windows managed probe** from GitHub Actions, with an up-to-date branch, force-push/deletion protection, and no bypass entries. See the [CI gate proof](docs/08-ci-merge-gate.md). Keep required check names stable and verify replacements before changing the rule; extend coverage through the [milestone checklist](docs/03-milestone-1.md#ci-and-branch-protection-follow-up). Required external review approvals depend on reviewer availability and must not make a sole maintainer unable to merge their own PRs.
