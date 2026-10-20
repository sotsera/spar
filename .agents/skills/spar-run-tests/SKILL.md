---
name: spar-run-tests
description: "Choose and run Spar's .NET test commands and xUnit filters, or explain an exact test command. Use for one-time test execution and runner-option errors; not for writing tests, assessing regression coverage, or configuring Hot Reload."
license: MIT
---

# Run Spar tests

Read [the repository runner binding](../../skill-overlays/dotnet-test/run-tests.md) before choosing a command. It must declare `core: dotnet-test/run-tests`, `binding-revision: "1"`, and `mode: extend`. If it is absent, invalid, or contradicts the project, report the discrepancy and derive the command from `global.json`, the affected test project, and root `AGENTS.md`. User instructions and verified project constraints take precedence.

For a command-only request, inspect the necessary configuration and return the requested command without executing it. For execution, use the smallest requested scope and report the actual selected/executed, passed, failed, and skipped counts when available. Zero tests or a runner failure is not a passing test result.

Preserve an explicit no-build request. If the matching binaries are absent or stale, explain that limitation instead of silently building. For ordinary implementation validation, use the repository's build-before-test sequence. Never change runner packages, SDK settings, or the test framework to repair a command-line mismatch.

Native rendering, AOT execution, editor Hot Reload, and cleanup acceptance use the relevant repository integration guide; a managed test result does not establish those outcomes.

Adapted from dotnet/skills; see [provenance](../UPSTREAM.json) and [MIT notice](../dotnet-skills-LICENSE.txt).
