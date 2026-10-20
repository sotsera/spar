---
name: spar-binlog-capture
description: "Capture a fresh MSBuild binary log for a Spar build, restore, pack, or publish investigation. Use when the diagnostic artifact is missing; not to analyze an existing binlog, debug a test assertion, or instrument a no-build MTP test run."
license: MIT
---

# Capture a Spar build log

Preserve the failing command's SDK, configuration, properties, restore state, and project scope. Follow root `AGENTS.md`; a capture must not rebuild shared tools while a native development session owns them. Use an isolated reproduction or finish the owned session before rebuilding.

Add a unique, quoted binlog switch to the relevant MSBuild operation. With the pinned SDK, PowerShell example:

```powershell
New-Item -ItemType Directory -Path artifacts/binlogs -Force | Out-Null
dotnet build Spar.slnx --disable-build-servers '-bl:artifacts/binlogs/build-{}.binlog;ProjectImports=None'
```

`{}` gives each invocation its own filename. Retain the exit code, original console failure, exact command, revision/working-tree identity, and resulting artifact path. Do not silently retry with another runtime mode, SDK, or configuration. A failed build still provides useful evidence.

`ProjectImports=None` avoids embedding project/import source; properties, task arguments, paths, and other sensitive values can still appear. Keep binlogs in ignored local artifacts. Inspect and redact a derived report before sharing; never commit or upload a raw binlog merely because capture succeeded. Do not put session credentials or launch capabilities in command lines for a diagnostic capture.

Do not add `/bl` to Spar's native MTP `dotnet test --no-build` command. Capture its preceding build only if the failure is actually in MSBuild; preserve test/session logs for execution failures. Routine successful builds need not create a binlog.

Adapted from dotnet/skills; see [provenance](../UPSTREAM.json) and [MIT notice](../dotnet-skills-LICENSE.txt).
