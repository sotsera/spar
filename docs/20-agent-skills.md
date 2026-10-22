# Focused repository skills

Six instruction-only skills under `.agents/skills/` adapt the reviewed [dotnet/skills revision cbb2bb74cb70e0c5ee49cca10470a92da03d4b25](https://github.com/dotnet/skills/tree/cbb2bb74cb70e0c5ee49cca10470a92da03d4b25). They support Spar's existing workflow; they do not change its runtime, packages, SDK, test framework, or integration acceptance requirements. Clean-machine verification remains deferred.

| Skill | Use it for |
| --- | --- |
| `spar-run-tests` | One-time .NET test execution, exact commands, and xUnit filtering |
| `spar-test-value-evidence` | Evidence that tests distinguish a specific changed contract from the relevant defect |
| `spar-binlog-capture` | Capturing a missing MSBuild diagnostic artifact for an actual investigation |
| `spar-binlog-analysis` | Analyzing an existing binlog, including when the original checkout or MCP tools are unavailable |
| `spar-msbuild-targets` | Custom target prerequisites, extension hooks, and return/item flow |
| `spar-generated-files` | Generated contracts, resources, notices, and clean tracking |

The repository location follows [official skill discovery guidance](https://developers.openai.com/codex/skills). Explicit invocation uses the skill name, for example `$spar-run-tests`. Descriptions support automatic selection when relevant; no skill is made mandatory for every turn. Hosts without skill discovery can read the corresponding `SKILL.md` when needed. Discovery/selection depends on the host; a structural validation pass is not evidence of automatic activation.

## Bindings and deliberate adaptations

The [runner overlay](../.agents/skill-overlays/dotnet-test/run-tests.md) binds the portable `dotnet-test/run-tests` contract to Spar's pinned native MTP/xUnit v3 configuration and build-before-test sequence. The `spar-run-tests` adapter reads that overlay and its version markers. The other skills are compact, self-contained adaptations rather than automatic imports of the upstream plugin.

The binlog capture mandate was narrowed to investigations. No `/bl` is added to `dotnet test --no-build`. Logs remain ignored local artifacts; disabling embedded project imports does not guarantee that properties or task arguments are free of secrets. Existing binlogs can be replayed with the pinned SDK without installing an MCP server or rebuilding the application.

Generated-file and target guidance preserves contract-before-frontend ordering, design-time exclusions, explicit output ownership, and production's deliberate frontend rebuild. It does not introduce speculative incrementality, broad cleanup, a new target abstraction, or an alternative Hot Reload owner. Test evidence remains revision-bound and distinguishes static assessment from executed detection; no full-suite mutation campaign is implied.

The general router, AOT migration, P/Invoke, refactoring, publishing, template, performance, and framework-specific skills were not installed. In particular, no upstream plugin manifest, unpinned prerelease binlog MCP, Copilot custom agent, or global skill installation is part of this change. Publishing and template workflows remain later scope.

## Provenance and updates

[UPSTREAM.json](../.agents/skills/UPSTREAM.json) records the repository, exact revision, six original source paths, SHA-256 values, and the nature of each adaptation. Hashes use UTF-8 with CRLF normalized to LF and no BOM. The original [MIT notice](../.agents/skills/dotnet-skills-LICENSE.txt) is retained and linked from every adapted skill. The shared runner overlay and local validation/evaluation material are authored for Spar.

Updates are deliberate: fetch a selected upstream revision into an ignored reference checkout, review each changed instruction and dependency, adapt only useful changes, refresh the provenance, and repeat structural and behavioral checks. Do not overwrite the Spar bindings with upstream defaults or install the whole plugin as an update shortcut.

## Validation

```powershell
node eng/Test-AgentSkills.mjs
# Optional: compare recorded original hashes with a local checkout of the pinned source.
node eng/Test-AgentSkills.mjs artifacts/dotnet-skills-review
```

The dependency-free Node check validates the inventory, constrained scalar frontmatter, license digest, local links, runner overlay, provenance fields, and evaluation-case references. It runs in the Fast suite behind **Windows managed probe**. It does not execute skill prompts, grade answers by keyword, fetch upstream, or install tooling.

[cases.json](../eng/agent-skills/cases.json) supplies positive, boundary, and dormancy prompts with review criteria. To evaluate selection, give an evaluator the catalog names/descriptions and the prompt first, withholding `expectedSkills` and `checks`; then load only selected skills and assess its proposed actions against the criteria. Use an isolated fixture for permitted execution. Record selection and semantic behavior separately from structural checks, and do not claim automatic host discovery or general model reliability from a manual walkthrough.

For changes to these skills, run the bundled skill-creator frontmatter validator when available, the Node check, and representative cases affected by the change. A full product integration rerun is needed only if product/build behavior changes. The [initial validation record](evidence/agent-skills-2026-10-20/README.md) identifies what was actually checked and the remaining evaluation limits.
