# Spike: several windows of one app, on WebView2 and WebKitGTK

Run 2026-11-27 (decision "M3d's decide-first questions", item 1). Questions:
- What does a second window cost: the time from asking for it to its page's first frame, and memory?
- How many renderers do 1, 2, 4 and 8 windows of one app on one origin get, with the app's own profile (not test windows), and what does ending one window's renderer do to the others?
- Does a modal GTK 4 window keep input from its owner, with and without a window manager?
- How do zoom and find behave on WebKitGTK (and zoom on WebView2)?

Machines:
- **Windows:** the development PC, Windows 11 Pro 10.0.26320, WebView2 Runtime 158.0.4370.45, .NET 11 CoreCLR Release. Every window was offscreen and never activated.
- **Linux:** Spar's Linux CI image (`spar-linux-ci:eval`: Ubuntu 24.04, GTK 4.14.5, WebKitGTK 2.52), on a private Xvfb with software rendering (`LIBGL_ALWAYS_SOFTWARE=1`), a session bus of its own, a private home, and the core limit at 1 byte. The window-manager runs used openbox 3.6.1, installed into the running container for this spike only (it is not in the image).

The page is the same on both hosts: a heading, a line of text to find, and 300 rows. It reports its first frame two animation frames after its script ran, as Spar's readiness waits.

## Windows (`Windows/`)

`dotnet run --project spikes/windows/Windows -c Release -- <run>` opens WinForms windows with the WebView2 SDK's managed layer. They share one environment on the default profile of a fresh folder under `artifacts/webview2-profiles/`, as an app's windows share its profile (not InPrivate). Each window serves the page from `https://app.localhost` with `?w=<name>`. Three runs: `results/windows-run1.json` to `windows-run3.json`.

| Measure | Result |
|---|---|
| First window, from the request to its first frame (the browser starts) | 392–416 ms, median 411 |
| Each later window, from the request to its first frame | 54–68 ms, median 59.2 (21 windows) |
| Of that, the controller on the shared environment | 16–23 ms, median 17.3 |
| Private memory of the browser's processes, one window | 162–165 MB |
| Each later window | +24–31 MB, median 26.4 |
| Renderers with 1, 2, 4, 8 windows | 1, 2, 4, 8: one per window, each holding one main frame, in all three runs |
| Window 3's renderer ended (`Process.Kill`) | only window 3 got `ProcessFailed` (`RenderProcessExited`, `Crashed`) and stopped answering; the other 8 pages answered at once |
| Zoom set to 1.5 on window 1 | only window 1 changed; its zoom stayed 1.5 after a reload; a new window opened at 1.0 |
| Ctrl+wheel through the DevTools protocol (`Input.dispatchMouseEvent` with Ctrl) | no zoom change, and no `ZoomFactorChanged` |

- **One renderer per window.** With the app's own profile, not only with test windows, Chromium gave every window on `https://app.localhost` its own renderer, up to the 9 windows opened here. Chromium may put windows together past its own process limit, which depends on the machine's memory. Nine windows did not reach it.
- **Ending one renderer leaves the others alone.** So "Reload page" can end just the hung window's renderer, found by its main frame (`GetProcessExtendedInfos`), instead of every renderer of the environment.
- **Zoom is the window's own.** A synthetic Ctrl+wheel does not reach the browser's zoom, so tests set the zoom through the host. Real Ctrl+wheel with `IsZoomControlEnabled` (on by default) was not tried here.

## Linux (`Linux/`)

`bash run.sh <label> [wm]` runs `Windows.Linux`, a GTK 4 program, in the container. Its windows have WebKitGTK views on one persistent `WebKitNetworkSession`, as an app's windows do, and one user content manager each, as Spar's host has. Each view loads the page with `webkit_web_view_load_html` on `https://app.probe/index.html?w=<name>`. Input is XTEST. Six runs: `results/linux-nowm1.json` to `-nowm3.json` without a window manager, and `linux-wm1.json` to `-wm3.json` under openbox; the numbers below cover all six.

| Measure | Result |
|---|---|
| First window, from the request to its first frame | 661–684 ms, median 673 |
| Each later window | 128–195 ms, median 158.5 (42 windows) |
| Memory of the process and its descendants (resident minus shared), the first window | +256–262 MB over the program alone (33 MB): the network process and the first web process |
| Each later window | +134–156 MB, median 141.3 (software rendering under Xvfb) |
| Web processes with 1, 2, 4, 8 views | 1, 2, 4, 8: one per view |
| `webkit_web_view_terminate_web_process` on view 3 | only view 3 got `web-process-terminated` and could not evaluate script; the other 8 answered |
| Zoom 1.5 on view 1 | only view 1; kept across a new document in the same view; a new view opened at 1.0 |
| Ctrl+wheel and Ctrl+plus (keypad) through XTEST, over the view | **no zoom**: the page got the wheel event with `ctrlKey` true, and the zoom level stayed 1.0 |
| `WebKitFindController`, "apple" (3 matches) | `count_matches`: `counted-matches` 3; `search`: `found-text` 3; `search_next`: `found-text` 1; "row 1" (111 matches): `found-text` 111; "zzz": `failed-to-find-text`. Each signal 1–21 ms after the call, median 6.7 (30 signals) |

**A modal window:** an owner window and a 300 × 200 window made transient for it. The probe clicks on the owner's page where the small window does not cover it, and on the small window.

| Step | Without a window manager | Under openbox |
|---|---|---|
| owner, while the small window is only transient | the owner's page got the click | the same |
| owner, while it is modal (`gtk_window_set_modal`) | **no click reached the owner** | the same |
| the modal window itself | got the click | the same |
| owner, after `gtk_window_set_modal(false)` | got the click | the same |

- **GTK 4 keeps input from a modal's owner by itself**, with or without a window manager, so the modal path does not depend on the desktop.
- **Openbox did not place the modal over its owner.** It put the 300 × 200 window at the top left of the screen (1, 20), while the owner was elsewhere (its top left at 1,119, 475). On X11 the host must centre a modal on its owner itself. On Wayland the compositor places it.
- **WebKitGTK does not zoom on Ctrl+wheel or Ctrl+plus.** The page sees those events, and the host must turn them into zoom levels, unlike WebView2, whose browser zooms by itself.
- **One web process per view, and terminating one leaves the others,** as on Windows. Linux's "Reload page" already ends only its own view's process.
- **Memory under Xvfb is software rendering's:** about 140 MB a window. A desktop with a GPU is likely lower, but it was not measured (plan table "Checks that need the user at the PC", the real-GPU row).
- **Reloading a `load_html` page** refetches its base address, which no server answers here, so the probe loaded the page again in the same view instead. Spar's host serves a real scheme, where a reload works.

## What this settles, and what it does not

- **For the renderer rule** (decision item 7): both engines gave each window its own renderer, and ending one left the others, so "Reload page" can end only the hung window's. Windows finds it by the window's main frame; Linux ends the view's own process. Sharing was never seen. Keep the fallback the decision named: if a hung window's renderer also holds another window's main frame, the dialog names those windows.
- **For a budget:** a second window's first frame took a median of 59 ms on WebView2 (at most 68) and 159 ms on WebKitGTK under Xvfb (at most 195), from a CoreCLR probe. Spar's own hosts and NativeAOT are measured when the slice is built.
- **For zoom:** Spar's Linux host has to handle Ctrl+wheel and Ctrl+plus, minus and 0 itself; on Windows the browser does, and the host clamps and saves through `ZoomFactorChanged`. Neither engine carries a zoom over to a new window, so `windows.json` restores it.
- **For modals:** GTK's modality works without a window manager. The host centres a modal on its owner on X11.
- **Not measured:** real Ctrl+wheel on WebView2; renderer sharing past Chromium's process limit (more windows than here, or a machine with little memory); a desktop with a GPU; Wayland; WSLg.
