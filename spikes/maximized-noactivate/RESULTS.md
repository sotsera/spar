# Spike: a maximized window without activation

Run 2026-11-26 on Windows 11 Pro 10.0.26320, SDK 11.0.100, one 1920 × 1080 monitor at 100 %, a work area of 1920 × 1032. Question (after decision "A restarted maximized window stays unmaximized", asked by the user): can a window that `spar dev` restarts come back maximized without taking the focus, which `SW_SHOWMAXIMIZED` takes?

`Probe.cs` (`dotnet run Probe.cs`) shows one top-level window per mode on a desktop of the process's own (`CreateDesktop`, `SetThreadDesktop`, never switched to, so nothing reached the monitor), pumps for 600 ms, and reads `IsZoomed`, the `WM_ACTIVATE` messages with `WA_ACTIVE` or `WA_CLICKACTIVE`, `GetActiveWindow` and the window's rectangle; for the last two modes it then sends `SC_RESTORE`, as the title bar's button does. The raw output is `results/private-desktop.txt`.

| Mode | Maximized | Activated | Rectangle |
|---|---|---|---|
| `SW_SHOWMAXIMIZED` (control) | yes | yes | (-8,-8)-(1928,1040) |
| `SW_SHOWNOACTIVATE` (control) | no | no | the normal place |
| `WS_MAXIMIZE` at creation with a place given, then `SW_SHOWNOACTIVATE` | no | no | the normal place (the style is ignored) |
| `SetWindowPlacement` with `SW_SHOWMAXIMIZED` before the first show | yes | yes | maximized |
| `SW_SHOWNOACTIVATE`, then `SW_MAXIMIZE` | yes | yes | maximized |
| `SW_SHOWNOACTIVATE`, then `SetWindowPos` to the work area | no | no | the work area (the look, not the state) |
| `WS_MAXIMIZE` set while hidden, sized to the work area plus the frame, `SW_SHOWNA` | yes | no | (-8,-8)-(1928,1040); Restore keeps that rectangle |
| `SetWindowPlacement` with the normal place and `SW_HIDE`, then as above | **yes** | **no** | (-8,-8)-(1928,1040); **Restore goes back to the normal place** |

**Result:** the last mode gives a window that Windows itself treats as maximized (`IsZoomed`, the Restore button, the same rectangle as a real maximize), shown without activation, whose Restore returns to the place recorded before. Every other way to maximize activates the window.

**Not verified here:** activation on the user's own desktop (the probe's desktop has no input and no foreground window of its own), a second monitor, other scales (the frame is `SM_CXSIZEFRAME` + `SM_CXPADDEDBORDER` at 96 DPI here), a window with a page-drawn title bar. The check at the PC covers the first.
