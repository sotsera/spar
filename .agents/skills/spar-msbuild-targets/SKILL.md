---
name: spar-msbuild-targets
description: "Author or review Spar custom MSBuild target dependencies, SDK extension hooks, and target return values. Use for broken target chains or prerequisite ordering; prefer spar-generated-files when the problem is a generated artifact missing from compilation, output, or clean tracking."
license: MIT
---

# Spar target authoring

Read the affected imports and target definitions in evaluation order, root `AGENTS.md`, and the relevant consumer/build guide. Diagnose the requested behavior before restructuring. Do not create a generalized target framework for a small extension.

- Use `DependsOnTargets` for prerequisites owned by the target; use `BeforeTargets`/`AfterTargets` to hook a pipeline owned elsewhere. A before/after hook alone does not invoke an otherwise unrequested target.
- Preserve existing values when extending dependency-chain properties, and account for later SDK imports that may overwrite the property. Prefer a named hooked target over replacing SDK targets such as `Build` or `BeforeBuild`.
- Use `Returns` to expose items to an `MSBuild` task caller; use `Inputs`/`Outputs` for timestamp-based incrementality. Verify the actual item flow rather than treating outputs as return-value caching.
- Track owned generated outputs through `FileWrites` and verify clean behavior. Capture partially written outputs where failure cleanup needs them; cleanup is not a reason to delete broad directories.
- Add `Inputs`/`Outputs` only with a complete dependency model and checks for clean, repeated, and changed-input builds. Target skipping must still supply required Compile/Content/EmbeddedResource items. Keep item declaration separate where necessary or verify MSBuild output inference.

Spar-specific constraints: contract generation precedes frontend compilation; design-time builds must not launch build tools or frontend servers; build tools remain private package dependencies; development and production host packages differ. Ordinary production builds deliberately rebuild the frontend. Do not remove that behavior as a speculative speed improvement. Package changes must work in a fresh external consumer, not just through source-project imports.

Validate proportionally with the repository build and affected consumer/integration lane. For a read-only review, report static findings and proposed checks without editing or claiming executed validation.

Adapted from dotnet/skills; see [provenance](../UPSTREAM.json) and [MIT notice](../dotnet-skills-LICENSE.txt).
