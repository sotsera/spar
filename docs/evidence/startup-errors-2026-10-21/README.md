# D1 development startup errors

Executed on 2026-10-21 on the selected Windows developer machine. Base: merged PR #43,
`4e82ce4160c466449f3cbefac34b4207c866fd57`. Implementation: `981810a` on
`feat/d1-startup-errors`. PR #44 is a separate, still-open test correction; its additional case is not
included in this branch's 73-test baseline. Source/package hashes and compact observed results are in
[receipt.json](receipt.json). This record adds no clean-machine or public-release qualification.

Final filesystem hardening: `7901c33` changes only CLI pre-session error handling and its process driver.
An existing root can deny creation of the first random session subdirectory even though creating/opening
the root itself succeeds. A real ACL reproduction failed with exit `-532462766` before the correction;
its original [failure receipt](denied-first-failure.json) is retained. The CLI now maps IO/access failures
only before its first public session event; errors after that point keep the existing session behavior.
The final build had no warnings/errors, all 73 managed tests passed, and all **34** source CLI cases
passed. The denied-directory fixture restores its original ACL in `finally`. Earlier 33-case receipts
and editor walkthroughs below remain bound to `981810a`; the follow-up receipt records final checks
separately instead of reinterpreting those runs.

The final packages were restored into another new isolated consumer. All **34 packaged CLI cases**
passed, including the real create-directory denial with restored ACL, and valid native development /
updates / restart passed again with all 31 retained processes exited. Earlier Release exclusion and
copied AOT evidence remains bound to the first package set; the follow-up changes no production code.
The final packaged VS Code debugger regression also passed, with 22 retained process exits. Its final
BMP showed only a title bar when inspected, despite the driver's passing pixel assertion, and is not
counted as reviewed native-rendering evidence. The earlier complete source/package captures remain
scoped to their original builds; the follow-up alters no rendering code or editor presentation.

## Behavior and scope

The complete CLI argument list is validated before configuration access or startup. Expected input,
configuration, owner-identity and destination failures return `1`, one concise stderr line and one
bounded `startup-error` JSON line. No-argument usage remains stderr-only. Pre-session errors have no
session/generation identity and are not mirrored into an events file. Unknown argument values and raw
configuration JSON are not echoed; private debugger launch transport remains separate.

VS Code reveals Output for startup errors and nonzero close, including failures without a session event.
The project-launch bootstrap reports its own expected startup failures and relays coordinator preflight
messages. Existing public settings loading and normal process/admission/retirement logic remain in place.
Visual Studio may still show ordinary first-chance exceptions. D2 exact log discovery and D3 progress /
withdrawn-readiness presentation remain separate work.

## Executed checks

| Check | Observed result |
| --- | --- |
| Solution build, `dotnet build Spar.slnx --disable-build-servers` | Zero warnings/errors. |
| `dotnet test --solution Spar.slnx --no-build` | 73 passed, none failed/skipped. The final sandboxed attempt failed in test-runner named-pipe access before cases ran; the unsandboxed run passed. |
| `Test-DevelopmentStartup.ps1` | 33 real rejected coordinator processes passed; exit `1`, bounded output, preserved existing event file, no prospective event/session root, no observed application children and emptied owned jobs. |
| `Test-VSCodeStartup.ps1 -PauseForVisualReview` | Actual VS Code 1.143.0: missing executable, missing/malformed configuration and blocked session directory passed. No actual debug session started. Output was automatically visible and visually reviewed. |
| `Test-ProjectLaunch.ps1 -MinimalApplication -VerifyRender` | Native close, forced debugger termination, contract retirement and coordinator crash passed; each verified 13 retained process exits. |
| `Test-VSCodeDevelopment.ps1 -MinimalApplication -VerifyRender` | Actual VS Code 1.143.0, C# 2.160.4 and C# Dev Kit 3.40.215 passed the ordinary-app debugger/update/restart/Stop regression; 22 retained processes exited. |
| Actual source Visual Studio F5 | Missing contract in project-owner mode produced `contract_required`; malformed JSON produced `configuration_invalid`. The IDE reported exit `1` for each. Corrected configuration reached native readiness and native close ended gracefully; 13 retained processes exited. |
| `Pack-Local.ps1 -Output artifacts/d1/packages`; `Test-MinimalConsumer.ps1 -Feed artifacts/d1/packages -EvidenceDirectory artifacts/d1/consumer -VerifyRender` | Five local packages; fresh ordinary consumer with isolated feed/cache passed updates/restart and 31 retained process exits, Release exclusion and copied NativeAOT execution. Seven retained production descendants exited. |
| Packaged `Test-DevelopmentStartup.ps1 -Coordinator <consumer>/.tools/Spar.Dev.exe` | The same 33 process-level negative cases passed with empty owned jobs. The final signed driver also passed again against source output. |
| Packaged `Test-VSCodeStartup.ps1 -Coordinator <consumer>/.tools/Spar.Dev.exe -ExtensionDirectory <consumer>/.vscode/spar-extension -PauseForVisualReview` | All four actual-editor negative cases passed with no debug session. Automatically revealed Output was visually reviewed. |
| Actual packaged Visual Studio F5 | Fresh two-project `Consumer.slnx`, packaged bootstrap/coordinator and ordinary profile: `contract_required` and malformed-JSON `configuration_invalid` reported exit `1`. Corrected F5 reached native readiness; graceful native close verified all 13 retained process exits. |
| Packaged `Test-VSCodeDevelopment.ps1 -ConsumerDirectory <consumer> -MinimalApplication -VerifyRender` | Actual debugger regression passed with the copied adapter and packaged coordinator; 22 retained process exits and reviewed native update capture. |

The CLI matrix includes invalid limits, oversized/null/locked configuration, duplicate/unknown/extra
arguments, absent option values, invalid/unavailable/mismatched private owners, invalid no-build mode,
event-file creation failures and a blocked session directory. It tests actual process output rather than
formatter internals. Windows may create a verified system `conhost.exe` helper; that is recorded
separately. Short-lived unavailable identities are recorded without claiming exhaustive observation.
The driver bounds failure cleanup and does not infer completion from timeout.

The source Visual Studio walkthrough used the disposable six-project `Fixture.slnx`, actual IDE build
and F5, the **Spar development** profile and the optional `DevelopmentApplication.Run` entry point.
Failed processes ended before independent handle observation: their exit results are IDE-reported,
not claimed as independently retained handles. The positive session was independently monitored.
Its retained public tail reports `owned-processes-ended` then graceful `session-stopped`.

## Reviewed captures

- [VS Code automatic Output](vscode-auto-output.png): actionable preflight message and nonzero close.
- [Visual Studio coordinator error](vs-contract-error.png) and [configuration error](vs-config-error.png):
  coded diagnostics and actual application exit `1`.
- [Visual Studio corrected F5](vs-recovery.png): native Connected greeting and application-ready output.
- [VS Code supported update](vscode-patched.png): yellow controls, retained counters, patched greeting
  and Connected status.
- [Packaged Output](package-vscode-output.png), [consumer update](package-updated.png) and
  [copied NativeAOT](package-aot.png): reviewed actionable errors, state-preserving update and rendered
  production greeting respectively.
- Packaged Visual Studio [coordinator error](package-vs-contract-error.png),
  [configuration error](package-vs-config-error.png) and [corrected F5](package-vs-recovery.png): actual
  Debug diagnostics/exit `1` followed by the Connected native application.
- [Packaged debugger update](package-vscode-patched.png): actual C# debugger patch and frontend HMR
  with retained counter state and Connected native rendering.

All four project-launch BMPs were inspected. Native-close and contract-change show the Connected app;
debugger-stop and coordinator-crash captures show only a title bar and do not establish reviewed native
pixels for those two cases. Their lifecycle/process checks passed independently. The explicit source
Visual Studio recovery and VS Code capture above establish the native rendering claims here.

## First failures and limits

The initial CLI harness had a PowerShell continuation syntax error before cases ran. The next three
attempts counted the OS console helper as an application child; the driver now verifies its exact
system image path and still requires an empty job. Another attempt expected an inaccessible-file code
for a directory used as configuration; actual `FileInfo.Length` produced file-not-found, so that
expectation was corrected and a real exclusive-lock case added. These are harness corrections, not
runtime fixes inferred from reruns. Earlier attempts remain under ignored `artifacts/d1/preflight*`.

A disposable Visual Studio configuration-preparation command initially tried assigning an absent
`debuggerBackend` property and emitted a PowerShell error; it was corrected before the gestures.
The coordinator-error walkthrough showed a caught `ObjectDisposedException` first-chance diagnostic
from existing close/dispose handling. It did not prevent error propagation, exit `1` or corrected F5;
this change does not claim to eliminate that existing debug-output noise.

The initial packaged Visual Studio monitor ran inside the sandbox and could not observe the IDE-owned
processes. Its empty receipt is not exit evidence. A fresh unsandboxed monitor retained all 13 processes
while the corrected session was still running, then verified each actual exit after native close.

Routine logs, raw profiles and full session traces remain ignored under `artifacts/d1`,
`artifacts/project-launch` and `artifacts/vscode-development`. Retained screenshots and receipts contain
public diagnostics only. Native/editor execution is local to this installed baseline; historical
acceptance is not substituted for new checks. Broader machine/editor coverage remains unverified.

Documentation links, six skills/provenance with 14 structural cases, all 34 selector/gate tests and
`git diff --check` passed. Hosted checks belong to the PR's exact current revision. This change adds
the CLI matrix to the existing required-check baseline; it changes no protection rule or check name.
