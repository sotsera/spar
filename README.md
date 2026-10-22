# Spar

Spar (`Sotsera.Spar`) is a Windows desktop framework with a C# backend and a framework-neutral web SPA.
It prioritizes SPA HMR, supported C# Hot Reload, coordinated restarts, and controls usable by editors and AI tools.
Development uses CoreCLR; production defaults to NativeAOT, with explicit attributed CoreCLR compatibility.

## Run an application

Start with the [minimal application quick start](docs/23-minimal-application.md#start-edit-and-publish):
prerequisites, one terminal launch, editor workflows and publication. The ordinary app is
[samples/Spar.NativeSample](samples/Spar.NativeSample/Spar.NativeSample.csproj); its
[contract](samples/Spar.NativeSample.Contracts/BackendContract.cs) exports greeting and counter operations.
For a fresh app outside the framework checkout, use the [package consumer guide](docs/18-package-consumer.md).

## Find guidance

- [Documentation map and current status](docs/README.md) — application, development, runtime and delivery guides; deferred work.
- [Contributing](docs/24-contributing.md) — validation commands, coding/dependency policy and PR workflow.
- [Product direction](docs/02-product-direction.md) — confirmed requirements, proposals and deferred scope.
- [CI and evidence policy](docs/21-continuous-integration.md) — fixed automatic suites, desktop qualification and retention.
- [Agent entry point](AGENTS.md) — essential constraints and links to contributor policy.

Milestone 1 has [revision-bound acceptance evidence](docs/19-milestone-1-acceptance.md) on the selected Windows
baseline. The instrumented acceptance app lives in `fixtures/`; historical passes do not certify every later
sample, editor or machine configuration. See [current status and limits](docs/README.md#status-and-deferred-work)
before treating the project as release-ready.
