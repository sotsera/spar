# M1c step 7: the Linux CI image and job; online and offline on Linux

Plan M1c, build step 7: one Linux CI image, CI running the Linux window tests in it, and the page's `online` and `offline` events in a container. The decision record is "The Linux CI image and job; online and offline on Linux (implementation record; choices for review; 2026-11-20)" in `docs/decisions.md`. Raw logs are in `m1c-ci-image/`.

## Machine

Windows 11 with Docker Desktop 29.9.3 (Linux containers on WSL 2, kernel 6.18.58.1-microsoft-standard-WSL2, 32 CPUs). Docker Desktop's engine was stopped at the start and was started with `docker desktop start`; nothing was installed and no setting was changed. The image is `spar-linux-ci:m1c-step7` and the container `spar-ci-image-step7`, both named for this work.

## The image (`eng/linux-ci/Dockerfile`)

- **Contents:** Ubuntu 24.04, pinned by digest (`sha256:9c7f200c…`). From Ubuntu's archive, unpinned: `libwebkitgtk-6.0-4` (2.52.6), `libgtk-4-1`, `bubblewrap`, `xdg-dbus-proxy`, `xvfb`, `xauth`, `x11-xkb-utils`, `xkb-data`, `libxtst6`, `dbus-daemon`, `dbus-bin`, `dbus-session-bus-common`, `at-spi2-core`, `fontconfig`, `fonts-dejavu-core`, `clang`, `zlib1g-dev`, `iproute2`, `procps`, `ca-certificates`, `curl` and `xz-utils`. That is 278 packages: 217 MB downloaded, 919 MB installed. Node 24.22.3 from nodejs.org, checked against a SHA-256 pinned in the Dockerfile. The .NET SDK named by `global.json` (11.0.100, passed as a build argument), checked against the SHA-512 in Microsoft's release metadata for its channel. The user is `spar`, uid 1000, which replaces the base image's `ubuntu`.
- **Size:** 2,406,111,741 B on disk (2.41 GB; the apt layer 938 MB, the SDK 570 MB, Node 218 MB, the base 88 MB), and 587,832,500 B as a `docker save | gzip -1`.
- **Build time:** 314 s cold (`image-build-cold.log`): apt 155.7 s, Node 17.7 s, the SDK 107.3 s, export 26.0 s. With every layer cached it took 1.1 s (`image-build-cached.log`). CI keeps the layers in buildx's GitHub Actions cache (`type=gha,mode=max`), which is not measured here.

## The container run

The workflow's commands come from `eng/linux-ci/run.sh`, with the tree mounted read-only at `/src` and copied into the container (`copy client build check window windowaot blackbox`):

```
docker run -d --name spar-ci-image-step7 --init --shm-size=1g --security-opt seccomp=unconfined \
  -e CI=true -v '<tree>:/src:ro' spar-linux-ci:m1c-step7 sleep infinity
docker exec spar-ci-image-step7 bash /src/eng/linux-ci/run.sh copy client build check window windowaot blackbox
```

Locally, `<tree>` was a snapshot rather than the working tree: `git archive` of HEAD (554db00) plus this step's own files. The parallel black-box work in the working tree did not build at that moment. Two fixes owned by the black-box work were applied to the container's copy only, to verify the job; neither is in the shared tree from this step. Fix A: at `ProcessExit` the test environment no longer ends its tethered Xvfb and D-Bus, which go once the process is gone. Fix B: `ThemeTests` runs in a `DisableParallelization` collection, standing in for the scheme gate's own fix.

| Step | Time | Result |
|---|---|---|
| `copy` (without `.git`, `bin`, `obj`, `node_modules`, `artifacts`, `spikes`) | about 10 s | 430 C# files |
| `client` | 29.6 s + 1.8 s | |
| `build` (Spar.WindowTests, Notebook.EndToEndTests, Debug, `ContinuousIntegrationBuild=true`) | 157.4 s + 137.5 s | 0 warnings |
| `check` (bwrap's namespaces, the network switch) | 2 ms + 6 ms | passed |
| `window`, Spar.WindowTests (CoreCLR) | 55.3 s | 66: 57 passed, 9 skipped (Windows-only mechanisms) |
| `window`, Notebook.EndToEndTests window journeys (CoreCLR) | 7.9 s | 10: 9 passed and 1 failed in the first run, 10 passed in the next 2 |
| `windowaot`, Spar.WindowTests (NativeAOT, 19,656,832 B) | publish 22.0 s, run 54.9 s | 66: 57 passed, 9 skipped, 1 failure in the first of 3 runs |
| `windowaot`, Notebook.EndToEndTests (NativeAOT, 19,112,512 B) | publish 20.0 s, run 5.5 s | 10 of 10 passed, 2 runs |
| `blackbox`, Spar.WindowTests (CoreCLR) | 0.7 s | 21: 12 passed, 9 skipped, so the step **failed as designed** |

Not run here: the Notebook's black-box step. The `blackbox` step stops at the first project that skips.

**Before the fixes** (the snapshot as it is):

- Every window-test process exited 1 after all its tests passed. `dotnet test` reported "error: 1"; the xUnit executable exited 1 with "Total: 2, Errors: 0, Failed: 0". The cause, from strace: at `ProcessExit` the test environment closes its helpers' standard input, its tether ends Xvfb, and the shared GTK loop thread's `recvmsg` on the X socket returns 0. GDK's IO-error handler then calls `_exit(1)`. With fix A it exits 0, and no process is left.
- The scheme gate deadlocks (`scheme-gate-*.txt`). Without the network switch, 3 of 4 runs had 3–5 failures; with it, 4 of 8 runs had 4–5. Each failing run includes `ThemeTests.A_dark_window_test…` and `HostTests.Closing_one_of_two_windows…`, all timing out together at 30 s in `WindowSurface.StartAsync`. With fix B: 3 of 3 runs on CoreCLR and 3 of 3 on NativeAOT had no such failure.

The other failures, each seen once and not explained:

- **NativeAOT, `KeyTests.Pressing_a_character_off_the_US_keyboard…("€")`:** `keydown` and `keyup` only, with no `keypress` or `input`. This looks like the spare-keycode rebinding race in the pitfalls. It happened in 1 of 3 runs.
- **Notebook, `A_page_changed_in_another_window_is_never_overwritten_silently`:** the text typed after `Control+End` landed before the page's final newline (`"# Plans\nFrom this window\n\n"`). It happened in 1 of 3 runs.

## Security options

- **`--security-opt seccomp=unconfined`** (the container's own profile; nothing on the runner changes). Docker's default seccomp profile refuses `unshare(CLONE_NEWUSER)`, which WebKit's bwrap sandbox needs (spikes `linux-desktop`, `startup-prerequisites`). Under it, `run.sh check` runs `bwrap --unshare-all` in 2 ms.
- **Superseded on 2026-11-21 by decision "The Linux CI container's AppArmor profile":** on GitHub's runner `docker-default` refused bwrap's mounts ("Failed to make / slave: Permission denied"); the container now runs under `eng/linux-ci/apparmor-profile`, `docker-default` with mounts allowed. What follows was the reasoning before that run.
- **AppArmor stays at Docker's `docker-default`.** Ubuntu 24.04 hosts, such as GitHub's runners, set `kernel.apparmor_restrict_unprivileged_userns=1`. That restriction applies to unconfined processes, and to profiles whose policy mediates user namespaces (AppArmor ABI 4.0). Moby's profile template sets ABI 3.0, so a container under `docker-default` is not mediated. `apparmor=unconfined` would put the container under the unconfined-process restriction instead (user namespaces without capabilities), which is the failure reported for unconfined `unshare` on those runners (actions/runner-images#10443). This is reasoning from the template and the reports; it was not run on GitHub. Docker Desktop's VM has no AppArmor, so the local run cannot show it. The job prints the runner's sysctl, and `run.sh check` fails first, with the option named, if bwrap cannot start.
- **`--init`**, so PID 1 reaps what the tests leave (spike `linux-loopback`); **`--shm-size=1g`** for WebKit's shared memory (spike `linux-desktop`); **uid 1000** from the image.

## Online and offline

| Way | Root on the host? | Result |
|---|---|---|
| **A user and network namespace with only the loopback and a default route through it, the route removed and added** (`eng/linux-ci/network-switch.sh`) | no | **Chosen.** GIO's netlink monitor (`GNetworkMonitorNetlink`) reported `network-available` false and true right after each change (a Python GIO probe in WSL). The page's `offline` came 2,871–3,697 ms after the request (median 3,086 ms) and `online` 2,918–2,963 ms after it (median 2,949 ms), over 13 runs (`network-status.txt`). `navigator.onLine` was false and then true, and Echo answered through the bridge both times. Nested bwrap inside the namespace worked (WSL and the container). |
| A dummy interface in that namespace (`ip link add d0 type dummy`) | no | Created in WSL's user namespace. Not needed: a route through `lo` is enough. |
| `docker network disconnect` / `connect` of the test container | no, but it needs the Docker CLI outside the container | The container's `eth0` and default route went away within the 0.5 s the command took, and came back on connect (`docker-disconnect-route-watch.txt`). That is the same kind of route change, but the page test was not run with it. Not chosen: it needs a helper outside the container, and it takes the whole container offline, the build's restore included. |
| The UI process's own network namespace (`unshare` from inside the test) | — | Not possible: `CLONE_NEWUSER` needs a single-threaded process, and the test process is not. |

Further probes in the container, removed again: a window opened while offline opened in 97 ms with `navigator.onLine` false, and Echo answered. 24 sequential and 36 parallel window starts during a switch all opened (108–576 ms). The network test runs alone in a `DisableParallelization` collection, because the switch takes every window of the test process offline.

## Windows

The solution build was 0 warnings and 0 errors (22 s, the shared working tree with the parallel work). The size check checked 523 source files: 0 errors, 0 warnings.

## Not verified

- The job on GitHub Actions: the gha layer cache, the runner's AppArmor with `docker-default`, the runner's speed, and `docker cp` of the results.
- A clean run from `copy` with both fixes in the shared tree. The local run had them only in the container's copy.
- The Notebook's black-box tests in the container, and the Linux black box in it (from the parallel work).
- A window manager, Wayland, and a GPU.
