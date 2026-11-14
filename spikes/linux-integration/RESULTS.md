# Linux desktop integration spike (WSLg + Docker): results

Recorded 2026-11-14. Question: do the desktop integrations of the Windows host also work on Linux, on WebKitGTK 4.1 (GTK3) and 6.0 (GTK4), on Wayland and on X11? Questions 1 to 9 were measured. Question 10 (file drop) was not tested.

## Summary

| # | Integration | GTK3 / 4.1 | GTK4 / 6.0 | Notes |
|---|---|---|---|---|
| 1 | Page-drawn title bar | **Works**, with host-driven move and resize | **Works**, same; one shutdown issue | WebKitGTK ignores `app-region` and `-webkit-app-region`. The move only verifiably moved the window on a real X11 window manager (openbox in Docker: 6/6 moves, 2/2 resizes). WSLg ignores synthetic drags, even on its own frame. Wayland drag untested. |
| 2 | Tray (SNI + dbusmenu over GDBus) | **Works** | **Works** | Real xfce4-panel: icon, tooltip, left click → `Activate`, menu, updates and panel restart all checked. Fake watcher: same, plus "no watcher" handled. |
| 3 | Close to tray and restore | **Works** | **Works, with a workaround on WSLg Wayland** | Restore to first page frame took 18–25 ms, and page state survived. On WSLg Wayland a hidden GTK4 window stays on screen as a stale image; unrealizing it after hiding fixes that. |
| 4 | Window placement | **Size and maximized: yes. Position: X11 only, and it drifts** | **Size and maximized: yes. Position: X11 only, through Xlib** | Wayland gives no position (confirmed). On WSLg Wayland, GTK4 started maximized is killed by the compositor (error 71); maximizing after the first map works. |
| 5 | File pickers | **Works** (in-process dialog; portal only with `GTK_USE_PORTAL=1`) | **Works** (`GtkFileDialog` always goes through the portal here) | Cancelled from code after 700 ms; the dialog was gone after the cancel. |
| 6 | Clipboard | **Works** | **Works** | Checked in Docker on X11 only, to keep the user's clipboard untouched. Reading content from outside the page raises `WebKitClipboardPermissionRequest`. |
| 7 | Single instance | GApplication: 6 ms handoff, but it hangs with a hung first instance and loses the launch while the first is exiting. Lock + Unix socket: 4–10 ms, bounded, no loss | Same (does not depend on the API) | Recommend the lock + socket. |
| 8 | Print to PDF with no dialog | **Works** (82 ms, 1 page) | **Works** (82 ms, 1 page) | Skia PDF, A4. |
| 9 | Presence | Same on both | Same on both | WSLg session: no notification server, no tray watcher, portal not running until activated. Private portal session: Notification v1 present, no GlobalShortcuts. Unity LauncherEntry signal delivered. |

**Effect on the API choice:** nothing here reverses the 6.0 recommendation. Every integration works on both APIs. The GTK4-only problems are:
- the stale window after hiding on WSLg Wayland (workaround verified);
- the start-maximized protocol error on WSLg Wayland (workaround verified);
- web processes that outlive a closed window after a window-manager move or resize, until the next button press (root cause not found; fix needed in M1);
- double-click to maximize fails if the move starts on mousedown. The threshold mode avoids this and is recommended for both APIs.

The first two are WSLg compositor behaviour, likely absent on real desktops (not verified). The third must be fixed or worked around in the host.

## Environment

- **WSL:** Windows 11 Pro 10.0.26320 with WSL Ubuntu 24.04 and WSLg (weston with `rdprail-shell`, XWayland). GTK 3.24.41 and 4.14.5, WebKitGTK 2.52.6 for both APIs, GLib 2.80. xdg-desktop-portal 1.18.4 with portal-gtk. NativeAOT .NET SDK 11.0.100. This is the same environment as `spikes/linux-wsl-desktop`.
  - Wayland globals: no `xdg_activation_v1`, no `xdg-decoration`, no `zwlr_*`; `weston_rdprail_shell` is present.
- **Docker:** container `spar-linux-integration-tray` from the existing local image `spar-linux-desktop:webkit2.52` (Ubuntu 24.04, same GTK and WebKit versions), run as uid 1000 with `seccomp=unconfined`, on `Xvfb :99` at 1280×800.
  - Added inside the container only: `xfce4-panel` 4.18.4 and `openbox` 3.6.1. That was 40 packages, 20.8 MB downloaded, after a 33.9 MB `apt-get update`.
  - **No image was added.** The container has been removed.
- **NativeAOT publish:** 0 warnings and 0 errors under the repository's `Directory.Build.props` (warnings are errors, code style enforced), with no suppressions. Sizes: GTK3 3,578,272 bytes, GTK4 3,582,464 bytes. Publish took 3.0–5.8 s warm, with an offline restore from the Windows global packages folder.

## Method

| Part | Role |
|---|---|
| `Integ.Linux/` | NativeAOT host, written for this spike. `-p:SparGtk=4` builds GTK4 + 6.0; the default is GTK3 + 4.1. It has one GTK thread with a GLib main loop and a `SynchronizationContext`. The page is served from `spar://app/` under the strict CSP, and the bridge script is injected in the top frame at document start. The host is driven through a control FIFO, and it logs marks on CLOCK_MONOTONIC so its log can be merged with the X11 tool, the fake tray host and the scripts. Files: `Host.cs` (window, frames, title-bar move, resize and menu, state, close path, commands), `HostServices.cs` (placement, print, pickers, clipboard), `Bridge.cs` (injected script: drag regions, double-click, context menu, edges), `Tray.cs` (StatusNotifierItem and com.canonical.dbusmenu over GDBus), `SingleInstance.cs`, `Presence.cs`. |
| `web/` | Probe page with a title bar drawn by the page: app icon, title, a search box, a "drag here" area, and minimize, maximize and close buttons. It also has a state line, an input for the "kept text" check, and Copy and Paste buttons. |
| `wsl/` | `sync.sh`, `publish.sh`, `lib.sh` (start a host, send commands, wait for marks, compute element positions), and `xtool.c`. `xtool` provides XTEST click, drag and press, `_NET_WM_STATE`, `WM_DELETE_WINDOW`, and a root watcher that logs the window manager requests GTK sends and the frames' ConfigureNotify. The scenarios are `titlebar-x11.sh`, `titlebar-wayland.sh`, `tray.sh` (private D-Bus session) with `fakesni.py` (fake watcher plus host), `placement.sh`, `pickers.sh`, `single.sh` and `print-presence.sh`. `summarize-titlebar.py` turns the runs into a summary. |
| `win/` | `wsl.sh` runs a command in the WSL working copy. `wincap.ps1` reads the Windows-side rectangle and state of the WSLg window, and saves an image of that window only through `PrintWindow`. It sends no input and activates nothing. |
| `docker/` | `setup.sh` (Xvfb, helpers, panel configuration), `tray-real.sh` (real xfce4-panel), `clipboard.sh`, and `xcap.c` (X11 capture to PNG, counting the tray icon's colours). |

```sh
# Git Bash, in spikes/linux-integration
win/wsl.sh sync; win/wsl.sh 'bash wsl/publish.sh gtk3; bash wsl/publish.sh gtk4'
win/wsl.sh 'bash wsl/titlebar-x11.sh gtk4 titlebar threshold'     # also: none|native, press; HOST_EXTRA="--region css"
win/wsl.sh 'bash wsl/titlebar-wayland.sh gtk4 titlebar'
win/wsl.sh 'bash wsl/tray.sh gtk4 wayland'                        # private D-Bus session + fakesni.py
win/wsl.sh 'bash wsl/placement.sh gtk4 x11'; win/wsl.sh 'bash wsl/pickers.sh gtk4 wayland portalconf'
win/wsl.sh 'bash wsl/single.sh gtk4 socket'; win/wsl.sh 'bash wsl/print-presence.sh gtk4'
# Docker (container from spar-linux-desktop:webkit2.52, files copied to /work): docker/setup.sh, then
# dbus-run-session -- bash docker/tray-real.sh gtk4 | docker/clipboard.sh gtk4 default | wsl/titlebar-x11.sh (with openbox)
```

Every window was 480×320 to 720×480 and closed itself within about 20 s. Pickers were cancelled after 700 ms. No Windows input was used. XTEST was used only against this spike's windows (XWayland under WSLg, and the container's Xvfb).

## Results

### 1. Custom title bar drawn by the page

**Drag regions.** WebKitGTK does not support `app-region` or `-webkit-app-region`:
- `CSS.supports` returned false for both properties on both APIs;
- the computed value was empty;
- pressing the bar with the host's drag handling off produced no window-manager request.

The host therefore drives the move from a script message.
- **Marking:** `data-spar-drag`, excluded by `data-spar-no-drag` and by interactive elements (`button, input, textarea, select, a[href], label, [contenteditable]`). An inherited custom property `--spar-region: drag | no-drag` worked the same (CSS mode, 2 runs).
  - The region check cost ≤1 ms per press. That is the resolution of `performance.now()` here, so the true cost is lower.
  - Proposal: the attribute, because it is explicit, cheap and independent of the cascade. The custom property is an option for regions that depend on media queries.
- **Mechanism:** the host records each button event on the web view before WebKit sees it. On GTK3 that is `button-press-event`; on GTK4, a `GtkEventControllerLegacy` in the capture phase. The host keeps the press event (button, time, root or surface coordinates, device). When the page asks, it calls:
  - GTK3: `gtk_window_begin_move_drag(window, button, root_x, root_y, press_time)`;
  - GTK4: `gdk_toplevel_begin_move(surface, device, button, x, y, press_time)`.

  The host refuses if no press is recorded or the button has already been released.
- **Start modes:** "press" starts the move on mousedown; "threshold" starts it once the pointer has moved 4 px with the button held, as GTK's own header bars do.

**Measured on X11** (XTEST; times from the XTEST press to the host's call, and to the `_NET_WM_MOVERESIZE` client message seen on the root):

| | WSLg (weston xwm), 15 runs | Docker, openbox, 6 runs |
|---|---|---|
| Search box inside the bar | focused, no move (all) | focused, no move (all) |
| Press → `begin_move`, threshold | GTK3 16.6–29.6 ms; GTK4 18.7–24.6 ms (one 16 ms motion step is included) | GTK3 16.3–25.5; GTK4 20.0–23.5 |
| Press → `begin_move`, press mode | 0.5–0.6 ms | 0.5–0.9 ms |
| WM request sent (`_NET_WM_MOVERESIZE`, direction 8, button 1, press coordinates) | yes, 0.3–2.2 ms after the call | yes |
| **Window moved** | **no**, in every run. The control drag on weston's own frame did not move it either (see below) | **yes, exactly 120,60, 6/6** (e.g. 280,160 → 400,220) |
| Double-click → maximize, then restore | threshold: 5–16 ms after the 2nd press. Press mode also worked (the WM never grabbed) | threshold: 1.9–5.2 ms. **Press mode: GTK3 worked; GTK4 got no state change.** The WM's grab from the first press takes the second press |
| Right-click → `show_window_menu` | returned 0 (weston xwm has no `_GTK_SHOW_WINDOW_MENU`) | returned 0 (openbox the same) |
| Maximize/restore from outside (`_NET_WM_STATE`) | the page saw the state in 7.1–13.8 ms | 7.1–11.2 ms |
| Page maximize button | works | works |
| Page minimize button | the window minimized (Windows-side `iconic=True`); GTK never reported minimized | not tested |
| Unminimize from the app (`present`, `deiconify`) | **no effect** (the `_NET_ACTIVE_WINDOW` request was ignored) | not tested |
| Close (`WM_DELETE_WINDOW`) | normal close path; web processes gone 12–24 ms after quit | same |
| Page-side resize edge (frame `none`, bottom-right) | `begin_resize` called, `_NET_WM_MOVERESIZE` direction 4 sent; size unchanged | **720×480 → 780×520** (+60,+40), both APIs |

The control on WSLg: an XTEST drag of weston's own server-side title bar (frame `native`) did not move the window either. XTEST input reaches XWayland, but weston starts a move only on its real seat's button. So WSLg cannot verify moves synthetically; the openbox runs do.

Also seen under WSLg: the first synthetic motion was sometimes replaced by the real pointer's position (the press landed at the screen centre). `xtool` now re-checks the pointer position before each press.

**Wayland (WSLg),** page buttons clicked from script, 4 runs:
- Maximize and restore work; `xdg_toplevel.set_maximized` and `unset_maximized` are sent, and the page sees the state.
- Minimize works (`set_minimized`; Windows-side iconic). The app then cannot unminimize: there is no `xdg_activation_v1`, and xdg-shell has no unminimize. The state reaches the page only as "not focused".
- Close goes through the normal path.
- **Not tested on Wayland:** the drag itself, double-click and the window menu (all need a real press, and there is no synthetic input on WSLg Wayland), and resize from the edges.

**Frames** (window size against 720×480 content; Windows-side rectangle; images in `artifacts/linux-integration/wsl/shots/frame-*.png`):

| Frame | Wayland GTK3 | Wayland GTK4 | X11 GTK3 | X11 GTK4 |
|---|---|---|---|---|
| `native` | CSD with a title bar (close button only), 26 px shadow | CSD with a header bar, shadow 14/12 px | weston's frame | weston's frame |
| `titlebar` (empty titlebar widget) | **CSD shadow and resize margins kept, no title bar** (772×532) | **same** (748×509), but the empty box **must be hidden**: a visible empty box got Adwaita's 46 px title-bar height | GTK falls back to "solid" CSD: a **5 px white border** (the theme's colour, not the app's) inside weston's undecorated frame | `csd` class, but no shadow or border (720×480) |
| `none` (`set_decorated(FALSE)`) | no GTK chrome, no shadow, so no resize edges (720×480) | same | no GTK chrome; weston still adds a 32 px invisible margin | same |

On X11, weston sets `_NET_FRAME_EXTENTS=38,38,59,38` for every client, decorated or not.

**Recommendation:**
- Use the empty titlebar widget (hidden on GTK4) on Wayland. It keeps the compositor-side shadow and GTK's own resize edges.
- Use `none` plus page-side edges only where CSD gives nothing (X11 without `_GTK_FRAME_EXTENTS`).
- Start the move in threshold mode.

**GTK4 issue found:** after a window-manager move or resize started by `gdk_toplevel_begin_move` or `begin_resize`, the **web processes did not exit within 10 s of the window being destroyed**. That was 4 children alive in 5/5 runs: openbox move ×2, openbox resize ×2, WSLg page-edge resize ×2. They exited when the host process ended.
- No drag: they exited in 19 ms.
- One page click after the drag: they exited in 19 ms.
- Waiting 3 s did not help.
- GTK3 with the same steps: 10 ms.

Hypothesis (not verified): GTK4 keeps the press's implicit grab or pointer focus, and with it a reference to the web view, because the window manager took the release.

### 2. Tray icon

`Tray.cs` implements the item directly on GDBus:
- it owns `org.kde.StatusNotifierItem-<pid>-1` and exports `/StatusNotifierItem` and `/MenuBar` (dbusmenu version 3: Restore, separator, Quit);
- it watches `org.kde.StatusNotifierWatcher` with `g_bus_watch_name`, and registers whenever a watcher appears (start or restart);
- the pixmap is a 16×16 ARGB circle (blue normal, yellow "busy", red NeedsAttention), and the icon name is one no theme has, so hosts use the pixmap.

**Fake watcher and host** (`wsl/fakesni.py`) in a private D-Bus session. 4 runs: GTK3 and GTK4 × Wayland and X11, all alike.

| Check | Result |
|---|---|
| No watcher | logged "tray unavailable", no crash |
| Registration after the watcher appeared | 0.4–0.8 ms |
| Properties (`GetAll`) | 0.6–0.8 ms: Id, Title, Status, IconName, IconPixmap (16×16, 1024 B), ToolTip (title and text), Menu `/MenuBar`, ItemIsMenu false |
| Status text update (tooltip and title) | `NewToolTip` and `NewTitle` received, and the new text was read back 2.5–4.6 ms later |
| `NewStatus('NeedsAttention')`, `NewIcon` | received; the pixmap checksum changed |
| `Activate` (left click) | handled; restored the hidden window |
| `ContextMenu` | answered; nothing to do (hosts render the dbusmenu) |
| `AboutToShow` and `GetLayout` | 0.2–0.4 ms each |
| `Event(clicked)` Restore and Quit | Restore presented the window. Quit went through the normal shutdown: web processes gone in 15–20 ms |
| Watcher restart | "unavailable" at once, then registered again 0.4–0.6 ms after the new watcher appeared |

**Real tray host, Docker, xfce4-panel 4.18.4 (Status Tray plugin) on Xvfb, X11.** Screenshots are in `artifacts/linux-integration/docker/`.

| Check | GTK3 | GTK4 |
|---|---|---|
| Icon shown (pixel check: 172 icon-colour pixels at 11,15) | yes (`gtk3-1-registered.png`) | yes |
| Tooltip on hover, then after a status text update | "Spar integration / Spar: idle", then "… Spar: copying 42%" (`gtk3-2`, `gtk4-3`) | same |
| Icon change (`NewIcon`) | pixmap repainted yellow (172 px) | same |
| NeedsAttention | red icon (172 px), shown after the panel restart | same |
| Left click | the panel called `Activate(11, 15)`; the hidden window was restored | same |
| Right click | the panel rendered the dbusmenu: Restore, separator, Quit (`*-6-menu.png`). It calls `GetLayout` with a property list, `GetGroupProperties`, `AboutToShowGroup` and **`EventGroup`**, not `Event` | same |
| Menu Restore and Quit (keyboard in the menu) | both work; Quit exited with web processes gone in 11 ms | same (10 ms) |
| Kill the panel and start it again | "unavailable", then registered 0.7 ms after the new watcher appeared; icon back. The panel took about 5.5–6 s to start under Xvfb | same |

So xfce4-panel sends `Activate` on a left click and shows the menu on a right click. A host must implement `EventGroup` and `AboutToShowGroup`, which this panel uses, not only `Event` and `AboutToShow`.

**Libraries:** `libayatana-appindicator3`, `libayatana-appindicator-glib`, `libappindicator3`, `libdbusmenu-glib` and `libdbusmenu-gtk3` are all **absent** in WSL (not installed). The GDBus implementation needs none of them.

**Desktops that would show the item** (from documentation; **not verified here** except XFCE):
- KDE Plasma implements StatusNotifierItem natively (the protocol comes from KDE).
- XFCE's panel hosts it (verified above with 4.18).
- GNOME Shell needs the "AppIndicator and KStatusNotifierItem Support" extension (extensions.gnome.org/extension/615). Ubuntu packages it as `gnome-shell-extension-appindicator` (version 57-2 in 24.04, per packages.ubuntu.com). Whether Ubuntu's desktop enables it by default was not checked.

### 3. Close to tray and restore

Close (page button, `WM_DELETE_WINDOW`, or `gtk_window_close`) runs the app's choice: with `--close-to-tray` it hides the window; otherwise it quits.

| | GTK3 Wayland | GTK3 X11 | GTK4 Wayland | GTK4 X11 | Docker (X11, both) |
|---|---|---|---|---|---|
| Hidden window on screen | gone | gone | **still on screen as a stale image** | gone | gone |
| `present` → page's first frame after `visibilitychange` | 18.4 ms (2 runs) | 18.7–19.6 ms | 19.9–20.5 ms | 20.4–21.4 ms | 18.2–25 ms |
| `visibilitychange` → frame | 15–16 ms | 16–17 | 16 | 18–19 | 16–22 |
| Page state (typed text) after restore | kept | kept | kept | kept | kept |
| Timers while hidden (100 ms interval) | 4 ticks in 3.0 s (throttled to about 1/s) | same | 5 in 3.2 s | 4 in 3.1 s | 2 in 0.8 s |
| Restored where | the compositor re-placed it (another position each time) | moved by 32 px | re-placed | moved by 32 px | same place |

The earlier "a hidden window never renders" applies to startup. A window hidden after its first frame comes back in about 20 ms.

**GTK4 on WSLg Wayland:**
- GTK4 hides by destroying the xdg role and attaching a null buffer, keeping the `wl_surface`. WSLg's RAIL shell then keeps the window on screen with its last frame (`hidden-gtk4-wayland.png`). GTK3 destroys the `wl_surface`, and the window disappears.
- **Workaround:** `gtk_widget_unrealize(window)` after hiding. The window disappeared on both backends, restore then took 38–40 ms to the first frame, and the page state was kept. But the re-realized window had no CSD shadow (720×480 instead of 748×509), so re-check the frame after a restore.
- Not checked on a real compositor, where null-buffer unmapping is the standard way to hide.

**Quit** from the tray menu goes through the normal shutdown: the main loop ends and the host waits for the web processes, which were gone in 10–20 ms in every run. No process was left (checked after each run).

### 4. Window placement

The test saved the size, the position and the maximized state on quit and restored them at the next start. The Windows-side rectangle is the ground truth.

| | GTK3 X11 | GTK4 X11 | GTK3 Wayland | GTK4 Wayland |
|---|---|---|---|---|
| Read the position | `gtk_window_get_position`: the frame position, which **does not round-trip**: restoring 262,141 put the window at 224,82 (one frame extent of drift per restart) | no GTK API; Xlib `XTranslateCoordinates` on the X window: exact | none (`get_position` returns 26,23, the shadow offset) | none |
| Set the position | `gtk_window_move` (with the drift above) | **`XMoveWindow` on the surface's XID after the first map: exact** (300,200 → 300,200) | ignored | no API |
| Size | restored (640×420) | restored (default size; GTK4 keeps the unmaximized size in it) | restored | restored |
| Maximized | restored; unmaximize returned to 640×420 | restored; unmaximize returned to 640×420 | restored, but the window **opened on the other monitor**. The spike saved the maximized size as the normal size, a host bug: track the normal size only from state events | **started maximized: the compositor killed the client (Wayland error 71)**, 2/2 |
| Position after the restart | as set (with drift) | as set | chosen by the compositor | chosen by the compositor |

The GTK4 error was `xdg_surface buffer (640 x 420) does not match the configured maximized state (0 x 0)`. WSLg's weston sent an initial configure of 0×0 without the maximized state, then treated the window as maximized.

**Workaround:** maximize after the first map. That worked 3/3, and unmaximize returned to 640×420.

### 5. File pickers

The test opened an Open dialog, cancelled it from code after 700 ms, watched the D-Bus traffic with `dbus-monitor` in a private session, and checked the Windows side for a window titled "Spar picker probe".

| | GTK3 `GtkFileChooserNative` | GTK4 `GtkFileDialog` |
|---|---|---|
| No `portals.conf`, empty `XDG_CURRENT_DESKTOP` | in-process GTK dialog, **no portal call** | **portal**: `OpenFile` (gtk.portal chosen as a "last-resort fallback"), then `Request.Close` on cancel |
| Private `portals.conf` (`default=gtk`), `XDG_CURRENT_DESKTOP=GNOME` | in-process dialog, no portal | portal |
| `GTK_USE_PORTAL=1` (GTK3) / `GDK_DEBUG=portals` (GTK4) | portal (`OpenFile`, `Close`) | portal |
| Cancel | `gtk_native_dialog_hide`: the dialog was gone at the next check; GTK3 emits no `response` on hide | `g_cancellable_cancel`: the callback ran at once with "Cancelled by application" (707–710 ms after open) |
| X11 backend | same as Wayland | same |

The dialog was a 1172–1279 × 940–1001 window on the user's desktop while open. It was always gone after the cancel.

On GTK3, the portal is used only inside a sandbox or with `GTK_USE_PORTAL=1`. A host that wants the portal everywhere on GTK3 must set it, or call the portal itself.

### 6. Clipboard

Tested only in Docker on X11. WSLg syncs the Wayland and X11 clipboards with the Windows clipboard, so testing there would have overwritten the user's clipboard. All runs used the strict CSP, with trusted clicks (XTEST) on the page's Copy and Paste buttons.

| | GTK3 | GTK4 |
|---|---|---|
| Host set, then host read (`gtk_clipboard_*` / `gdk_clipboard_*`) | round trip in 0.1–0.2 ms | 0.0–0.1 ms |
| Page `writeText` (with a gesture) | allowed; **no permission request**; the host read it back | same |
| Page `readText` of text set by the host (another "origin"), default | `permission-request` with **`WebKitClipboardPermissionRequest`**; the host denied it, and the page got `NotAllowedError` | same |
| …with the host allowing that request | allowed | same |
| …with `javascript-can-access-clipboard` set | allowed with **no permission request** (the gate is bypassed) | same |
| Page `readText` of text the page itself wrote | allowed, even from an untrusted scripted click | same |

So the permission gate sees exactly one request type, for cross-origin reads. A strict default (deny) still allows copying and same-page paste. `javascript-can-access-clipboard` must stay off by default, because it removes the gate. Wayland is untested.

### 7. Single instance

Both mechanisms ran in a private D-Bus session with the GTK4 build; GApplication was also run with the GTK3 build, with the same numbers. Neither depends on the API.

| Case | GApplication (D-Bus uniqueness, as GtkApplication) | Lock file (`flock`) + Unix socket in `$XDG_RUNTIME_DIR` (0600) |
|---|---|---|
| Normal: secondary started → primary got the arguments | 5.9–6.8 ms; the secondary exited after 14–16 ms with the primary's status | 3.8–9.6 ms; the secondary exited after 12–17 ms |
| First instance hung (`SIGSTOP`) | **the secondary blocked until killed** (8 s timeout, exit 124). GApplication waits with no timeout | the secondary gave up after its 2 s timeout (exit 4, "not responding", no second primary). The stopped primary handled the queued arguments when resumed (2.0 s later), even though the secondary had reported failure |
| First instance exiting (name or lock kept for 3 s after it stopped serving) | the secondary failed after 2.6 s with `NoReply: recipient disconnected`; **the launch was lost** | connect refused (no socket); the secondary waited for the lock and **became the primary** 2.6 s later, with its own arguments |

Recommendation: the lock plus socket, with a bounded acknowledgement. Remove the socket before releasing the lock. GApplication is fine for the normal case, but has no timeout and loses launches during shutdown.

### 8. Printing

`webkit_print_operation_print` with `GtkPrintSettings`, with printer "Print to File", `output-file-format` pdf, and `output-uri` set to a `file://` URI. No dialog appeared.

| | GTK3 | GTK4 |
|---|---|---|
| `finished` after | 82 ms | 82 ms |
| Output | 1,713,240 bytes, `%PDF-1.4`, 1 page, A4 (595×842 pt), producer Skia/PDF m145 | same |

The file was added to `~/.local/share/recently-used.xbel` (GTK's recent files).

### 9. Presence (nothing visible was triggered)

| | User session (WSLg; read-only, no auto-start) | Private session with `portals.conf` |
|---|---|---|
| `org.freedesktop.Notifications` | not owned, **not activatable**: no notification server | same |
| `org.kde.StatusNotifierWatcher` | none (so the tray reports "unavailable") | none |
| `com.canonical.Unity`, `org.gnome.Shell` | none | none |
| xdg portal | activatable, not running until something activates it | running. Notification **v1**, FileChooser v3, Settings v2, Inhibit v3, OpenURI v3, Print v2, Account v1. **No GlobalShortcuts**, no Background, no Secret |
| `com.canonical.Unity.LauncherEntry.Update` (progress 0.42, count 3) | not emitted there | emitted by the host, and seen by a listener (1 signal, with `progress` and `count`) |

No notification was sent, and no capability call went to a notification server (none exists).

## Problems met

1. **Wrong GVariant text:** a one-element tuple needs a trailing comma (`('x',)`). The exception escaped an `[UnmanagedCallersOnly]` callback and the host died silently. Fixed; every GDBus callback now catches.
2. **FIFO writes blocked** forever when the host had died. Writes now go through `timeout 2`.
3. **At-spi daemons started by a private D-Bus session outlived it** (1 seen). Scripts now stop only processes that were not there before. `NO_AT_BRIDGE=1` did not stop portal-gtk from starting at-spi.
4. **Background `run … &` gave the subshell's PID,** so `SIGSTOP` hit bash. Fixed with `exec`.
5. **The socket primary aborted** writing the acknowledgement to a secondary that had already given up (`SocketException`). Fixed: caught and logged.
6. **XWayland sometimes replaced the first synthetic motion with the real pointer position.** `xtool` checks and retries.
7. **Git Bash `sed` mangles multi-line inserts.** Edits were redone with an editor.
8. **Docker output:** `docker cp` of the run folders copied only one folder before the container was removed. The openbox title-bar numbers and the follow-up web-process runs are kept as recorded console output (`docker/titlebar-openbox-summary.txt`); the screenshots were copied earlier and are complete.
9. **Writes outside the private folders (constraint breach, to undo):**
   - The file-picker runs without a private `XDG_CONFIG_HOME` let GTK3's in-process dialog save its settings through dconf, creating `~/.config/dconf/user` and empty `~/.config/gtk-3.0` and `~/.config/gtk-4.0` in WSL.
   - Printing added `~/.local/share/recently-used.xbel`.
   - `dotnet restore` and the portal created `~/.nuget/NuGet/NuGet.Config`, `~/.local/share/NuGet` and `~/.local/share/flatpak`.

   All of these folders were created during this session; `~/.config`, `~/.local` and `~/.nuget` did not exist before it, according to their birth times. Deleting them, and the 583 MB working copy `~/spar-linux-integration`, was **refused by the session's permission check**, so they are still there.

## Observed, skipped, not tested

**Observed:**
- Frame matrix: 12 runs with Windows-side captures.
- X11 title bar under WSLg: 15 runs (two APIs; frames `titlebar`, `none`, `native`; press, threshold and CSS-region modes).
- Wayland title bar: 4 runs.
- Openbox title bar: 6 runs, plus 6 follow-up runs on web-process exit.
- Tray with the fake watcher: 4 runs. Real xfce4-panel: 2 runs.
- Close to tray: 4 WSLg runs, 2 Docker runs, and 4 hide/unrealize runs.
- Placement: 12 runs, plus 3 with the deferred maximize.
- Pickers: 8 runs. Clipboard: 6 runs. Single instance: 3 sets. Print: 2. Presence: 2 sessions.
- No process was left after any run, except the at-spi daemon noted above, which was stopped.

**Skipped** because of the rules:
- Synthetic input on Wayland, so the Wayland drag, double-click, window menu and edge resize are untested.
- The clipboard under WSLg (it would overwrite the user's Windows clipboard).
- Sending a desktop notification.
- Installing libayatana.
- Any change to WSL or Windows settings.

**Not tested:**
- **Question 10:** dropping files onto the page, and how the host learns their paths.
- A real Wayland desktop (GNOME/Mutter, KDE/KWin): Wayland moves through `xdg_toplevel.move`, window menus, hiding with a null buffer, starting maximized, `xdg_activation` for restoring from minimize.
- GNOME with the AppIndicator extension; KDE's tray.
- HiDPI.
- Save dialogs.
- Printing with a real printer or the print portal.
- The tray's `Scroll` and `SecondaryActivate` from a real panel.
- The GTK4 web-process issue after a window-manager move or resize: its cause and a fix.
- Restoring several monitors' positions.
- How long GTK3's position drift continues over many restarts.

## Leftovers

- **Docker:** container `spar-linux-integration-tray` removed. No image or volume was added. The earlier image `spar-linux-desktop:webkit2.52` and volumes `spar-linux-desktop-nuget` and `spar-linux-nuget` were left alone.
- **Downloads:** 33.9 MB of apt lists and 20.8 MB of packages, inside the container only, now gone with it. NuGet was restored offline. Two pages were read from packages.ubuntu.com and extensions.gnome.org.
- **WSL:** the working copy and the session-created folders listed in problem 9 remain; the user needs to delete them. No processes from this spike are running.
- **Disposable outputs** in `artifacts/linux-integration/`:
  - `wsl/` (run logs, frame and hide screenshots, `titlebar-x11-summary.txt`, the placement JSON files, the PDFs, the launcher logs);
  - `docker/` (panel screenshots `gtk3-*.png` and `gtk4-*.png`, `titlebar-openbox-summary.txt`, one clipboard run folder).
