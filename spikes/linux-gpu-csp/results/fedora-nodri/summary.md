# fedora-nodri

## series.jsonl

| Method | Kind | n | Detect ms median (range) | Process wall ms median | RSS growth MB median | Verdict | Renderer / reason |
|---|---|---|---|---|---|---|---|
| `none` | cold | 3 | 0.00 (0.00–0.00) | 3.27 (3.03–3.36) | 0.0 | unknown |  |
| `node` | cold | 3 | 0.05 (0.04–0.05) | 3.62 (3.56–3.96) | 0.0 | software | no render node |
| `combined` | cold | 3 | 0.05 (0.05–0.05) | 3.53 (3.41–3.88) | 0.0 | software | no openable render node; EGL skipped |
| `devices` | cold | 3 | 62.55 (60.58–64.00) | 68.48 (67.05–70.52) | 33.2 | unknown |  |
| `egl --platform surfaceless --no-context` | cold | 3 | 69.36 (65.52–70.52) | 75.43 (71.74–76.85) | 35.1 | software | driver name 'swrast'; EGL driver swrast |
| `egl --platform surfaceless` | cold | 3 | 79.84 (58.91–99.62) | 84.72 (62.91–107.00) | 54.5 | software | llvmpipe (LLVM 21.1.8, 256 bits); EGL driver swrast |
| `egl --platform device` | cold | 3 | 26.05 (24.81–27.49) | 29.43 (27.65–30.50) | 48.0 | software | llvmpipe (LLVM 21.1.8, 256 bits); EGL driver swrast |
| `egl --platform default` | cold | 3 | 15.95 (14.16–66.77) | 19.52 (17.64–73.46) | 27.6 | software | no GL renderer: eglInitialize failed (0x3001); error: eglInitialize failed (0x3001) |
| `none` | warm | 25 | 0.00 (0.00–0.00) | 1.23 (1.14–1.49) | 0.0 | unknown |  |
| `node` | warm | 25 | 0.04 (0.03–0.07) | 1.15 (1.07–1.30) | 0.0 | software | no render node |
| `combined` | warm | 25 | 0.04 (0.03–0.05) | 1.12 (1.06–1.36) | 0.0 | software | no openable render node; EGL skipped |
| `devices` | warm | 25 | 7.03 (6.66–7.41) | 9.05 (8.48–10.10) | 33.3 | unknown |  |
| `egl --platform surfaceless --no-context` | warm | 25 | 8.17 (7.61–9.43) | 10.33 (9.37–11.79) | 35.0 | software | driver name 'swrast'; EGL driver swrast |
| `egl --platform surfaceless` | warm | 25 | 18.72 (17.79–20.46) | 20.33 (19.17–22.06) | 54.4 | software | llvmpipe (LLVM 21.1.8, 256 bits); EGL driver swrast |
| `egl --platform device` | warm | 25 | 18.78 (17.61–20.38) | 20.44 (19.15–22.03) | 54.4 | software | llvmpipe (LLVM 21.1.8, 256 bits); EGL driver swrast |
| `egl --platform default` | warm | 25 | 7.07 (6.85–8.19) | 9.31 (8.87–10.35) | 33.3 | software | no GL renderer: eglInitialize failed (0x3001); error: eglInitialize failed (0x3001) |

Warm phase medians (ms):

- `node`: node 0.04;
- `combined`: node 0.04;
- `devices`: load 0.17; enumerate 6.86;
- `egl --platform surfaceless --no-context`: load 0.18; get-display 6.79; initialize 1.19; query-driver 0.01; teardown 0.02;
- `egl --platform surfaceless`: load 0.18; get-display 6.85; initialize 1.19; query-driver 0.01; context-and-gl-strings 9.43; teardown 1.05;
- `egl --platform device`: load 0.19; get-display 6.92; initialize 1.13; query-driver 0.01; context-and-gl-strings 9.50; teardown 1.08;
- `egl --platform default`: load 0.19; get-display 6.88;

## gdk.jsonl

| Method | Kind | n | Detect ms median (range) | Process wall ms median | RSS growth MB median | Verdict | Renderer / reason |
|---|---|---|---|---|---|---|---|
| `gdk` | cold | 3 | 125.35 (72.01–140.41) | 134.57 (78.80–147.34) | 85.3 | software | llvmpipe (LLVM 21.1.8, 256 bits) |
| `gdk --window` | cold | 3 | 140.52 (136.46–140.87) | 151.67 (146.80–151.96) | 97.3 | software | llvmpipe (LLVM 21.1.8, 256 bits); window: GskGLRenderer |
| `gdk --window --gsk-after-init cairo` | cold | 3 | 129.98 (129.53–136.65) | 140.69 (140.39–146.96) | 90.4 | software | llvmpipe (LLVM 21.1.8, 256 bits); window: GskCairoRenderer |
| `gdk --pre-egl --window` | cold | 3 | 145.38 (145.31–150.58) | 157.07 (156.52–161.23) | 103.4 | software | llvmpipe (LLVM 21.1.8, 256 bits); window: GskGLRenderer |
| `gdk` | warm | 25 | 41.76 (40.51–49.12) | 44.69 (43.26–53.70) | 88.2 | software | llvmpipe (LLVM 21.1.8, 256 bits) |
| `gdk --window` | warm | 25 | 58.07 (55.60–68.98) | 62.39 (58.48–72.00) | 100.3 | software | llvmpipe (LLVM 21.1.8, 256 bits); window: GskGLRenderer |
| `gdk --window --gsk-after-init cairo` | warm | 25 | 50.82 (48.73–57.03) | 54.06 (51.57–59.79) | 93.4 | software | llvmpipe (LLVM 21.1.8, 256 bits); window: GskCairoRenderer |
| `gdk --pre-egl --window` | warm | 25 | 67.39 (63.29–73.42) | 70.86 (66.25–78.62) | 106.9 | software | llvmpipe (LLVM 21.1.8, 256 bits); window: GskGLRenderer |

Warm phase medians (ms):

- `gdk`: gtk-init 34.69; gl-context-and-strings 7.20;
- `gdk --window`: gtk-init 35.06; gl-context-and-strings 7.18; window-realize 16.28;
- `gdk --window --gsk-after-init cairo`: gtk-init 35.12; gl-context-and-strings 7.05; window-realize 8.71;
- `gdk --pre-egl --window`: load 0.18; get-display 6.80; initialize 1.18; query-driver 0.01; context-and-gl-strings 9.28; teardown 1.13; pre-egl 0.00; gtk-init 24.70; gl-context-and-strings 7.00; window-realize 15.84;

## connect-src

### none-1.json: profile `none`, WebKitGTK 2.54.2

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

### none-2.json: profile `none`, WebKitGTK 2.54.2

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

### self-1.json: profile `self`, WebKitGTK 2.54.2

origin `spar://app`, secure context True, trustedTypes True

| Probe | Outcome | Violation (blockedURI, effectiveDirective) |
|---|---|---|
| fetch relative data.json | ok 200 {"from": "spar://app/data.json"} | none |
| fetch absolute spar://app/data.json | ok 200 {"from": "spar://app/data.json"} | none |
| fetch spar://other/data.json | rejected TypeError: Load failed | `spar://other/data.json` connect-src |
| xhr relative data.json | load 200 {"from": "spar://app/data.json"} | none |
| xhr absolute spar://app/data.json | load 200 {"from": "spar://app/data.json"} | none |
| websocket ws://127.0.0.1 | threw SecurityError: The operation is insecure. | `ws://127.0.0.1:44997/` connect-src |
| websocket wss://127.0.0.1 | threw SecurityError: The operation is insecure. | `wss://127.0.0.1:43703/` connect-src |
| websocket ws://localhost | threw SecurityError: The operation is insecure. | `ws://localhost:41365/` connect-src |
| websocket ws://app | threw SecurityError: The operation is insecure. | `ws://app/` connect-src |
| websocket wss://app | error event, readyState 3 | none |
| websocket ws://app:port | threw SecurityError: The operation is insecure. | `ws://app:40143/` connect-src |
| websocket wss://app:port | threw SecurityError: The operation is insecure. | `wss://app:37095/` connect-src |
| fetch https://app:port | rejected TypeError: Load failed | `https://app:35653/` connect-src |
| fetch http://127.0.0.1 | rejected TypeError: Load failed | `http://127.0.0.1:36347/` connect-src |
| fetch http://localhost | rejected TypeError: Load failed | `http://localhost:41943/` connect-src |
| xhr http://127.0.0.1 | error event, status 0 | `http://127.0.0.1:38571/` connect-src |
| eventsource spar://app/events | message scheme | none |
| eventsource relative events | message scheme | none |
| eventsource http://127.0.0.1 | threw SecurityError: The operation is insecure. | `http://127.0.0.1:37901/` connect-src |
| sendBeacon spar://app/beacon | threw TypeError: Beacons can only be sent over HTTP(S) | none |
| sendBeacon relative beacon | threw TypeError: Beacons can only be sent over HTTP(S) | none |
| sendBeacon http://127.0.0.1 | returned true | `http://127.0.0.1:45795/` connect-src |
| worker fetch relative data.json | ok 200 {"from": "spar://app/data.json"} | none |
| worker fetch absolute spar://app/data.json | ok 200 {"from": "spar://app/data.json"} | none |
| worker xhr relative data.json | load 200 {"from": "spar://app/data.json"} | none |
| worker fetch http://127.0.0.1 | rejected TypeError: Load failed | none |
| worker xhr http://127.0.0.1 | error event, status 0 | none |
| worker websocket ws://127.0.0.1 | threw SecurityError: The operation is insecure. | none |

Loopback connections per listener: ws 0, wss 0, wsLocalhost 0, wsApp 0, wssApp 0, httpsApp 0, fetch 0, fetchLocalhost 0, xhr 0, es 0, beacon 0, wfetch 0, wws 0, wxhr 0
Scheme requests (other than scripts and the page): GET spar://app/data.json, GET spar://app/data.json, POST spar://app/_spar/csp-report, GET spar://app/data.json, GET spar://app/data.json, POST spar://app/_spar/csp-report, POST spar://app/_spar/csp-report, POST spar://app/_spar/csp-report, POST spar://app/_spar/csp-report, POST spar://app/_spar/csp-report, POST spar://app/_spar/csp-report, POST spar://app/_spar/csp-report, POST spar://app/_spar/csp-report, POST spar://app/_spar/csp-report, POST spar://app/_spar/csp-report, GET spar://app/events, GET spar://app/events, POST spar://app/_spar/csp-report, POST spar://app/_spar/csp-report, GET spar://app/data.json, GET spar://app/data.json, GET spar://app/data.json
CSP reports received: 13

### self-2.json: profile `self`, WebKitGTK 2.54.2

origin `spar://app`, secure context True, trustedTypes True

| Probe | Outcome | Violation (blockedURI, effectiveDirective) |
|---|---|---|
| fetch relative data.json | ok 200 {"from": "spar://app/data.json"} | none |
| fetch absolute spar://app/data.json | ok 200 {"from": "spar://app/data.json"} | none |
| fetch spar://other/data.json | rejected TypeError: Load failed | `spar://other/data.json` connect-src |
| xhr relative data.json | load 200 {"from": "spar://app/data.json"} | none |
| xhr absolute spar://app/data.json | load 200 {"from": "spar://app/data.json"} | none |
| websocket ws://127.0.0.1 | threw SecurityError: The operation is insecure. | `ws://127.0.0.1:42877/` connect-src |
| websocket wss://127.0.0.1 | threw SecurityError: The operation is insecure. | `wss://127.0.0.1:35207/` connect-src |
| websocket ws://localhost | threw SecurityError: The operation is insecure. | `ws://localhost:42655/` connect-src |
| websocket ws://app | threw SecurityError: The operation is insecure. | `ws://app/` connect-src |
| websocket wss://app | error event, readyState 3 | none |
| websocket ws://app:port | threw SecurityError: The operation is insecure. | `ws://app:40835/` connect-src |
| websocket wss://app:port | threw SecurityError: The operation is insecure. | `wss://app:38305/` connect-src |
| fetch https://app:port | rejected TypeError: Load failed | `https://app:36189/` connect-src |
| fetch http://127.0.0.1 | rejected TypeError: Load failed | `http://127.0.0.1:38147/` connect-src |
| fetch http://localhost | rejected TypeError: Load failed | `http://localhost:44643/` connect-src |
| xhr http://127.0.0.1 | error event, status 0 | `http://127.0.0.1:36611/` connect-src |
| eventsource spar://app/events | message scheme | none |
| eventsource relative events | message scheme | none |
| eventsource http://127.0.0.1 | threw SecurityError: The operation is insecure. | `http://127.0.0.1:36767/` connect-src |
| sendBeacon spar://app/beacon | threw TypeError: Beacons can only be sent over HTTP(S) | none |
| sendBeacon relative beacon | threw TypeError: Beacons can only be sent over HTTP(S) | none |
| sendBeacon http://127.0.0.1 | returned true | `http://127.0.0.1:34633/` connect-src |
| worker fetch relative data.json | ok 200 {"from": "spar://app/data.json"} | none |
| worker fetch absolute spar://app/data.json | ok 200 {"from": "spar://app/data.json"} | none |
| worker xhr relative data.json | load 200 {"from": "spar://app/data.json"} | none |
| worker fetch http://127.0.0.1 | rejected TypeError: Load failed | none |
| worker xhr http://127.0.0.1 | error event, status 0 | none |
| worker websocket ws://127.0.0.1 | threw SecurityError: The operation is insecure. | none |

Loopback connections per listener: ws 0, wss 0, wsLocalhost 0, wsApp 0, wssApp 0, httpsApp 0, fetch 0, fetchLocalhost 0, xhr 0, es 0, beacon 0, wfetch 0, wws 0, wxhr 0
Scheme requests (other than scripts and the page): GET spar://app/data.json, GET spar://app/data.json, POST spar://app/_spar/csp-report, GET spar://app/data.json, GET spar://app/data.json, POST spar://app/_spar/csp-report, POST spar://app/_spar/csp-report, POST spar://app/_spar/csp-report, POST spar://app/_spar/csp-report, POST spar://app/_spar/csp-report, POST spar://app/_spar/csp-report, POST spar://app/_spar/csp-report, POST spar://app/_spar/csp-report, POST spar://app/_spar/csp-report, POST spar://app/_spar/csp-report, GET spar://app/events, GET spar://app/events, POST spar://app/_spar/csp-report, POST spar://app/_spar/csp-report, GET spar://app/data.json, GET spar://app/data.json, GET spar://app/data.json
CSP reports received: 13

### spar-1.json: profile `spar`, WebKitGTK 2.54.2

origin `spar://app`, secure context True, trustedTypes True

| Probe | Outcome | Violation (blockedURI, effectiveDirective) |
|---|---|---|
| fetch relative data.json | ok 200 {"from": "spar://app/data.json"} | none |
| fetch absolute spar://app/data.json | ok 200 {"from": "spar://app/data.json"} | none |
| fetch spar://other/data.json | rejected TypeError: Load failed | `spar://other/data.json` connect-src |
| xhr relative data.json | load 200 {"from": "spar://app/data.json"} | none |
| xhr absolute spar://app/data.json | load 200 {"from": "spar://app/data.json"} | none |
| websocket ws://127.0.0.1 | threw SecurityError: The operation is insecure. | `ws://127.0.0.1:39687/` connect-src |
| websocket wss://127.0.0.1 | threw SecurityError: The operation is insecure. | `wss://127.0.0.1:45831/` connect-src |
| websocket ws://localhost | threw SecurityError: The operation is insecure. | `ws://localhost:44413/` connect-src |
| websocket ws://app | threw SecurityError: The operation is insecure. | `ws://app/` connect-src |
| websocket wss://app | threw SecurityError: The operation is insecure. | `wss://app/` connect-src |
| websocket ws://app:port | threw SecurityError: The operation is insecure. | `ws://app:42689/` connect-src |
| websocket wss://app:port | threw SecurityError: The operation is insecure. | `wss://app:38131/` connect-src |
| fetch https://app:port | rejected TypeError: Load failed | `https://app:45531/` connect-src |
| fetch http://127.0.0.1 | rejected TypeError: Load failed | `http://127.0.0.1:35003/` connect-src |
| fetch http://localhost | rejected TypeError: Load failed | `http://localhost:42715/` connect-src |
| xhr http://127.0.0.1 | error event, status 0 | `http://127.0.0.1:43523/` connect-src |
| eventsource spar://app/events | message scheme | none |
| eventsource relative events | message scheme | none |
| eventsource http://127.0.0.1 | threw SecurityError: The operation is insecure. | `http://127.0.0.1:44901/` connect-src |
| sendBeacon spar://app/beacon | threw TypeError: Beacons can only be sent over HTTP(S) | none |
| sendBeacon relative beacon | threw TypeError: Beacons can only be sent over HTTP(S) | none |
| sendBeacon http://127.0.0.1 | returned true | `http://127.0.0.1:38677/` connect-src |
| worker fetch relative data.json | ok 200 {"from": "spar://app/data.json"} | none |
| worker fetch absolute spar://app/data.json | ok 200 {"from": "spar://app/data.json"} | none |
| worker xhr relative data.json | load 200 {"from": "spar://app/data.json"} | none |
| worker fetch http://127.0.0.1 | rejected TypeError: Load failed | none |
| worker xhr http://127.0.0.1 | error event, status 0 | none |
| worker websocket ws://127.0.0.1 | threw SecurityError: The operation is insecure. | none |

Loopback connections per listener: ws 0, wss 0, wsLocalhost 0, wsApp 0, wssApp 0, httpsApp 0, fetch 0, fetchLocalhost 0, xhr 0, es 0, beacon 0, wfetch 0, wws 0, wxhr 0
Scheme requests (other than scripts and the page): GET spar://app/data.json, GET spar://app/data.json, POST spar://app/_spar/csp-report, GET spar://app/data.json, GET spar://app/data.json, POST spar://app/_spar/csp-report, POST spar://app/_spar/csp-report, POST spar://app/_spar/csp-report, POST spar://app/_spar/csp-report, POST spar://app/_spar/csp-report, POST spar://app/_spar/csp-report, POST spar://app/_spar/csp-report, POST spar://app/_spar/csp-report, POST spar://app/_spar/csp-report, POST spar://app/_spar/csp-report, POST spar://app/_spar/csp-report, GET spar://app/events, GET spar://app/events, POST spar://app/_spar/csp-report, POST spar://app/_spar/csp-report, GET spar://app/data.json, GET spar://app/data.json, GET spar://app/data.json
CSP reports received: 14

### spar-2.json: profile `spar`, WebKitGTK 2.54.2

origin `spar://app`, secure context True, trustedTypes True

| Probe | Outcome | Violation (blockedURI, effectiveDirective) |
|---|---|---|
| fetch relative data.json | ok 200 {"from": "spar://app/data.json"} | none |
| fetch absolute spar://app/data.json | ok 200 {"from": "spar://app/data.json"} | none |
| fetch spar://other/data.json | rejected TypeError: Load failed | `spar://other/data.json` connect-src |
| xhr relative data.json | load 200 {"from": "spar://app/data.json"} | none |
| xhr absolute spar://app/data.json | load 200 {"from": "spar://app/data.json"} | none |
| websocket ws://127.0.0.1 | threw SecurityError: The operation is insecure. | `ws://127.0.0.1:33741/` connect-src |
| websocket wss://127.0.0.1 | threw SecurityError: The operation is insecure. | `wss://127.0.0.1:41023/` connect-src |
| websocket ws://localhost | threw SecurityError: The operation is insecure. | `ws://localhost:41825/` connect-src |
| websocket ws://app | threw SecurityError: The operation is insecure. | `ws://app/` connect-src |
| websocket wss://app | threw SecurityError: The operation is insecure. | `wss://app/` connect-src |
| websocket ws://app:port | threw SecurityError: The operation is insecure. | `ws://app:36437/` connect-src |
| websocket wss://app:port | threw SecurityError: The operation is insecure. | `wss://app:44453/` connect-src |
| fetch https://app:port | rejected TypeError: Load failed | `https://app:43379/` connect-src |
| fetch http://127.0.0.1 | rejected TypeError: Load failed | `http://127.0.0.1:33381/` connect-src |
| fetch http://localhost | rejected TypeError: Load failed | `http://localhost:44771/` connect-src |
| xhr http://127.0.0.1 | error event, status 0 | `http://127.0.0.1:39359/` connect-src |
| eventsource spar://app/events | message scheme | none |
| eventsource relative events | message scheme | none |
| eventsource http://127.0.0.1 | threw SecurityError: The operation is insecure. | `http://127.0.0.1:41741/` connect-src |
| sendBeacon spar://app/beacon | threw TypeError: Beacons can only be sent over HTTP(S) | none |
| sendBeacon relative beacon | threw TypeError: Beacons can only be sent over HTTP(S) | none |
| sendBeacon http://127.0.0.1 | returned true | `http://127.0.0.1:44779/` connect-src |
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
| defaults-1.json | - | always | GskGLRenderer | 222.5 | 312.8 | Policy: always ¦ 2D canvas: Accelerated ¦ Renderer: DMABuf (Supported buffers: Shared Memory) ¦ Native interface: EGL ¦ GL_RENDERER: llvmpipe (LLVM 21.1.8, 256 bits) |
| defaults-2.json | - | always | GskGLRenderer | 195.7 | 180.2 | Policy: always ¦ 2D canvas: Accelerated ¦ Renderer: DMABuf (Supported buffers: Shared Memory) ¦ Native interface: EGL ¦ GL_RENDERER: llvmpipe (LLVM 21.1.8, 256 bits) |
| defaults-3.json | - | always | GskGLRenderer | 195.7 | 179.2 | Policy: always ¦ 2D canvas: Accelerated ¦ Renderer: DMABuf (Supported buffers: Shared Memory) ¦ Native interface: EGL ¦ GL_RENDERER: llvmpipe (LLVM 21.1.8, 256 bits) |
| dmabuf-off-cairo-1.json | GSK_RENDERER=cairo WEBKIT_DISABLE_DMABUF_RENDERER=1 | never | GskCairoRenderer | 73.1 | 99.0 | Policy: never ¦ 2D canvas: Accelerated |
| dmabuf-off-cairo-2.json | GSK_RENDERER=cairo WEBKIT_DISABLE_DMABUF_RENDERER=1 | never | GskCairoRenderer | 82.1 | 100.9 | Policy: never ¦ 2D canvas: Accelerated |
| dmabuf-off-cairo-3.json | GSK_RENDERER=cairo WEBKIT_DISABLE_DMABUF_RENDERER=1 | never | GskCairoRenderer | 73.2 | 100.5 | Policy: never ¦ 2D canvas: Accelerated |
| never-cairo-1.json | GSK_RENDERER=cairo | never | GskCairoRenderer | 82.3 | 99.2 | Policy: never ¦ 2D canvas: Accelerated |
| never-cairo-2.json | GSK_RENDERER=cairo | never | GskCairoRenderer | 73.1 | 97.2 | Policy: never ¦ 2D canvas: Accelerated |
| never-cairo-3.json | GSK_RENDERER=cairo | never | GskCairoRenderer | 73.2 | 99.8 | Policy: never ¦ 2D canvas: Accelerated |
| never-only-1.json | - | never | GskGLRenderer | 100.2 | 111.7 | Policy: never ¦ 2D canvas: Accelerated |
| never-only-2.json | - | never | GskGLRenderer | 100.1 | 111.4 | Policy: never ¦ 2D canvas: Accelerated |
| never-only-3.json | - | never | GskGLRenderer | 99.9 | 109.7 | Policy: never ¦ 2D canvas: Accelerated |

## Versions

```
uid=1000(probe) gid=1000(probe) groups=1000(probe)
6.18.58.1-microsoft-standard-WSL2
PRETTY_NAME="Fedora Linux 43 (Container Image)"
ls: cannot access '/dev/dri': No such file or directory
ls: cannot access '/dev/dxg': No such file or directory
webkitgtk6.0-2.54.2-1.fc43.x86_64
gtk4-4.20.4-1.fc43.x86_64
mesa-dri-drivers-25.3.6-3.fc43.x86_64
mesa-libEGL-25.3.6-3.fc43.x86_64
```

