# Spike: files dropped from the OS, and the Linux file dialog

> A spike. Its code was deleted when M3c's choices were accepted; it is in git history at [`spikes/file-drop/`](https://github.com/sotsera/spar/tree/1aa99dca19ce298ba23005f346e1b7056336d1ce/spikes/file-drop) (commit `1aa99dc`), and this record and its `results/` moved here. Paths under `spikes/file-drop/` below name that commit's files.

Run 2026-11-27 (decision "M3c's decide-first questions", item 1). Questions: how do files dropped on the page reach each host with their paths, how does the host learn which area of the page they were dropped on, and what does GTK's file dialog do when no file-chooser portal answers?

Machines:

- **Windows:** the development PC, Windows 11 Pro 10.0.26320, WebView2 Runtime 158.0.4370.45, SDK 1.0.4314.35.
- **Linux:** Spar's Linux CI image (`spar-linux-ci:eval`: Ubuntu 24.04, GTK 4.14.5, WebKitGTK 2.52 `libwebkitgtk-6.0.so.4.16.10`), on a private Xvfb, with the core limit at 1 byte.

No window reached the monitor. Raw output is in `results/`.

## Windows (`Windows/`)

`dotnet run --project spikes/file-drop/Windows` builds a WinForms window with the WebView2 SDK's managed layer, shown offscreen without activation, with its profile under `artifacts/webview2-profiles/file-drop-spike`. The page has two halves: the left one takes drops, the right one has no handler. Drops are made with the in-process DevTools protocol, `Input.dispatchDragEvent` with `data.files`, as dragEnter, dragOver, then drop.

The page's `drop` handler calls `chrome.webview.postMessageWithAdditionalObjects("files", e.dataTransfer.files)` synchronously, because the DataTransfer is empty once the event ends. The host reads `CoreWebView2WebMessageReceivedEventArgs.AdditionalObjects`.

| Drop | The page sees | The host receives |
|---|---|---|
| one file | `types` `["Files"]`, its name, size and type; `path` undefined; `text/uri-list` empty | one `CoreWebView2File`, its full path |
| a name with `ü`, `文件` | the name | the path, intact |
| a file and a folder | two Files (the folder with size 0 and type `""`), `webkitGetAsEntry().isDirectory` true for the folder, `getAsFileSystemHandle()` kind `directory`, its 3 children readable | two `CoreWebView2File`s, the folder's path among them |
| a 50 MB file | its size | its path (nothing is read) |
| 1,000 files | 1,000 Files | 1,000 paths, all on disk, 205.6 ms after the drop (the page's handler took 110.5 ms of it) |
| the right half (no handler) | no event | no message; `NewWindowRequested` for `file:///…/a.txt`, user-initiated (no navigation) |

- **One file, drop to path on the host:** 0.48–0.60 ms, median 0.56 ms (10 drops after a first one left out).
- **The page cannot invent a file.** `postMessageWithAdditionalObjects` with a `new File([...], "C:\\Windows\\win.ini")` throws in the page: "additional File object is not a file on the disk". Nothing reaches the host. Only files the user gave the page (a drop, or `<input type="file">`) carry a path.
- **Directory handles arrive as `null`.** With `getAsFileSystemHandle()`, the posted `FileSystemHandle`s arrived as `null` items in the managed layer. The folder's `File` already carries its path, so handles are not needed.
- **CDP is not the OLE path.** With `AllowExternalDrop = false`, a CDP drop still arrived. CDP injects into the renderer and skips the window's OLE drop target, so these numbers say nothing about the OS's own drag (see "Not verified").

## Linux (`Linux/`)

`FileDrop.Linux <mode> <results> <fixtures>` builds a GTK 4 window with a WebKitGTK view and the same two halves, with its messages on a script message handler. The drop comes from a drag source of the probe's own on a second X connection, `XdndSource.cs`. It speaks XDND version 5 as a file manager does:

- it sends Enter, then a Position every 16 ms for 400 ms, then Drop;
- it answers the target's selection requests for `text/uri-list` (`file://` URIs).

No pointer moves. An early version dropped 2 ms after entering, which WebKit accepted only one time in two; hovering for 400 ms as a pointer does made every drop succeed.

Modes:

- **`page`:** WebKit's own handling only.
- **`motion`:** adds a `GtkDropControllerMotion` on the view, which reads the drop's `GdkFileList` itself on `enter` (`gtk_drop_controller_motion_get_drop`, `gdk_drop_read_value_async`) while WebKit handles the drop.
- **`target`:** adds a `GtkDropTarget` for `GdkFileList` on the view, in the capture phase.
- **`target-pass`:** the same target, declining drops on the right half.

Every mode also has a relay in an isolated script world, as Spar's bridge relay has (`BridgeRelay.js`). Its capture listener for `drop` on `window` posts `isTrusted`, the position and the element's area to a handler that exists only in that world. At load, the page dispatches a `DragEvent("drop")` of its own.

| Mode | The page's drop event | The host's paths | Other |
|---|---|---|---|
| `page` | fires on the area, with `dragenter`/`dragover` | none | **no Files**: `files` empty, `types` `["text/uri-list","text/html"]`, `getData("text/uri-list")` `""` |
| `page`, sandbox off | the same, no Files | none | `WEBKIT_DISABLE_SANDBOX_THIS_IS_DANGEROUS=1`: no change |
| `motion` | fires, as in `page` | all of them (1 to 1,000, the folder's included), read about 1 ms after enter, about 410 ms before the drop | two reads of the selection: WebKit's and the host's |
| `target` | **never fires**; WebKit saw `dragenter` only before the first drop, never after | all of them, with the position, 1.2–1.3 ms after the drop | taking the drop leaves WebKit's drag state stuck |
| `target-pass` | the declined drop does not reach WebKit either | — | a declined drop fails (`XdndFinished` success 0) |

- **The isolated world can tell a real drop.** In every mode with a page event, the relay saw each real drop with `isTrusted: true`, its position and its area, and the page's own `DragEvent` with `isTrusted: false`. One file, drop to page: median 0.87 ms.
- **No Files reach the page.** This is a known WebKitGTK defect: [bug 320301](https://bugs.webkit.org/show_bug.cgi?id=320301) (2.52.5: `DataTransfer::allowsFileAccess()` is false on every non-Cocoa port) and [bug 323277](https://bugs.webkit.org/show_bug.cgi?id=323277) (a regression; the fix is still a pull request). A page on WebKitGTK cannot read a dropped file itself.
- **The area without a handler:** WebKit asks to navigate the page to `file:///…/a.txt` (`decide-policy`, navigation action, not a new window). The probe ignored it.

### The file dialog without a portal (`dialog`, `dialog-nobus`)

The image has no `xdg-desktop-portal`. `gtk_file_dialog_open` on the window, under a session bus of its own (`dbus-run-session`) and with no session bus at all, **opened GTK's in-process chooser** in both cases. A toplevel titled "Pick a File" was open at 1.5 s, and the dialog ended only when the probe cancelled it ("Cancelled by application", 1.5 s). GTK 4.14 uses the portal when one answers and falls back to its own chooser otherwise; it never reports that no portal answered.

## What this settles for the design

- **Windows:** the page's drop handler hands the files to the host with `postMessageWithAdditionalObjects` inside the event. The browser guarantees each one is a file on disk that the user gave the page, so the page names the area and the host makes the grants.
- **Linux:**
  - A motion controller on the view reads the drag's file list at enter, and WebKit keeps its own drag handling: hover feedback, the page's `drop` event and the area.
  - The relay's trusted `drop` (its own world, `isTrusted`) is the proof that the user dropped. The host pairs it with the drag's file list, once per drag.
  - A drop target of the host's own would take the drop from WebKit and break its drag state.
- **The page cannot read dropped files on WebKitGTK 2.52,** so the page reads them through grants on both hosts. This includes the Notebook's image dropped into the editor, which works today only with in-page events.
- **The Linux file dialog has to check for the portal itself:** a call to `org.freedesktop.portal.FileChooser` on the session bus, off the loop. Otherwise GTK falls back to the in-process chooser that the decision did not take.
- **A drop outside an area** asks to open a new window on WebView2 and to navigate on WebKitGTK. Both must stay refused by the navigation lock (to be verified on Spar's own hosts).

## Not verified

- A drag from Explorer or a file manager, through the OS's own drag: on Windows, CDP skips OLE (needs real input, so Windows Sandbox); on Linux, the source is the probe's own XDND client, not a file manager. Wayland was not run (WebKit there gets the same `GdkDrop`, but it was not measured).
- The file dialog with a portal present, in the image or under WSLg.
- Directory handles through Spar's own COM declarations (the managed layer gave `null`).
