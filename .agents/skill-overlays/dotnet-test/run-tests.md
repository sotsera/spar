---
core: dotnet-test/run-tests
binding-revision: "1"
mode: extend
---

# Spar runner binding

Run from the repository root. Read [AGENTS.md](../../../AGENTS.md) and verify `global.json`, `Directory.Packages.props`, and the selected test project if runner configuration has changed. The current baseline is SDK 10.0.402, native Microsoft.Testing.Platform command mode, xUnit v3 package 4.0.2, and AwesomeAssertions 9.6.0. These are observations, not an instruction to undo a deliberate upgrade.

Default local suite (builds its managed test projects first):

```powershell
pwsh -NoProfile -File eng/Test.ps1
pwsh -NoProfile -File eng/Test.ps1 -Suite processes
```

Ordinary `dotnet test` excludes `LongRunning=true`. After a matching solution build, use `dotnet test --solution Spar.slnx --no-build -p:IncludeLongRunningTests=true --minimum-expected-tests 1` to run all managed tests. The fixed suite catalog runs ordinary and real-process cases explicitly. See [CI and suite policy](../../../docs/21-continuous-integration.md) for all automatic suites and zero-selection safeguards. MSBuild filters do not automatically apply to direct executable or IDE runners.

For a named project use `--project`, not a positional path. Native MTP arguments follow directly, without the VSTest bridge `--` separator. For example, after a matching build:

```powershell
dotnet test --project tests/Sotsera.Spar.Bridge.Tests --no-build
```

For an xUnit method or class, append `--filter-method` or `--filter-class` with the actual fully qualified name found in the source. Confirm ambiguous or compound filters against that built test application's `--help`; do not infer support solely from the SDK's help. Do not mix xUnit filter families. The runner supports more than one syntax; prefer its simple xUnit filters for single methods/classes rather than migrating frameworks or installing an adapter.

`dotnet test --no-build` is not an MSBuild binlog-capture step. Capture a binlog on the separate build when diagnosing build failures. A test assertion failure or watcher failure needs its own test/session evidence, not an unrelated rebuild.

The managed tests launch real watcher processes in isolated fixtures. Coordinate shared outputs and never build or repack shared tools while a native development suite owns running processes. Preserve the one-Hot-Reload-owner rule; this skill does not start a persistent test watcher.

For package or native changes, select the integration checks from the [production](../../../docs/16-production-delivery.md), [compatibility](../../../docs/17-dependency-compatibility.md), or [external consumer](../../../docs/18-package-consumer.md) guide. Managed tests do not replace those checks or an actual image review. Clean-machine no-SDK/runtime verification remains separately deferred.
