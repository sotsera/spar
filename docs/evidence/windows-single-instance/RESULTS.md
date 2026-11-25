# Windows single instance: results

> A spike. Its code was deleted with `spikes/` at the start of M2; it is in git history at [`spikes/windows-single-instance/`](https://github.com/sotsera/spar/tree/24f5db0942833047c81fa692908410596f3b8960/spikes/windows-single-instance) (commit `24f5db0`), and this record and its `results/` moved here. Paths under `spikes/` below name that history.

Recorded 2026-11-17. Host: Windows 11 Pro 10.0.26320, SDK 11.0.100, a NativeAOT probe (`Single.Win`, 2,191,872 bytes, 0 warnings). Guest: Windows Sandbox on the same host (no network, no camera, no microphone; `results/sandbox/sandbox-config.xml`), where the signed-in user `WDAGUtilityAccount` runs elevated.

The mechanism the user chose to measure (M1b "decide first"): **an exclusive lock file in the app's data folder plus a named pipe whose name comes from that lock file.** It mirrors the Linux decision (a lock file plus a Unix socket, `spikes/linux-integration`).

## Summary

| # | Question | Result | Recommendation |
|---|---|---|---|
| 1 | Normal handoff | 20 of 20 delivered. 1.2 ms median (max 1.5) from the secondary's `Main` to the primary having the arguments, with the server-process check; 38 ms median (max 48) from `Process.Start`, which is process start-up. The secondary exits 41 ms after it was started (median) | Adopt |
| 2 | Ten launches at once | 10 of 10 delivered, none lost; the last waited 5 ms for the single pipe instance | One pipe instance, reused with `DisconnectNamedPipe` |
| 3 | Hung primary (suspended) | The secondary gave up after its 2 s acknowledgement timeout (6 of 6, 2.05–2.07 s) and started no second primary. When resumed, the primary handled every queued launch. A second secondary waiting behind the busy instance failed after the 5 s lock wait instead (1 of 1) | 2 s acknowledgement timeout; report both as "not responding" |
| 4 | Launch while the primary is exiting | Pipe closed, lock held for 3 s: the secondary waited for the lock and became the primary with its own arguments (3 of 3, after 2.95–2.99 s). Held for 7 s: the secondary failed clearly after its 5 s lock wait (3 of 3) | Close the pipe before releasing the lock; wait up to about 5 s for the lock |
| 5 | Primary killed | The next launch took the lock at its first attempt (10 of 10): the OS releases the handle with the process. No stale state: the pipe disappears with its process too | No stale-lock handling needed |
| 6 | Pipe security for an **elevated** user | .NET's `PipeOptions.CurrentUserOnly` follows the token's **owner**, which is Administrators for an elevated token: the pipe became `O:BA D:(A;;0x1f019f;;;BA)`. A SYSTEM process connected to it. A SYSTEM squatter whose pipe was owned by Administrators passed the elevated client's check, received the arguments and sent a fake acknowledgement | **Do not use `CurrentUserOnly`.** Set the DACL from the token's **user** SID, and check the user of the server process |
| 7 | The explicit security | Pipe `O:<user> D:P(A;;FA;;;<user>)`, `FILE_FLAG_FIRST_PIPE_INSTANCE`, `PIPE_REJECT_REMOTE_CLIENTS`. The SYSTEM probe got "Access denied"; the SYSTEM squatter was refused because its process runs as `S-1-5-18` (exit 12) | Adopt |
| 8 | Name squatting | While the primary runs, nobody can take the name: one instance, so a second `CreateNamedPipe` fails with 231 (`ERROR_PIPE_BUSY`). Before the primary starts, a squatter of the same user wins: the primary runs without a pipe and a launch goes to the squatter (same user: not a security boundary). Another user's squatter is refused (7) | The primary keeps running, logs it, and launches fail clearly |
| 9 | Pipe name from the path or from the file | Through a **junction**, path naming failed (the secondary waited 5 s and gave up) and file-identity naming worked. Through an **8.3 short name**, both worked, because `Path.GetFullPath` expands short names | Name the pipe from the lock file's volume serial number and file index |
| 10 | Long path (384 characters) | Works. A raw `CreateFileW` call needs the `\\?\` prefix past `MAX_PATH`; .NET's own file APIs do not | Prefix raw Win32 path calls |
| 11 | Focus handoff (your desktop, real foreground rules) | The secondary, started by a click, calls `AllowSetForegroundWindow(serverPid)`; the primary then shows, restores and activates its window. Foreground in 9 of 9 (3 normal, 3 minimized, 3 hidden). **Without the call, 3 of 3 failed:** `SetForegroundWindow` returned false and the launcher kept focus | Required: the secondary calls it with the pid from `GetNamedPipeServerProcessId` |
| 12 | Focus in Windows Sandbox | Every case took the foreground, the controls included (15 of 15), although the guest has the default `ForegroundLockTimeout` (200,000 ms) | Sandbox focus results cannot show a focus rule |

## How it works

`Single.Win/Instance.cs`:

1. **Lock:** `instance.lock` in the data folder, opened with `FileShare.None`. A second process gets `ERROR_SHARING_VIOLATION` (0x80070020). The lock covers exactly the data folder a second instance would corrupt, so test data roots and separate channels are separate instances automatically.
2. **Name:** `spar-` plus 32 hex characters of SHA-256 over the lock file's volume serial number and file index. The secondary reads them by opening the file with no access rights, which a `FileShare.None` holder allows.
3. **Primary:** creates the only pipe instance with the explicit DACL, and loops: wait for a connection, read the message, queue it, write `ok`, wait for the client to close its end, `DisconnectNamedPipe`. Disconnecting discards unread data, so the primary waits for the client's close first.
4. **Secondary:** connect (100 ms), check that the server process (`GetNamedPipeServerProcessId`, then its token's user) is this user, call `AllowSetForegroundWindow` with that pid, send the arguments, wait 2 s for `ok`. With the lock held and no pipe, retry the lock every 20 ms for up to 5 s.
5. **Exit:** close the pipe, then release the lock.

Spike-only exit codes: 0 forwarded, 10 not responding, 11 the lock stays held and nobody serves, 12 the server runs as another user, 13 the primary could not create its pipe.

## Numbers

From `results/host-summary.txt` (final executable; "from main" is the secondary's `Main` to the primary's receipt, on one QueryPerformanceCounter clock):

| Case | Runs | From main (ms) | From `Process.Start` (ms) | Secondary wall (ms) |
|---|---|---|---|---|
| Normal | 20 | median 1.2, max 1.5 | median 38.0, max 48.3 | median 41.1, max 51.0 |
| Burst (10 at once) | 10 | median 1.8, max 5.0 | median 53.0, max 54.6 | median 57.0, max 59.2 |
| Long path | 3 | 1.3–1.6 | 35.4–44.6 | 37.8–47.0 |
| Through a junction (file id) | 1 | 1.5 | 38.4 | 41.2 |
| Through a short name (file id / path) | 1 + 1 | 2.4 / 2.6 | 40.1 / 54.2 | 42.9 / 66.0 |

The first version, with `CurrentUserOnly` and no server-process check, measured 0.7 ms median from main (20 runs); the token check costs about 0.5 ms.

## Problems met

- **`PipeOptions.CurrentUserOnly` is the token owner, not the user.** For an elevated administrator that is `BUILTIN\Administrators`, on both the server's DACL and the client's owner check (6).
- **A raw `CreateFileW` past 260 characters fails with error 3** without `\\?\` (10); the probe's first long-path run failed this way.
- **`NamedPipeClientStream.Connect(timeout)` keeps retrying a pipe that does not exist** until the timeout, so a missing pipe and a busy one look the same (`TimeoutException`). That is why the "busy behind a hung primary" case ends as "nobody serves" (3).
- **`DisconnectNamedPipe` discards data the client has not read,** so the primary must not disconnect right after writing the acknowledgement.
- **Windows Sandbox grants the foreground freely** (12).
- **`AllowSetForegroundWindow` returned true in a secondary that was not in the foreground** in several host runs (started from this harness), and false with error 5 in the Sandbox's no-click case. Its return value says nothing about whether the primary will get the foreground; only the primary's `SetForegroundWindow` result does.
- Probe bugs fixed during the run: the squatter opened a lock file the primary held, and the host script's wait matched an earlier run's log line.

## Not tested

- **Two sessions of the same user** (console plus Remote Desktop): the lock is per data folder, so the second session's launch would go to the first session's window. The secondary can see the server's session (`GetNamedPipeServerSessionId`, logged) and report it instead.
- **Elevated and non-elevated instances of the same user together** (integrity levels on the pipe); the host user is not elevated and the Sandbox user always is.
- **Another real user account** (the cross-user checks used SYSTEM in the Sandbox).
- **A launch from Explorer, a file association or a shortcut;** the click came from a launcher window driven by injected input.
- **The background-launch case on your desktop:** the primary's newly created window already had the foreground, so it showed nothing.
- **Network or redirected profile folders** for the lock file.

## Reproduce

From the repository root:

```bash
dotnet publish spikes/windows-single-instance/Single.Win -c Release
pwsh -NoProfile -File spikes/windows-single-instance/scripts/host.ps1 -ShortRoot <a folder reached through an 8.3 name>
node spikes/windows-single-instance/scripts/summarize.mjs artifacts/windows-single-instance/host
spikes/windows-single-instance/sandbox/run.sh start && spikes/windows-single-instance/sandbox/run.sh connect
spikes/windows-single-instance/sandbox/run.sh phases focus
spikes/windows-single-instance/sandbox/run.sh stop
# On the desktop, only after asking whether it is in use (injected clicks, windows take focus):
pwsh -NoProfile -File spikes/windows-single-instance/sandbox/guest.ps1 -Exe <published exe> -Phase focus -OutRoot artifacts/windows-single-instance/host-focus -DataRoot artifacts/windows-single-instance/host-focus/data
```

`run.sh connect` uses `spikes/windows-sandbox-checks/sandbox/find-client.ps1` to keep the Sandbox client minimized; `wsb connect` did not return, so its task was stopped after the phases. Cleanup: the sandbox was stopped, no Sandbox client window was left, and every data folder and log is under `artifacts/windows-single-instance/` (raw logs kept in `results/`, with the profile path and the host user's SID redacted).
