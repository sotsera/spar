# Frontend distribution notices

Observed 2026-10-20 on the existing Windows development machine. This follow-up starts from merged milestone baseline `56e6bbaedba6bc362155a13099203012deb95c43`; publication receipts record the modified working tree and source hashes. Earlier production and milestone evidence is unchanged.

## Distributed code and notices

The real Vite build inventories bundled React `19.3.1`, React DOM `19.3.1`, and scheduler `0.28.0`. The sample's frontend post-build step adds the core license from installed Vite `8.3.4` for its emitted modulepreload helper. All four notices include the complete installed license text. The [retained notice](frontend-LICENSES.txt) has SHA-256 `6851BB9337567178B8E00AC05E7074F737C36607F92A5B44A818FFD84DA47C19`.

The shared build targets require the notice file, embed it in the asset archive, and copy it alongside the executable. Frontend-specific attribution stays in the frontend build. No package versions or runtime dependencies changed. Replacing or extending the frontend still requires reviewing its own distributed code, helpers, assets, and license obligations.

## Verification

- `dotnet build Spar.slnx --disable-build-servers`: [passed](build.log), zero warnings/errors.
- `dotnet test --solution Spar.slnx --no-build`: [65 passed](test.log), none skipped, including real watcher fixtures.
- Actual frontend build and `node eng/Test-FrontendNotices.mjs`: [passed](frontend-notices.log). Inventory and package versions match; full license texts match installed packages; repeating the post-build step leaves the output identical.
- Default sample NativeAOT [publish](publish.log) and [production boundary](production-boundary.json): passed. Embedded and sidecar notices have identical SHA-256; development/compiler/probe exclusions remain intact.
- Copied sample NativeAOT: [three production runs](production/receipt.json) passed, with actual typed greetings, rejected development switches, missing-runtime diagnostics, retained-process exit and profile lease checks. The [publication receipt](publication.json) hashes the new sidecar and all other payload files.

The three sample captures were individually reviewed: [run 1](production/run-1.png), [run 2](production/run-2.png), [run 3](production/run-3.png). All show the production SPA, zero counters, `Hello, Spar`, and `Connected`.

## Fresh package consumer

`eng/Pack-Local.ps1` [built all five local packages](pack.log), then `eng/Test-ExternalConsumer.ps1 -VerifyRender` created a new consumer outside the checkout with its own feed/cache. The [suite receipt](external/receipt.json) records package hashes and all four expected build/publication outcomes:

- Fresh build and actual development session passed, including CSS HMR, backend Hot Reload with retained state, coordinated restart, and cleanup. The [development receipt](external/development.json) reports 31 retained processes, no forced cleanup, and an unrelated process surviving. Its generic `rendering: not-exercised` field does not represent the external driver's separate patched-window capture.
- Default NativeAOT publication, full installed frontend license comparison, [Release boundary](external/production-boundary.log), and [copied execution](external/aot-run/receipt.json) passed.
- Adding the consumer-owned `DynamicMethod` dependency caused the expected [strict-AOT IL3050 rejection](external/strict-dependency.log), with no success receipt or fallback.
- Explicit attributed CoreCLR publication and [copied execution](external/coreclr-run/receipt.json) passed, including the dynamically generated greeting. Both modes retain receipt-hashed frontend notices: [AOT publication](external/aot-publication.json), [CoreCLR publication](external/coreclr-publication.json).

All three external captures were reviewed. The [patched development window](external/patched.png) shows yellow controls, local/backend counters `7`/`2`, retained text, `Hello from packages, Spar`, and revision `1`. The [AOT window](external/aot-run/run-1.png) shows the connected production SPA and `Hello, Spar`; the [CoreCLR window](external/coreclr-run/run-1.png) shows `Hello from generated IL, Spar`. Neither copied production run required forced cleanup.

## Limits

These are developer-machine executions with the installed WebView2 runtime. A clean Windows environment without the .NET SDK/runtime remains pending, as explicitly selected by the user. No clean-machine, installer, signing, update, broad compatibility, or public-release certification is claimed. Broader package/release review remains separate from this frontend-notice follow-up.

The older saved notice draft and its historical evidence remain preserved in the stash; this record uses newly built payloads and fresh execution results.
