# ubuntu-nodri

## series.jsonl

| Method | Kind | n | Detect ms median (range) | Process wall ms median | RSS growth MB median | Verdict | Renderer / reason |
|---|---|---|---|---|---|---|---|
| `none` | cold | 3 | 0.00 (0.00–0.00) | 2.59 (1.97–2.90) | 0.0 | unknown |  |
| `node` | cold | 3 | 0.04 (0.04–0.04) | 2.89 (2.74–3.12) | 0.0 | software | no render node |
| `combined` | cold | 3 | 0.04 (0.04–0.04) | 2.78 (2.73–2.87) | 0.0 | software | no openable render node; EGL skipped |
| `devices` | cold | 3 | 63.12 (62.01–63.38) | 68.33 (67.49–68.86) | 54.0 | unknown |  |
| `egl --platform surfaceless --no-context` | cold | 3 | 65.40 (65.25–67.58) | 70.55 (70.33–73.02) | 55.7 | software | driver name 'swrast'; EGL driver swrast |
| `egl --platform surfaceless` | cold | 3 | 77.23 (30.79–87.87) | 81.96 (33.71–92.09) | 73.6 | software | llvmpipe (LLVM 20.1.2, 256 bits); EGL driver swrast |
| `egl --platform device` | cold | 3 | 31.91 (31.51–31.96) | 34.23 (33.88–34.34) | 66.9 | software | llvmpipe (LLVM 20.1.2, 256 bits); EGL driver swrast |
| `egl --platform default` | cold | 3 | 21.49 (18.81–63.16) | 24.35 (22.47–68.50) | 48.3 | software | no GL renderer: eglInitialize failed (0x3001); error: eglInitialize failed (0x3001) |
| `none` | warm | 25 | 0.00 (0.00–0.00) | 1.18 (1.10–1.71) | 0.0 | unknown |  |
| `node` | warm | 25 | 0.04 (0.03–0.09) | 1.09 (1.00–1.37) | 0.0 | software | no render node |
| `combined` | warm | 25 | 0.03 (0.03–0.07) | 1.05 (0.98–1.27) | 0.0 | software | no openable render node; EGL skipped |
| `devices` | warm | 25 | 8.36 (7.77–9.75) | 10.45 (9.66–11.92) | 54.4 | unknown |  |
| `egl --platform surfaceless --no-context` | warm | 25 | 9.47 (8.82–11.33) | 11.71 (10.75–14.05) | 56.0 | software | driver name 'swrast'; EGL driver swrast |
| `egl --platform surfaceless` | warm | 25 | 19.03 (17.73–22.98) | 20.59 (19.14–24.63) | 73.6 | software | llvmpipe (LLVM 20.1.2, 256 bits); EGL driver swrast |
| `egl --platform device` | warm | 25 | 19.20 (17.80–22.97) | 20.67 (19.15–24.58) | 73.6 | software | llvmpipe (LLVM 20.1.2, 256 bits); EGL driver swrast |
| `egl --platform default` | warm | 25 | 8.43 (7.83–9.96) | 10.87 (10.03–12.25) | 54.3 | software | no GL renderer: eglInitialize failed (0x3001); error: eglInitialize failed (0x3001) |

Warm phase medians (ms):

- `node`: node 0.04;
- `combined`: node 0.03;
- `devices`: load 0.17; enumerate 8.16;
- `egl --platform surfaceless --no-context`: load 0.19; get-display 8.20; initialize 1.07; query-driver 0.01; teardown 0.02;
- `egl --platform surfaceless`: load 0.19; get-display 8.09; initialize 1.07; query-driver 0.01; context-and-gl-strings 8.48; teardown 1.04;
- `egl --platform device`: load 0.19; get-display 8.22; initialize 1.01; query-driver 0.01; context-and-gl-strings 8.70; teardown 1.03;
- `egl --platform default`: load 0.19; get-display 8.24;

## gdk.jsonl

| Method | Kind | n | Detect ms median (range) | Process wall ms median | RSS growth MB median | Verdict | Renderer / reason |
|---|---|---|---|---|---|---|---|
| `gdk` | cold | 3 | 92.65 (90.01–106.82) | 99.42 (96.34–114.58) | 95.4 | software | llvmpipe (LLVM 20.1.2, 256 bits) |
| `gdk --window` | cold | 3 | 153.03 (148.60–418.32) | 166.11 (158.87–429.33) | 155.2 | software | llvmpipe (LLVM 20.1.2, 256 bits); window: GskGLRenderer |
| `gdk --window --gsk-after-init cairo` | cold | 3 | 94.65 (93.40–97.80) | 101.76 (100.88–105.83) | 100.2 | software | llvmpipe (LLVM 20.1.2, 256 bits); window: GskCairoRenderer |
| `gdk --pre-egl --window` | cold | 3 | 156.97 (155.79–170.26) | 167.68 (165.51–181.67) | 160.5 | software | llvmpipe (LLVM 20.1.2, 256 bits); window: GskGLRenderer |
| `gdk` | warm | 25 | 38.84 (33.62–44.27) | 41.31 (36.22–46.88) | 95.9 | software | llvmpipe (LLVM 20.1.2, 256 bits) |
| `gdk --window` | warm | 25 | 99.39 (95.32–111.53) | 105.40 (100.45–117.69) | 155.6 | software | llvmpipe (LLVM 20.1.2, 256 bits); window: GskGLRenderer |
| `gdk --window --gsk-after-init cairo` | warm | 25 | 46.06 (42.97–52.03) | 48.37 (45.22–54.58) | 100.7 | software | llvmpipe (LLVM 20.1.2, 256 bits); window: GskCairoRenderer |
| `gdk --pre-egl --window` | warm | 25 | 107.44 (102.05–120.53) | 114.21 (108.05–127.03) | 161.6 | software | llvmpipe (LLVM 20.1.2, 256 bits); window: GskGLRenderer |

Warm phase medians (ms):

- `gdk`: gtk-init 32.33; gl-context-and-strings 6.43;
- `gdk --window`: gtk-init 31.76; gl-context-and-strings 6.32; window-realize 61.44;
- `gdk --window --gsk-after-init cairo`: gtk-init 31.76; gl-context-and-strings 6.25; window-realize 7.83;
- `gdk --pre-egl --window`: load 0.20; get-display 8.78; initialize 1.11; query-driver 0.01; context-and-gl-strings 8.77; teardown 1.12; pre-egl 0.00; gtk-init 15.89; gl-context-and-strings 6.18; window-realize 64.71;

## connect-src

### none-1.json: profile `none`, WebKitGTK 2.52.6

origin `spar://app`, secure context True, trustedTypes True

| Probe | Outcome | Violation (blockedURI, effectiveDirective) |
|---|---|---|
| fetch relative data.json | ok 200 {"from": "spar://app/data.json"} | none |
| fetch absolute spar://app/data.json | ok 200 {"from": "spar://app/data.json"} | none |
| fetch spar://other/data.json | rejected TypeError: Load failed | none |
| xhr relative data.json | load 200 {"from": "spar://app/data.json"} | none |
| xhr absolute spar://app/data.json | load 200 {"from": "spar://app/data.json"} | none |
| websocket ws://127.0.0.1 | error event, readyState 3 | none |
| websocket wss://127.0.0.1 | error event, readyState 3 | none |
| websocket ws://localhost | error event, readyState 3 | none |
| websocket ws://app | error event, readyState 3 | none |
| websocket wss://app | error event, readyState 3 | none |
| websocket ws://app:port | error event, readyState 3 | none |
| websocket wss://app:port | error event, readyState 3 | none |
| fetch https://app:port | rejected TypeError: Load failed | none |
| fetch http://127.0.0.1 | ok 200 loopback | none |
| fetch http://localhost | ok 200 loopback | none |
| xhr http://127.0.0.1 | load 200 loopback | none |
| eventsource spar://app/events | message scheme | none |
| eventsource relative events | message scheme | none |
| eventsource http://127.0.0.1 | message loopback | none |
| sendBeacon spar://app/beacon | threw TypeError: Beacons can only be sent over HTTP(S) | none |
| sendBeacon relative beacon | threw TypeError: Beacons can only be sent over HTTP(S) | none |
| sendBeacon http://127.0.0.1 | returned true | none |
| worker fetch relative data.json | ok 200 {"from": "spar://app/data.json"} | none |
| worker fetch absolute spar://app/data.json | ok 200 {"from": "spar://app/data.json"} | none |
| worker xhr relative data.json | load 200 {"from": "spar://app/data.json"} | none |
| worker fetch http://127.0.0.1 | ok 200 loopback | none |
| worker xhr http://127.0.0.1 | load 200 loopback | none |
| worker websocket ws://127.0.0.1 | error event, readyState 3 | none |

Loopback connections per listener: ws 1, wss 1, wsLocalhost 1, wsApp 1, wssApp 1, httpsApp 1, fetch 1, fetchLocalhost 1, xhr 1, es 1, beacon 1, wfetch 1, wws 1, wxhr 1
Scheme requests (other than scripts and the page): GET spar://app/data.json, GET spar://app/data.json, GET spar://other/data.json, GET spar://app/data.json, GET spar://app/data.json, GET spar://app/events, GET spar://app/events, GET spar://app/data.json, GET spar://app/data.json, GET spar://app/data.json
CSP reports received: 0

### none-2.json: profile `none`, WebKitGTK 2.52.6

origin `spar://app`, secure context True, trustedTypes True

| Probe | Outcome | Violation (blockedURI, effectiveDirective) |
|---|---|---|
| fetch relative data.json | ok 200 {"from": "spar://app/data.json"} | none |
| fetch absolute spar://app/data.json | ok 200 {"from": "spar://app/data.json"} | none |
| fetch spar://other/data.json | rejected TypeError: Load failed | none |
| xhr relative data.json | load 200 {"from": "spar://app/data.json"} | none |
| xhr absolute spar://app/data.json | load 200 {"from": "spar://app/data.json"} | none |
| websocket ws://127.0.0.1 | error event, readyState 3 | none |
| websocket wss://127.0.0.1 | error event, readyState 3 | none |
| websocket ws://localhost | error event, readyState 3 | none |
| websocket ws://app | error event, readyState 3 | none |
| websocket wss://app | error event, readyState 3 | none |
| websocket ws://app:port | error event, readyState 3 | none |
| websocket wss://app:port | error event, readyState 3 | none |
| fetch https://app:port | rejected TypeError: Load failed | none |
| fetch http://127.0.0.1 | ok 200 loopback | none |
| fetch http://localhost | ok 200 loopback | none |
| xhr http://127.0.0.1 | load 200 loopback | none |
| eventsource spar://app/events | message scheme | none |
| eventsource relative events | message scheme | none |
| eventsource http://127.0.0.1 | message loopback | none |
| sendBeacon spar://app/beacon | threw TypeError: Beacons can only be sent over HTTP(S) | none |
| sendBeacon relative beacon | threw TypeError: Beacons can only be sent over HTTP(S) | none |
| sendBeacon http://127.0.0.1 | returned true | none |
| worker fetch relative data.json | ok 200 {"from": "spar://app/data.json"} | none |
| worker fetch absolute spar://app/data.json | ok 200 {"from": "spar://app/data.json"} | none |
| worker xhr relative data.json | load 200 {"from": "spar://app/data.json"} | none |
| worker fetch http://127.0.0.1 | ok 200 loopback | none |
| worker xhr http://127.0.0.1 | load 200 loopback | none |
| worker websocket ws://127.0.0.1 | error event, readyState 3 | none |

Loopback connections per listener: ws 1, wss 1, wsLocalhost 1, wsApp 1, wssApp 1, httpsApp 1, fetch 1, fetchLocalhost 1, xhr 1, es 1, beacon 1, wfetch 1, wws 1, wxhr 1
Scheme requests (other than scripts and the page): GET spar://app/data.json, GET spar://app/data.json, GET spar://other/data.json, GET spar://app/data.json, GET spar://app/data.json, GET spar://app/events, GET spar://app/events, GET spar://app/data.json, GET spar://app/data.json, GET spar://app/data.json
CSP reports received: 0

### self-1.json: profile `self`, WebKitGTK 2.52.6

origin `spar://app`, secure context True, trustedTypes True

| Probe | Outcome | Violation (blockedURI, effectiveDirective) |
|---|---|---|
| fetch relative data.json | ok 200 {"from": "spar://app/data.json"} | none |
| fetch absolute spar://app/data.json | ok 200 {"from": "spar://app/data.json"} | none |
| fetch spar://other/data.json | rejected TypeError: Load failed | `spar://other/data.json` connect-src |
| xhr relative data.json | load 200 {"from": "spar://app/data.json"} | none |
| xhr absolute spar://app/data.json | load 200 {"from": "spar://app/data.json"} | none |
| websocket ws://127.0.0.1 | threw SecurityError: The operation is insecure. | `ws://127.0.0.1:42895/` connect-src |
| websocket wss://127.0.0.1 | threw SecurityError: The operation is insecure. | `wss://127.0.0.1:35189/` connect-src |
| websocket ws://localhost | threw SecurityError: The operation is insecure. | `ws://localhost:34431/` connect-src |
| websocket ws://app | threw SecurityError: The operation is insecure. | `ws://app/` connect-src |
| websocket wss://app | error event, readyState 3 | none |
| websocket ws://app:port | threw SecurityError: The operation is insecure. | `ws://app:35639/` connect-src |
| websocket wss://app:port | threw SecurityError: The operation is insecure. | `wss://app:45385/` connect-src |
| fetch https://app:port | rejected TypeError: Load failed | `https://app:44887/` connect-src |
| fetch http://127.0.0.1 | rejected TypeError: Load failed | `http://127.0.0.1:45189/` connect-src |
| fetch http://localhost | rejected TypeError: Load failed | `http://localhost:35495/` connect-src |
| xhr http://127.0.0.1 | error event, status 0 | `http://127.0.0.1:40725/` connect-src |
| eventsource spar://app/events | message scheme | none |
| eventsource relative events | message scheme | none |
| eventsource http://127.0.0.1 | threw SecurityError: The operation is insecure. | `http://127.0.0.1:39799/` connect-src |
| sendBeacon spar://app/beacon | threw TypeError: Beacons can only be sent over HTTP(S) | none |
| sendBeacon relative beacon | threw TypeError: Beacons can only be sent over HTTP(S) | none |
| sendBeacon http://127.0.0.1 | returned true | `http://127.0.0.1:44575/` connect-src |
| worker fetch relative data.json | ok 200 {"from": "spar://app/data.json"} | none |
| worker fetch absolute spar://app/data.json | ok 200 {"from": "spar://app/data.json"} | none |
| worker xhr relative data.json | load 200 {"from": "spar://app/data.json"} | none |
| worker fetch http://127.0.0.1 | rejected TypeError: Load failed | none |
| worker xhr http://127.0.0.1 | error event, status 0 | none |
| worker websocket ws://127.0.0.1 | threw SecurityError: The operation is insecure. | none |

Loopback connections per listener: ws 0, wss 0, wsLocalhost 0, wsApp 0, wssApp 0, httpsApp 0, fetch 0, fetchLocalhost 0, xhr 0, es 0, beacon 0, wfetch 0, wws 0, wxhr 0
Scheme requests (other than scripts and the page): GET spar://app/data.json, GET spar://app/data.json, POST spar://app/_spar/csp-report, GET spar://app/data.json, GET spar://app/data.json, POST spar://app/_spar/csp-report, POST spar://app/_spar/csp-report, POST spar://app/_spar/csp-report, POST spar://app/_spar/csp-report, POST spar://app/_spar/csp-report, POST spar://app/_spar/csp-report, POST spar://app/_spar/csp-report, POST spar://app/_spar/csp-report, POST spar://app/_spar/csp-report, POST spar://app/_spar/csp-report, GET spar://app/events, GET spar://app/events, POST spar://app/_spar/csp-report, POST spar://app/_spar/csp-report, GET spar://app/data.json, GET spar://app/data.json, GET spar://app/data.json
CSP reports received: 13

### self-2.json: profile `self`, WebKitGTK 2.52.6

origin `spar://app`, secure context True, trustedTypes True

| Probe | Outcome | Violation (blockedURI, effectiveDirective) |
|---|---|---|
| fetch relative data.json | ok 200 {"from": "spar://app/data.json"} | none |
| fetch absolute spar://app/data.json | ok 200 {"from": "spar://app/data.json"} | none |
| fetch spar://other/data.json | rejected TypeError: Load failed | `spar://other/data.json` connect-src |
| xhr relative data.json | load 200 {"from": "spar://app/data.json"} | none |
| xhr absolute spar://app/data.json | load 200 {"from": "spar://app/data.json"} | none |
| websocket ws://127.0.0.1 | threw SecurityError: The operation is insecure. | `ws://127.0.0.1:35379/` connect-src |
| websocket wss://127.0.0.1 | threw SecurityError: The operation is insecure. | `wss://127.0.0.1:34829/` connect-src |
| websocket ws://localhost | threw SecurityError: The operation is insecure. | `ws://localhost:38115/` connect-src |
| websocket ws://app | threw SecurityError: The operation is insecure. | `ws://app/` connect-src |
| websocket wss://app | error event, readyState 3 | none |
| websocket ws://app:port | threw SecurityError: The operation is insecure. | `ws://app:43009/` connect-src |
| websocket wss://app:port | threw SecurityError: The operation is insecure. | `wss://app:39877/` connect-src |
| fetch https://app:port | rejected TypeError: Load failed | `https://app:40337/` connect-src |
| fetch http://127.0.0.1 | rejected TypeError: Load failed | `http://127.0.0.1:39947/` connect-src |
| fetch http://localhost | rejected TypeError: Load failed | `http://localhost:40855/` connect-src |
| xhr http://127.0.0.1 | error event, status 0 | `http://127.0.0.1:41823/` connect-src |
| eventsource spar://app/events | message scheme | none |
| eventsource relative events | message scheme | none |
| eventsource http://127.0.0.1 | threw SecurityError: The operation is insecure. | `http://127.0.0.1:41173/` connect-src |
| sendBeacon spar://app/beacon | threw TypeError: Beacons can only be sent over HTTP(S) | none |
| sendBeacon relative beacon | threw TypeError: Beacons can only be sent over HTTP(S) | none |
| sendBeacon http://127.0.0.1 | returned true | `http://127.0.0.1:46601/` connect-src |
| worker fetch relative data.json | ok 200 {"from": "spar://app/data.json"} | none |
| worker fetch absolute spar://app/data.json | ok 200 {"from": "spar://app/data.json"} | none |
| worker xhr relative data.json | load 200 {"from": "spar://app/data.json"} | none |
| worker fetch http://127.0.0.1 | rejected TypeError: Load failed | none |
| worker xhr http://127.0.0.1 | error event, status 0 | none |
| worker websocket ws://127.0.0.1 | threw SecurityError: The operation is insecure. | none |

Loopback connections per listener: ws 0, wss 0, wsLocalhost 0, wsApp 0, wssApp 0, httpsApp 0, fetch 0, fetchLocalhost 0, xhr 0, es 0, beacon 0, wfetch 0, wws 0, wxhr 0
Scheme requests (other than scripts and the page): GET spar://app/data.json, GET spar://app/data.json, POST spar://app/_spar/csp-report, GET spar://app/data.json, GET spar://app/data.json, POST spar://app/_spar/csp-report, POST spar://app/_spar/csp-report, POST spar://app/_spar/csp-report, POST spar://app/_spar/csp-report, POST spar://app/_spar/csp-report, POST spar://app/_spar/csp-report, POST spar://app/_spar/csp-report, POST spar://app/_spar/csp-report, POST spar://app/_spar/csp-report, POST spar://app/_spar/csp-report, GET spar://app/events, GET spar://app/events, POST spar://app/_spar/csp-report, POST spar://app/_spar/csp-report, GET spar://app/data.json, GET spar://app/data.json, GET spar://app/data.json
CSP reports received: 13

### spar-1.json: profile `spar`, WebKitGTK 2.52.6

origin `spar://app`, secure context True, trustedTypes True

| Probe | Outcome | Violation (blockedURI, effectiveDirective) |
|---|---|---|
| fetch relative data.json | ok 200 {"from": "spar://app/data.json"} | none |
| fetch absolute spar://app/data.json | ok 200 {"from": "spar://app/data.json"} | none |
| fetch spar://other/data.json | rejected TypeError: Load failed | `spar://other/data.json` connect-src |
| xhr relative data.json | load 200 {"from": "spar://app/data.json"} | none |
| xhr absolute spar://app/data.json | load 200 {"from": "spar://app/data.json"} | none |
| websocket ws://127.0.0.1 | threw SecurityError: The operation is insecure. | `ws://127.0.0.1:39795/` connect-src |
| websocket wss://127.0.0.1 | threw SecurityError: The operation is insecure. | `wss://127.0.0.1:46155/` connect-src |
| websocket ws://localhost | threw SecurityError: The operation is insecure. | `ws://localhost:40751/` connect-src |
| websocket ws://app | threw SecurityError: The operation is insecure. | `ws://app/` connect-src |
| websocket wss://app | threw SecurityError: The operation is insecure. | `wss://app/` connect-src |
| websocket ws://app:port | threw SecurityError: The operation is insecure. | `ws://app:37421/` connect-src |
| websocket wss://app:port | threw SecurityError: The operation is insecure. | `wss://app:45789/` connect-src |
| fetch https://app:port | rejected TypeError: Load failed | `https://app:36133/` connect-src |
| fetch http://127.0.0.1 | rejected TypeError: Load failed | `http://127.0.0.1:46051/` connect-src |
| fetch http://localhost | rejected TypeError: Load failed | `http://localhost:36781/` connect-src |
| xhr http://127.0.0.1 | error event, status 0 | `http://127.0.0.1:40945/` connect-src |
| eventsource spar://app/events | message scheme | none |
| eventsource relative events | message scheme | none |
| eventsource http://127.0.0.1 | threw SecurityError: The operation is insecure. | `http://127.0.0.1:41167/` connect-src |
| sendBeacon spar://app/beacon | threw TypeError: Beacons can only be sent over HTTP(S) | none |
| sendBeacon relative beacon | threw TypeError: Beacons can only be sent over HTTP(S) | none |
| sendBeacon http://127.0.0.1 | returned true | `http://127.0.0.1:42553/` connect-src |
| worker fetch relative data.json | ok 200 {"from": "spar://app/data.json"} | none |
| worker fetch absolute spar://app/data.json | ok 200 {"from": "spar://app/data.json"} | none |
| worker xhr relative data.json | load 200 {"from": "spar://app/data.json"} | none |
| worker fetch http://127.0.0.1 | rejected TypeError: Load failed | none |
| worker xhr http://127.0.0.1 | error event, status 0 | none |
| worker websocket ws://127.0.0.1 | threw SecurityError: The operation is insecure. | none |

Loopback connections per listener: ws 0, wss 0, wsLocalhost 0, wsApp 0, wssApp 0, httpsApp 0, fetch 0, fetchLocalhost 0, xhr 0, es 0, beacon 0, wfetch 0, wws 0, wxhr 0
Scheme requests (other than scripts and the page): GET spar://app/data.json, GET spar://app/data.json, POST spar://app/_spar/csp-report, GET spar://app/data.json, GET spar://app/data.json, POST spar://app/_spar/csp-report, POST spar://app/_spar/csp-report, POST spar://app/_spar/csp-report, POST spar://app/_spar/csp-report, POST spar://app/_spar/csp-report, POST spar://app/_spar/csp-report, POST spar://app/_spar/csp-report, POST spar://app/_spar/csp-report, POST spar://app/_spar/csp-report, POST spar://app/_spar/csp-report, POST spar://app/_spar/csp-report, GET spar://app/events, GET spar://app/events, POST spar://app/_spar/csp-report, POST spar://app/_spar/csp-report, GET spar://app/data.json, GET spar://app/data.json, GET spar://app/data.json
CSP reports received: 14

### spar-2.json: profile `spar`, WebKitGTK 2.52.6

origin `spar://app`, secure context True, trustedTypes True

| Probe | Outcome | Violation (blockedURI, effectiveDirective) |
|---|---|---|
| fetch relative data.json | ok 200 {"from": "spar://app/data.json"} | none |
| fetch absolute spar://app/data.json | ok 200 {"from": "spar://app/data.json"} | none |
| fetch spar://other/data.json | rejected TypeError: Load failed | `spar://other/data.json` connect-src |
| xhr relative data.json | load 200 {"from": "spar://app/data.json"} | none |
| xhr absolute spar://app/data.json | load 200 {"from": "spar://app/data.json"} | none |
| websocket ws://127.0.0.1 | threw SecurityError: The operation is insecure. | `ws://127.0.0.1:46299/` connect-src |
| websocket wss://127.0.0.1 | threw SecurityError: The operation is insecure. | `wss://127.0.0.1:40841/` connect-src |
| websocket ws://localhost | threw SecurityError: The operation is insecure. | `ws://localhost:34297/` connect-src |
| websocket ws://app | threw SecurityError: The operation is insecure. | `ws://app/` connect-src |
| websocket wss://app | threw SecurityError: The operation is insecure. | `wss://app/` connect-src |
| websocket ws://app:port | threw SecurityError: The operation is insecure. | `ws://app:46605/` connect-src |
| websocket wss://app:port | threw SecurityError: The operation is insecure. | `wss://app:42391/` connect-src |
| fetch https://app:port | rejected TypeError: Load failed | `https://app:37783/` connect-src |
| fetch http://127.0.0.1 | rejected TypeError: Load failed | `http://127.0.0.1:34725/` connect-src |
| fetch http://localhost | rejected TypeError: Load failed | `http://localhost:44083/` connect-src |
| xhr http://127.0.0.1 | error event, status 0 | `http://127.0.0.1:46163/` connect-src |
| eventsource spar://app/events | message scheme | none |
| eventsource relative events | message scheme | none |
| eventsource http://127.0.0.1 | threw SecurityError: The operation is insecure. | `http://127.0.0.1:35425/` connect-src |
| sendBeacon spar://app/beacon | threw TypeError: Beacons can only be sent over HTTP(S) | none |
| sendBeacon relative beacon | threw TypeError: Beacons can only be sent over HTTP(S) | none |
| sendBeacon http://127.0.0.1 | returned true | `http://127.0.0.1:38535/` connect-src |
| worker fetch relative data.json | ok 200 {"from": "spar://app/data.json"} | none |
| worker fetch absolute spar://app/data.json | ok 200 {"from": "spar://app/data.json"} | none |
| worker xhr relative data.json | load 200 {"from": "spar://app/data.json"} | none |
| worker fetch http://127.0.0.1 | rejected TypeError: Load failed | none |
| worker xhr http://127.0.0.1 | error event, status 0 | none |
| worker websocket ws://127.0.0.1 | threw SecurityError: The operation is insecure. | none |

Loopback connections per listener: ws 0, wss 0, wsLocalhost 0, wsApp 0, wssApp 0, httpsApp 0, fetch 0, fetchLocalhost 0, xhr 0, es 0, beacon 0, wfetch 0, wws 0, wxhr 0
Scheme requests (other than scripts and the page): GET spar://app/data.json, GET spar://app/data.json, POST spar://app/_spar/csp-report, GET spar://app/data.json, GET spar://app/data.json, POST spar://app/_spar/csp-report, POST spar://app/_spar/csp-report, POST spar://app/_spar/csp-report, POST spar://app/_spar/csp-report, POST spar://app/_spar/csp-report, POST spar://app/_spar/csp-report, POST spar://app/_spar/csp-report, POST spar://app/_spar/csp-report, POST spar://app/_spar/csp-report, POST spar://app/_spar/csp-report, POST spar://app/_spar/csp-report, GET spar://app/events, GET spar://app/events, POST spar://app/_spar/csp-report, POST spar://app/_spar/csp-report, GET spar://app/data.json, GET spar://app/data.json, GET spar://app/data.json
CSP reports received: 14

## Acceleration default

| Run | Env | Settings policy | GSK renderer | Private MB (all processes) | Load finished ms | webkit://gpu excerpt |
|---|---|---|---|---|---|---|
| defaults-1.json | - | always | GskGLRenderer | 321.4 | 538.4 | Policy: always ¦ 2D canvas: Accelerated ¦ Renderer: DMABuf (Supported buffers: Shared Memory) ¦ Native interface: EGL ¦ GL_RENDERER: llvmpipe (LLVM 20.1.2, 256 bits) |
| defaults-2.json | - | always | GskGLRenderer | 255.1 | 187.5 | Policy: always ¦ 2D canvas: Accelerated ¦ Renderer: DMABuf (Supported buffers: Shared Memory) ¦ Native interface: EGL ¦ GL_RENDERER: llvmpipe (LLVM 20.1.2, 256 bits) |
| defaults-3.json | - | always | GskGLRenderer | 254.7 | 189.4 | Policy: always ¦ 2D canvas: Accelerated ¦ Renderer: DMABuf (Supported buffers: Shared Memory) ¦ Native interface: EGL ¦ GL_RENDERER: llvmpipe (LLVM 20.1.2, 256 bits) |
| dmabuf-off-cairo-1.json | GSK_RENDERER=cairo WEBKIT_DISABLE_DMABUF_RENDERER=1 | never | GskCairoRenderer | 72.5 | 91.5 | Policy: never ¦ 2D canvas: Accelerated |
| dmabuf-off-cairo-2.json | GSK_RENDERER=cairo WEBKIT_DISABLE_DMABUF_RENDERER=1 | never | GskCairoRenderer | 72.3 | 90.9 | Policy: never ¦ 2D canvas: Accelerated |
| dmabuf-off-cairo-3.json | GSK_RENDERER=cairo WEBKIT_DISABLE_DMABUF_RENDERER=1 | never | GskCairoRenderer | 72.3 | 92.8 | Policy: never ¦ 2D canvas: Accelerated |
| never-cairo-1.json | GSK_RENDERER=cairo | never | GskCairoRenderer | 82.5 | 106.9 | Policy: never ¦ 2D canvas: Accelerated |
| never-cairo-2.json | GSK_RENDERER=cairo | never | GskCairoRenderer | 84.6 | 100.0 | Policy: never ¦ 2D canvas: Accelerated |
| never-cairo-3.json | GSK_RENDERER=cairo | never | GskCairoRenderer | 82.1 | 108.7 | Policy: never ¦ 2D canvas: Accelerated |
| never-only-1.json | - | never | GskGLRenderer | 140.6 | 156.5 | Policy: never ¦ 2D canvas: Accelerated |
| never-only-2.json | - | never | GskGLRenderer | 140.4 | 156.7 | Policy: never ¦ 2D canvas: Accelerated |
| never-only-3.json | - | never | GskGLRenderer | 140.5 | 156.2 | Policy: never ¦ 2D canvas: Accelerated |

## Versions

```
uid=1000(ubuntu) gid=1000(ubuntu) groups=1000(ubuntu),4(adm),20(dialout),24(cdrom),25(floppy),27(sudo),29(audio),30(dip),44(video),46(plugdev)
6.18.58.1-microsoft-standard-WSL2
PRETTY_NAME="Ubuntu 24.04.5 LTS"
ls: cannot access '/dev/dri': No such file or directory
ls: cannot access '/dev/dxg': No such file or directory
libwebkitgtk-6.0-4:amd64 2.52.6-0ubuntu0.24.04.2
mesa-libgallium:amd64 25.2.8-0ubuntu0.24.04.5
```

