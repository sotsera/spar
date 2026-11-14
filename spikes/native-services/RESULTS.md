# Native services spike: file dialogs, file grants, credential storage

**Question.** Under NativeAOT with warnings as errors, can Spar (a) show the Windows file open/save dialogs from the
backend and hand the page an opaque grant instead of a path, and (b) store connection secrets with Credential Manager or
DPAPI? What does each piece cost, and how much of the dialog work can be automated without leaving windows on screen?

**Answer: yes to all of it.** Every check below passed in the published AOT executable, with zero warnings in every
variant. The dialogs work through hand-declared `[GeneratedComInterface]` COM, through CsWin32's `allowMarshaling:
false` structs, and through CsWin32's build-task mode with COM source generators. They are fully automatable in-process,
and with a thread-local WH_CBT hook no dialog was observed on screen. Credential Manager and DPAPI both work through
`[LibraryImport]`; neither isolates one app from another app of the same user.

Environment: Windows 11 Pro 10.0.26320 (26H2), SDK 11.0.100, Windows SDK headers 10.0.26100.0, CsWin32 0.3.369,
System.Security.Cryptography.ProtectedData 10.0.13 (11.0.0 is not in the local cache; 10.0.13 was, so nothing was
downloaded), win-x64, Release, `InvariantGlobalization=true`. All numbers were observed on 2026-11-13 in this folder.
Raw output is in `artifacts/results/`. `./measure.sh` reproduces it.

## What was built

| Path | Role |
|---|---|
| `NativeServices.App/` | `net11.0-windows` Exe, `PublishAot`, `IsAotCompatible`, warnings as errors from the repository props. `-p:SpikeParts=` picks the pieces for size variants. Modes: `all` (default), `dialogs`, `cred-write-peer`, `cred-read-peer`, `cred-verify`. |
| `Dialogs/ShellInterop.cs` | Hand-declared `[GeneratedComInterface]`: `IModalWindow`, `IFileDialog`, `IFileOpenDialog`, `IFileSaveDialog`, `IShellItem`, `IShellItemArray`, `IOleWindow`, `IFileDialogEvents`. `[LibraryImport]`: `CoCreateInstance`, `SHCreateItemFromParsingName`. |
| `Dialogs/FileDialogs.cs` | `FileDialogs.Show(owner, request, script)`: options, title, filters, default folder or folder, file name, default extension, multi-select, client GUID, results through `SIGDN_FILESYSPATH`, and a `[GeneratedComClass]` event sink. |
| `Dialogs/CsWin32Dialogs.cs` | The same Open dialog through CsWin32 0.3.369 with `allowMarshaling: false` (unmanaged COM structs). |
| `Dialogs/DialogWindows.cs` | Test-only automation: an STA thread with an unshown owner window, a WH_CBT hook that makes dialogs transparent, the watchdog, and dismissal. |
| `Grants/` | `GrantTable` (opaque handle → path, access, expiry) and `FilesService` (the page-facing sketch), plus checks. |
| `Credentials/` | `CredentialManager` (`CredWriteW`, `CredReadW`, `CredDeleteW`, `CredEnumerateW`, `CredFree`), `Dpapi` (`CryptProtectData`, `CryptUnprotectData`, `LocalFree`), the `ProtectedData` package, and checks. |
| `CsWin32ComProbe/` | Minimal probe of CsWin32's build-task mode with `comInterop.useComSourceGenerators: true`. |
| `measure.sh` | Publishes 11 size variants, runs their checks, and runs the cross-process credential check. |

## Declarations (checked against the Windows SDK 10.0.26100.0 headers)

- **COM from `um/ShObjIdl_core.h`.** IIDs come from the `MIDL_INTERFACE` lines. The CLSIDs are FileOpenDialog
  `DC1C5A9C-E88A-4dde-A5A1-60F82A20AEF7` and FileSaveDialog `C0B4E2F3-BA21-4773-8DBA-335EC946EB8B`. Both are registered
  `ThreadingModel=Apartment` in comdlg32.dll, with a proxy/stub registered for `IFileDialog` and `IFileOpenDialog`.
  - **Slot order.** Unused methods are `[PreserveSig] int SlotNN()` placeholders. A base interface (`IFileDialog`) must
    declare every slot through `SetFilter` (slot 26). Otherwise the derived `IFileOpenDialog::GetResults` lands on the
    wrong slot (see Failures).
  - **Raw pointers.** `Show`, `GetResult`, `GetResults`, `GetItemAt`, `GetDisplayName`, `Close` and `ClearClientData`
    are `[PreserveSig]`. Methods that hand out an interface return a raw `nint` holding one owned reference.
    `ShellCom.Wrap` turns that into a `CreateObjectFlags.UniqueInstance` wrapper, and the caller releases it with
    `ComObject.FinalRelease()` on the STA. Event methods take borrowed `nint` pointers, so no wrapper is created per
    callback.
  - **Constants.** `SIGDN_FILESYSPATH = 0x80058000`. `FOS_*` values are from the header. Filter strings
    (`COMDLG_FILTERSPEC`, from `shared/ShTypes.h`) are CoTaskMem copies that are freed after `SetFileTypes`.
  - **Explicit IIDs.** IIDs are passed explicitly (`ShellConstants.Iid*`), with no `Type.GUID` lookup.
- **`um/wincred.h`.** `CREDENTIALW` is 80 bytes on x64, asserted at run time. `FILETIME` is declared as `long` at
  offset 24, which is already 8-aligned. The constants are `CRED_TYPE_GENERIC=1`, `CRED_PERSIST_SESSION=1`,
  `LOCAL_MACHINE=2`, `ENTERPRISE=3` and `CRED_MAX_CREDENTIAL_BLOB_SIZE=5*512`. `SetLastError=true`, and the error is
  read immediately. `ERROR_NOT_FOUND` (1168) maps to "absent". Every `CredRead`/`CredEnumerate` buffer goes back
  through `CredFree`, and the blob copy is wiped first.
- **`um/dpapi.h`.** `DATA_BLOB` is `{uint, byte*}`. `CRYPTPROTECT_UI_FORBIDDEN` is always set. The output is
  `LocalFree`d, and plaintext output is wiped before the free.
- **CsWin32 alternative for the flat APIs.** `CredWrite`, `CredRead`, `CredDelete`, `CredEnumerate`, `CredFree`,
  `CryptProtectData`, `CryptUnprotectData` and `LocalFree` are all covered by CsWin32 with `allowMarshaling: false`
  (`NativeMethods.txt`). They generate and compile cleanly, and include SafeHandles that call `CredFree`. They were
  **not called** in this spike, because the spike exercises the `[LibraryImport]` versions the task asked for.

## 1. File dialogs

### Scenarios (AOT exe, hand-declared COM, `artifacts/results/run-all.txt`)

| Scenario | Result |
|---|---|
| Open, `SetDefaultFolder` (fresh `SetClientGuid` + `ClearClientData`), accept `a.sparrecipe` | **pass**: one path in the default folder, through `GetResults` → `GetItemAt` → `GetDisplayName(SIGDN_FILESYSPATH)` |
| Title and owner | **pass**: `GetWindowText` matches `SetTitle`, and `GetWindow(GW_OWNER)` is the owner HWND |
| Open, multi-select (`FOS_ALLOWMULTISELECT`), file name `"a.sparrecipe" "b.sparrecipe"` | **pass**: 2 paths |
| Cancel (IDCANCEL) | **pass**: `Show` returns `0x800704C7` = HRESULT_FROM_WIN32(ERROR_CANCELLED) |
| `IFileDialog::Close(E_ABORT)` from `OnFolderChange` | **pass**: `Show` returns `0x80004004`, and the window was never visible |
| `IFileDialog::Close(ERROR_CANCELLED)` | **pass**: reads as a cancel |
| Open a missing name with `FOS_FILEMUSTEXIST` | **pass**: native error box, dismissed by the watchdog after the 2 s deadline |
| Save, `SetDefaultExtension("csv")`, name `export` | **pass**: `<folder>\export.csv`, and the file is not created by the dialog |
| Save over an existing file, `OnOverwrite` → `FDEOR_ACCEPT` | **pass**: no native prompt, `S_OK` |
| Save over an existing file, default `OnOverwrite` | **pass**: native "Confirm Save As" task dialog, dismissed by the watchdog (IDNO via `TDM_CLICK_BUTTON`, then IDCANCEL) |
| Caller in the **MTA** | `CoCreateInstance` `S_OK`, `Show` works, but the dialog runs on another thread (COM's host STA, thread 70192 vs caller 57692) |
| CsWin32 `allowMarshaling:false`, file name prefilled, watchdog posts IDOK | **pass**: path returned |
| CsWin32 `allowMarshaling:false`, cancel | **pass**, but cancel arrives as `COMException(0x800704C7)`, because `Show` calls `ThrowOnFailure` |
| CsWin32 build task + `useComSourceGenerators` (probe) | **pass**: `IShellItem` display name, plus a `[GeneratedComClass]` `IFileDialogEvents` closing the dialog → `0x800704C7` |
| `FilesService` end to end (open → grant → readText, save → grant → writeText, release, cancel) | **pass** (see section 2) |

### STA requirement

- **Use the window's UI STA thread.** The dialog class is apartment-threaded. From an STA thread, everything runs on
  that thread inside `Show`'s modal loop, and that is where Spar's window thread already is.
- **What an MTA caller gets.** COM creates the object in a host STA and marshals every call through the registered
  proxies. It "works", but the dialog's thread is not the owner's thread, every event callback crosses apartments, and
  modality depends on a cross-thread owner.
- **The spike's guard.** `FileDialogs.Show` refuses a non-STA caller.

### Latency (AOT, 5 runs, ranges of medians in ms; first call in parentheses)

| Step | Median | Notes |
|---|---|---|
| `CoCreateInstance(FileOpenDialog)` + `FinalRelease` | 0.05–0.15 (first 2.8–6.2) | n=20 |
| `Show` → first `OnFolderChange` (window created, not yet visible) | 89–216 | n=10 |
| `Show` → automated IDOK → `Show` returns | 148–348 | n=10, includes the dialog's own folder enumeration |

### Automation: what works

- **Event-driven, recommended for tests.**
  - The first `IFileDialogEvents::OnFolderChange` arrives once the window exists but before it is shown
    (`visibleWhenFound=False` in every run).
  - From there, `QueryInterface(IOleWindow)` → `GetWindow` gives the HWND. The test can call `SetFileName` then
    `PostMessage(WM_COMMAND, IDOK)` for a **real** result through the dialog's own validation, post IDCANCEL, or call
    `IFileDialog::Close(hr)`. `Close` returns that HRESULT from `Show` without ever showing the window. This is also the
    production way to abort a dialog when its window closes.
- **Watchdog, for callers without an event sink** (CsWin32 struct mode, MTA).
  - A background thread finds the dialog by its unique title with `FindWindow("#32770", title)`, every 5 ms.
  - **Commands posted before the dialog is visible are ignored.** In the first attempt IDOK was posted at 30 ms and the
    dialog sat until the deadline. The watchdog therefore waits for `IsWindowVisible`, then posts IDOK or IDCANCEL. A
    file name prefilled with `SetFileName` before `Show` is accepted.
- **Deadline dismissal.** After the deadline, every `#32770` window of the dialog thread gets IDCANCEL. Secondary boxes
  also get IDNO, and a `TDM_CLICK_BUTTON(IDNO)` for task dialogs. The overwrite confirmation is a task dialog with Yes
  and No only, and it ignores `WM_COMMAND IDCANCEL`.
- **Keeping it off screen.**
  - **Moving alone does not work.** Moving the dialog offscreen in `OnFolderChange` is not enough: when it shows, it
    repositions itself onto the desktop at (0,0).
  - **Make it transparent.** The automation therefore adds `WS_EX_LAYERED` with alpha 0 and moves it.
  - **Message boxes need the hook.** The message box and the overwrite task dialog appeared centred on screen at
    (769,407) until a thread-local `WH_CBT` hook (no DLL, this thread only) re-hid every `#32770` on `HCBT_CREATEWND` and
    again on `HCBT_ACTIVATE`. They reset their extended style while initialising, so hiding at creation alone was not
    enough.
  - **What was observed.** In the final runs, 5 ms sampling recorded **no dialog visible on the desktop** in any
    scenario (`seenOnScreen=[]`).
- **Limits.**
  - The hook only covers dialogs on the hooking thread, so the MTA case is not covered.
  - The sampling cannot prove that zero frames were composed.
  - Activation and focus side effects on the user's foreground window were not measured.

**For Spar.** Production code needs none of this. A test seam over the dialog (an `IFileDialogService`) is enough for
app tests, and the real dialog can be checked in framework tests with the event-driven script plus the hook.

### CsWin32 versus hand-declared COM

| | Hand `[GeneratedComInterface]` | CsWin32 `allowMarshaling: false` | CsWin32 build task + `useComSourceGenerators` |
|---|---|---|---|
| AOT warnings | 0 | 0 | 0 |
| Shape | managed interfaces, ComWrappers | unmanaged structs, function-pointer vtables | generated `[GeneratedComInterface]` from metadata |
| Ref counting | `FinalRelease` for deterministic release | manual `Release()` | ComWrappers (finalizer unless `FinalRelease`) |
| HRESULTs | `[PreserveSig]` where you choose | failures throw, so cancel is an exception (`preserveSigMethods` not tried) | failures throw |
| Event sink | `[GeneratedComClass]`: works | needs a hand-built CCW vtable: **not tried** | `[GeneratedComClass]`: works (probe) |
| Maintenance | hand slot ordering, which caused one real bug | generated | generated |
| Requires | nothing new | the current repository pattern | `<CsWin32RunAsBuildTask>true</CsWin32RunAsBuildTask>` and **`allowMarshaling: true`**, which changes every CsWin32 output in that project |
| Exe size (see table) | +196,608 over a COM baseline | +137,728 over the same baseline | probe exe 2,010,624 |

## 2. Backend-owned grants (sketch, implemented and checked)

**Model.** The page never sends or receives a path. A grant is created only from a path the user picked in a
backend-owned dialog.

```text
GrantTable (per window, in memory)
  handle "fg_" + base64url(16 random bytes)  →  { fullPath, access: Read | Write, expiresAt }
  Issue(path, access, lifetime)   Require(handle, access) → Unknown | Expired | Forbidden
  ReadText(handle)   ≤ 16 MiB, UTF-8 with BOM detection
  WriteText(handle)  temp file in the same folder, then File.Move(overwrite)
  Revoke(handle), RevokeAll()  ← on window close
```

**Page-facing contract (sketch of a generated `@spar/files` client).**

```ts
interface FileFilter { name: string; patterns: string[] }        // "*.sparrecipe"
interface PickedFile { handle: string; name: string }            // display name only, never a path

files.pickOpen({ purpose?, title?, filters?, multiple? }): Promise<PickedFile[] | null>   // null = cancelled
files.pickSave({ purpose?, title?, suggestedName?, filters?, defaultExtension? }): Promise<PickedFile | null>
files.readText(handle): Promise<string>
files.writeText(handle, text): Promise<void>
files.release(handle): Promise<void>
// errors: { code: "unknown" | "expired" | "forbidden" | "too-large" | "io" }
```

- **Purpose, not folders.** `purpose` is an app-registered key, for example `"recipes"` or `"exports"`. The backend
  maps it to a dialog client GUID so the dialog remembers the last folder per purpose, plus default filters. The page
  never names a folder.
- **Access.** Open grants Read. Save grants Write and Read (to re-read what was written). There is no directory
  listing, no sibling access, and no way to turn a path into a grant from the page.
- **Cancel.** It is an ordinary `null`, not an error, and issues no grant (checked).
- **On restart.** Grants live only in memory, so a new process does not know old handles (checked: `Unknown`). The page
  must call `pickOpen` again. A "recent files" feature should re-open through a backend dialog prefilled with
  `SetFolder` and `SetFileName`, not by persisting grants. Persisting grants would need storage for paths, revalidation,
  and a rule for when user consent expires; that is not proposed for M3.
- **Lifetime.** A TTL (8 h in the sketch, checked with a manual `TimeProvider`), explicit `release`, and `RevokeAll`
  when the window closes.
- **Numbers.** `Issue` and `Require` medians are under 1 µs (n=1000). 10,000 handles were all distinct.
- **Gaps, not built.**
  - **The grant binds a path, not a file identity.** A file replaced between pick and read is read as its new content.
    One option is to capture the file ID (`GetFileInformationByHandleEx(FileIdInfo)`) at grant time and verify it on
    use.
  - **The atomic write does not preserve the original file's ACL or alternate streams.** `ReplaceFile` would.
  - **Streaming.** Large files need streams (also in M3).
  - **One dialog at a time per window.** A second `pickOpen` while a dialog is open must be refused or queued.

## 3. Credential storage

Only fake secrets were used: `...;Password=FAKE-not-a-secret-<guid>;...`, 177 bytes. Every target used the prefix
`SparSpike/native-services/`. All were deleted, and `CredEnumerateW("SparSpike/*")` was empty at the end of every run
(`artifacts/results/peer.txt`, and `cmdkey /list:SparSpike*` printed `* NONE *`).

| | Credential Manager (`CRED_TYPE_GENERIC`) | DPAPI (`CryptProtectData`, current user) |
|---|---|---|
| Round trip | **pass**, for both `LOCAL_MACHINE` and `SESSION`. `CredWrite` on an existing target replaces it. | **pass**: protect → file → unprotect. |
| Size limit | **2,560 bytes** accepted. 2,561 is refused with **Win32 error 1783** (`RPC_X_BAD_STUB_DATA`, LSA's RPC range check, not a tidy "too long"). | No practical limit: 1 MiB took 46–59 ms. |
| Overhead | none visible | +281 bytes for 177 plaintext bytes with a description, +229 without |
| Persistence | `SESSION`: until logoff. `LOCAL_MACHINE`: this user on this machine, survives reboot. `ENTERPRISE`: roams (not tested). | Wherever you store the blob. It decrypts for the same user, and the user master key roams with the profile. |
| Isolation between apps | **None.** A second process read the secret, and `cmdkey` (another program) listed the target and user. | **None.** Any process of the user can decrypt. App-embedded entropy is obscurity only. Wrong entropy or a tampered blob → error 13 (checked). |
| Visibility to the user | Listed in Control Panel → Credential Manager and by `cmdkey` (target and user, not the secret). The user can delete it. | An opaque file. |
| Pitfall | — | **The `szDataDescr` description is stored in clear text inside the blob** (checked: the UTF-16 bytes were found). Never put secrets in it. |
| Latency, median (4 runs) | read 0.04–0.10 ms (first 3.4–9.8). Write `SESSION` 0.09–0.23. Write `LOCAL_MACHINE` **4.0–10.9**. Write + delete `LOCAL_MACHINE` 7.9–20.9. Enumerate 0.13–0.23. | protect 0.10–0.27, unprotect 0.08–0.20 |
| `ProtectedData` package | — | 10.0.13 from the cache. It works under AOT with no warnings, and it interoperates with direct `CryptProtectData` both ways (checked). Same latency. |

## 4. Sizes (Release, NativeAOT, win-x64, bytes; `artifacts/results/sizes.txt`)

Each variant includes that piece's **checks and reporting code** (`Report`, interpolation, `Stopwatch`, threads), so a
delta is an upper bound for the piece itself. Every variant published with **0 warnings**.

| Variant (`SpikeParts`) | Exe | Delta |
|---|---|---|
| `None` | 992,256 | 0 |
| `ComBaseline`: one `[GeneratedComInterface]` call (`IShellItem`) | 1,899,520 | **+907,264** over None. ComWrappers plus the reflection used by `IDynamicInterfaceCastable`. A WebView2 host already pays this; D8's WebView2 exe is 2,139,136. |
| `Dialogs` (hand COM + automation + checks) | 2,095,104 | +1,102,848 over None |
| `ComBaseline;Dialogs` | 2,096,128 | **+196,608** over ComBaseline: the marginal cost in a WebView2 app |
| `CsWin32Dialogs` | 1,488,896 | +496,640 over None |
| `ComBaseline;CsWin32Dialogs` | 2,037,248 | **+137,728** over ComBaseline |
| `Dialogs;Grants` | 2,298,880 | +203,776 over Dialogs (`RandomNumberGenerator`, `FileStream`/`StreamReader`, `Base64Url`, `FilesService`) |
| `CredMan` | 1,311,744 | +319,488 over None (includes `Win32Exception`, UTF-8 and date formatting) |
| `Dpapi` | 1,193,472 | +201,216 over None (includes file I/O, `Random`, `Win32Exception`) |
| `ProtectedData` | 1,019,904 | +27,648 over None (no file I/O in its checks) |
| all six | 2,371,584 | CredMan + Dpapi + ProtectedData + CsWin32Dialogs together add **+72,704** over `Dialogs;Grants`, so the marginal cost once the BCL pieces are shared is small. |
| `CsWin32ComProbe` (build-task COM generators, minimal) | 2,010,624 | — |

Adding the eight CsWin32 credential and DPAPI APIs to `NativeMethods.txt` (generated, not called) left every size
unchanged: unused generated code is trimmed.

## Failures and their causes

1. **`ClearClientData` threw `COMException 0x80004005`.** It returns `E_FAIL` when nothing is stored for the client
   GUID. It is now `[PreserveSig]` and the result is ignored.
2. **Access violation in `IFileOpenDialog::GetResults` (AOT exe: segfault; JIT: `0xC0000005` with a managed stack).**
   The hand-declared `IFileDialog` omitted its last slot (`SetFilter`) because the spike never calls it, so every
   `IFileOpenDialog` method was one slot off. Fixed by declaring every base-interface slot. **A Spar slot-order test
   against the header or CsWin32 metadata is worth having** if COM stays hand-declared.
3. **NativeAOT-only cast failure, with no build warning.**
   - **Symptom.** Wrapping the save dialog as `IFileSaveDialog`, while none of `IFileSaveDialog`'s own methods is called
     anywhere, failed at the cast: `InvalidOperationException: Operation is not valid due to the current state of the
     object`, from `IIUnknownDerivedDetails.GetFromAttribute` → `ComObject.IsInterfaceImplemented`.
   - **Scope.** It passes under JIT (`run-save-Derived-jit.txt`), and a reachable call to one of its methods makes it
     pass under AOT (`run-save-Rooted.txt`).
   - **Workaround.** Ask for the interface whose methods you call (`IFileDialog` for save).
   - **Repro.** `-p:SpikeSaveRepro=Derived|Rooted`.
4. **Watchdog IDOK ignored.** It was posted about 30 ms after creation, before the dialog was initialised. The watchdog
   now waits for `IsWindowVisible`.
5. **Dialogs appeared on screen.**
   - **Position.** The dialog repositions itself onto the desktop when shown.
   - **Style reset.** The message box and the task dialog reset `WS_EX_LAYERED` while initialising.
   - **Fixes.** Alpha 0 plus the WH_CBT hook on create and on activate (see Automation).
6. **Windows left on screen during development.** In three runs before fixes 2 and 5 and the `TDM_CLICK_BUTTON`
   dismissal, a dialog stayed up until the process was killed:
   - two runs ended by `timeout` (120 s and 240 s);
   - one run ended by `Stop-Process` on my own PID after about 3 minutes.

   The overwrite task dialog ignores IDCANCEL. All three processes were ones I started. None remain, and the temp
   folders they left were deleted.
7. **`measure.sh`.** The first version passed `-p:SpikeParts=A;B`, and MSBuild rejected it as an invalid switch. It now
   escapes the semicolons as `%3B`.

## Observed, skipped, not tested

**Observed (AOT exe, this folder, 2026-11-13).**
- Every check in `run-*.txt`:
  - `all`: 40 passed, 0 failed;
  - the per-piece variants pass;
  - the `Derived` repro fails as intended.
- Zero warnings in all 13 publishes (11 variants plus 2 repro builds).
- Dialog latency, credential and DPAPI latency, and sizes as tabled.
- The dialog thread for an MTA caller.
- No `SparSpike/*` credential remains.
- No `NativeServices.App` or `CsWin32ComProbe` process remains.
- No `%TEMP%\spar-native-services-*` folder remains.
- No `ComDlg32` MRU value naming either exe was found under
  `HKCU\...\Explorer\ComDlg32` (`FOS_DONTADDTORECENT` was set, and `ClearClientData` ran for the spike's client GUID).

**Skipped.**
- `CRED_PERSIST_ENTERPRISE` (roaming).
- `CRYPTPROTECT_LOCAL_MACHINE` scope.
- The ProtectedData 11.0.0 package (not cached).
- Calling CsWin32's generated `Cred*`/`Crypt*` wrappers (generated only).
- An `IFileDialogEvents` sink in CsWin32 struct mode.
- CsWin32 `preserveSigMethods`.

**Not tested.**
- Dialogs inside a real WebView2 window, including:
  - owner disabling (modality);
  - focus return to the page;
  - a page-initiated call queued onto the UI thread.
- Focus and activation impact of the transparent test dialogs on the user's foreground window.
- File-identity checks.
- Long paths, UNC, OneDrive placeholders and non-filesystem items (`FOS_FORCEFILESYSTEM` was always set).
- High DPI and other display scales.
- ARM64.
- Behaviour after logoff for `SESSION` credentials.
- Credential Manager on a machine where policy disables it.

## Recommendations for Spar

- **Dialog bindings.**
  - **Option A: generated COM.** Use CsWin32's build-task mode with `useComSourceGenerators` (generated
    `[GeneratedComInterface]`, real `[GeneratedComClass]` event sinks, no hand slot maintenance). It needs
    `allowMarshaling: true` for that project, so it needs a decision. A separate small interop assembly would keep the
    rest at `allowMarshaling: false`.
  - **Option B: if the decision keeps `allowMarshaling: false`.** Hand-declare the dialog interfaces as the WebView2 ones
    already are, add a slot-order test, and wrap only the interfaces whose methods are called.
  - **Option C: CsWin32 struct mode.** It is the smallest, but it has no event sink and throws on cancel. It suits a
    fire-and-forget dialog, but loses `Close` on window shutdown.
- **Threading.** Run dialogs on the window's STA thread. Expose them as `Task`-returning backend calls that complete
  after `Show` returns. Abort with `IFileDialog::Close` when the window closes, and never replay.
- **Grants.** Adopt the handle-only contract above: `pickOpen`/`pickSave` → `{ handle, name }`, plus
  `readText`/`writeText`/`release`. Use per-purpose client GUIDs, in-memory grants, revoke on window close, and re-pick
  after a restart.
- **Credentials.** Use **Credential Manager** for SQL passwords:
  - `CRED_TYPE_GENERIC` with `CRED_PERSIST_LOCAL_MACHINE`;
  - target `<AppId>/<connection-id>`, with the SQL login in `UserName`;
  - **only the password in the blob**, which stays far below 2,560 bytes;
  - keep server, database and auth mode in ordinary settings.

  This is user-visible and user-removable, costs 4–11 ms per write, and is backend-only.
- **DPAPI.** Use it (the CsWin32 import or the 28 KB `ProtectedData` package) for larger blobs, such as token caches
  stored under the app's data folder. Never put anything in the description.
- **No isolation.** Neither store isolates apps of the same user, so say so in the docs.
- **The page never reads a secret back.** A page-facing `secrets` contract should be write-only and existence-only
  (`set`, `has`, `delete`). The backend uses the secret to connect.
