---
name: spar-generated-files
description: "Fix or review generated Spar contracts, frontend assets, or notices missing from compilation, build/publish output, or clean tracking. Covers evaluation-time globs and generation timing; not Roslyn generator logic, frontend source edits, or general target-chain redesign."
license: MIT
---

# Generated artifacts in Spar builds

Identify the producer, exact output path, consumer item type, and consuming target before moving hooks. Follow root `AGENTS.md`. An evaluation-time glob cannot see a file first created during target execution; a file left from an earlier build can hide this defect.

Place build intermediates under `$(IntermediateOutputPath)`. Add generated `Compile`, `Content`, or `EmbeddedResource` items during target execution before their consumers run. `AssignTargetPaths` matters for content/resource processing; generated C# must also precede compilation. A target producing both kinds must satisfy both consumers, not merely run before `CoreCompile`.

Register files actually owned by the producer in `FileWrites`. Do not register user-authored inputs, entire directories, external application state, or fixture junctions. For copied outputs, capture the actual destinations and verify cleanup ownership. Adding clean tracking does not authorize recursive deletion.

Check clean first-build behavior, repeat builds, changed inputs, and the intended clean result. When adding incremental skipping, ensure required items are still declared or inferred on a skipped generation target. Preserve the compiler input and embedded client relationship so that stale contract metadata cannot pair with a different frontend.

The current contract target is [Spar.Contracts.targets](../../../build/Spar.Contracts.targets); production assets and notice copying are in [Spar.Assets.targets](../../../build/Spar.Assets.targets). Preserve their prerequisite ordering and design-time exclusions. Frontend output is deliberately rebuilt for production; changing that policy needs a separate measured and validated change. Never hand-edit generated C#/JS bindings.

For changes in packaged targets, use [the external consumer guide](../../../docs/18-package-consumer.md) and verify a fresh consumer with isolated packages/outputs. A working tree containing old generated files is insufficient evidence. For review-only requests, identify the likely missing item or hook and label the conclusion static.

Adapted from dotnet/skills; see [provenance](../UPSTREAM.json) and [MIT notice](../dotnet-skills-LICENSE.txt).
