# wsl

## series.jsonl

| Method | Kind | n | Detect ms median (range) | Process wall ms median | RSS growth MB median | Verdict | Renderer / reason |
|---|---|---|---|---|---|---|---|
| `none` | cold | 5 | 0.00 (0.00–0.00) | 3.65 (2.95–8.94) | 0.0 | unknown |  |
| `node` | cold | 5 | 0.11 (0.10–0.11) | 3.41 (3.20–8.23) | 0.2 | software | no render node is openable |
| `combined` | cold | 5 | 0.10 (0.10–0.12) | 7.25 (3.13–8.86) | 0.2 | software | no openable render node; EGL skipped |
| `devices` | cold | 5 | 73.10 (68.53–78.30) | 80.98 (74.53–84.73) | 54.3 | unknown |  |
| `egl --platform surfaceless --no-context` | cold | 5 | 89.51 (81.11–93.36) | 95.93 (89.83–102.07) | 55.8 | software | driver name 'swrast'; EGL driver swrast |
| `egl --platform surfaceless` | cold | 5 | 39.72 (38.64–114.37) | 46.50 (42.62–118.98) | 73.8 | software | llvmpipe (LLVM 20.1.2, 256 bits); EGL driver swrast |
| `egl --platform device` | cold | 5 | 38.37 (32.48–87.97) | 43.08 (36.17–91.40) | 73.7 | software | llvmpipe (LLVM 20.1.2, 256 bits); EGL driver swrast |
| `egl --platform default` | cold | 5 | 85.20 (23.35–93.37) | 90.92 (27.90–102.84) | 54.3 | software | no GL renderer: eglInitialize failed (0x3001); error: eglInitialize failed (0x3001) |
| `none` | warm | 25 | 0.00 (0.00–0.00) | 1.26 (1.10–1.58) | 0.0 | unknown |  |
| `node` | warm | 25 | 0.09 (0.09–0.18) | 1.22 (1.12–1.42) | 0.2 | software | no render node is openable |
| `combined` | warm | 25 | 0.09 (0.08–0.11) | 1.17 (1.06–1.78) | 0.2 | software | no openable render node; EGL skipped |
| `devices` | warm | 25 | 7.85 (7.62–9.17) | 9.93 (9.41–11.47) | 54.3 | unknown |  |
| `egl --platform surfaceless --no-context` | warm | 25 | 9.45 (8.80–11.12) | 11.55 (10.85–14.58) | 55.8 | software | driver name 'swrast'; EGL driver swrast |
| `egl --platform surfaceless` | warm | 25 | 20.29 (18.36–26.80) | 21.98 (19.84–29.41) | 73.7 | software | llvmpipe (LLVM 20.1.2, 256 bits); EGL driver swrast |
| `egl --platform device` | warm | 25 | 20.23 (18.31–27.63) | 21.71 (19.78–29.42) | 73.7 | software | llvmpipe (LLVM 20.1.2, 256 bits); EGL driver swrast |
| `egl --platform default` | warm | 25 | 7.94 (7.60–9.81) | 10.11 (9.53–12.22) | 54.4 | software | no GL renderer: eglInitialize failed (0x3001); error: eglInitialize failed (0x3001) |

Warm phase medians (ms):

- `node`: node 0.09;
- `combined`: node 0.09;
- `devices`: load 0.17; enumerate 7.69;
- `egl --platform surfaceless --no-context`: load 0.19; get-display 7.88; initialize 1.28; query-driver 0.01; teardown 0.02;
- `egl --platform surfaceless`: load 0.18; get-display 7.94; initialize 1.29; query-driver 0.01; context-and-gl-strings 9.02; teardown 1.16;
- `egl --platform device`: load 0.18; get-display 7.80; initialize 1.18; query-driver 0.01; context-and-gl-strings 9.79; teardown 1.07;
- `egl --platform default`: load 0.18; get-display 7.76;

## gdk.jsonl

| Method | Kind | n | Detect ms median (range) | Process wall ms median | RSS growth MB median | Verdict | Renderer / reason |
|---|---|---|---|---|---|---|---|
| `gdk` | cold | 5 | 109.54 (100.63–113.07) | 116.35 (108.95–120.75) | 98.9 | software | llvmpipe (LLVM 20.1.2, 256 bits) |
| `gdk --window` | cold | 5 | 176.86 (163.59–179.13) | 187.88 (175.76–191.02) | 158.7 | software | llvmpipe (LLVM 20.1.2, 256 bits); window: GskGLRenderer |
| `gdk --window --gsk-after-init cairo` | cold | 5 | 106.01 (103.96–115.24) | 118.15 (115.28–124.89) | 103.8 | software | llvmpipe (LLVM 20.1.2, 256 bits); window: GskCairoRenderer |
| `gdk --pre-egl --window` | cold | 5 | 175.01 (160.55–179.24) | 187.84 (172.03–196.95) | 164.5 | software | llvmpipe (LLVM 20.1.2, 256 bits); window: GskGLRenderer |
| `gdk` | warm | 25 | 34.45 (33.22–43.46) | 36.72 (35.46–45.98) | 99.1 | software | llvmpipe (LLVM 20.1.2, 256 bits) |
| `gdk --window` | warm | 25 | 91.51 (88.59–114.08) | 97.64 (92.43–120.00) | 158.9 | software | llvmpipe (LLVM 20.1.2, 256 bits); window: GskGLRenderer |
| `gdk --window --gsk-after-init cairo` | warm | 25 | 42.32 (39.46–48.59) | 44.98 (41.63–53.66) | 104.0 | software | llvmpipe (LLVM 20.1.2, 256 bits); window: GskCairoRenderer |
| `gdk --pre-egl --window` | warm | 25 | 99.53 (94.23–122.83) | 105.08 (99.71–129.83) | 164.7 | software | llvmpipe (LLVM 20.1.2, 256 bits); window: GskGLRenderer |

Warm phase medians (ms):

- `gdk`: gtk-init 28.40; gl-context-and-strings 5.86;
- `gdk --window`: gtk-init 28.44; gl-context-and-strings 5.97; window-realize 58.59;
- `gdk --window --gsk-after-init cairo`: gtk-init 28.94; gl-context-and-strings 5.97; window-realize 7.75;
- `gdk --pre-egl --window`: load 0.18; get-display 7.46; initialize 1.20; query-driver 0.01; context-and-gl-strings 8.39; teardown 1.08; pre-egl 0.00; gtk-init 16.52; gl-context-and-strings 5.65; window-realize 58.76;

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

Loopback connections per listener: ws 1, wss 1, wsLocalhost 1, wsApp 0, wssApp 0, httpsApp 0, fetch 1, fetchLocalhost 1, xhr 1, es 1, beacon 1, wfetch 1, wws 1, wxhr 1
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

Loopback connections per listener: ws 1, wss 1, wsLocalhost 1, wsApp 0, wssApp 0, httpsApp 0, fetch 1, fetchLocalhost 1, xhr 1, es 1, beacon 1, wfetch 1, wws 1, wxhr 1
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
| websocket ws://127.0.0.1 | threw SecurityError: The operation is insecure. | `ws://127.0.0.1:36307/` connect-src |
| websocket wss://127.0.0.1 | threw SecurityError: The operation is insecure. | `wss://127.0.0.1:46015/` connect-src |
| websocket ws://localhost | threw SecurityError: The operation is insecure. | `ws://localhost:40579/` connect-src |
| websocket ws://app | threw SecurityError: The operation is insecure. | `ws://app/` connect-src |
| websocket wss://app | error event, readyState 3 | none |
| websocket ws://app:port | threw SecurityError: The operation is insecure. | `ws://app:39041/` connect-src |
| websocket wss://app:port | threw SecurityError: The operation is insecure. | `wss://app:34921/` connect-src |
| fetch https://app:port | rejected TypeError: Load failed | `https://app:46439/` connect-src |
| fetch http://127.0.0.1 | rejected TypeError: Load failed | `http://127.0.0.1:39485/` connect-src |
| fetch http://localhost | rejected TypeError: Load failed | `http://localhost:38535/` connect-src |
| xhr http://127.0.0.1 | error event, status 0 | `http://127.0.0.1:36955/` connect-src |
| eventsource spar://app/events | message scheme | none |
| eventsource relative events | message scheme | none |
| eventsource http://127.0.0.1 | threw SecurityError: The operation is insecure. | `http://127.0.0.1:38615/` connect-src |
| sendBeacon spar://app/beacon | threw TypeError: Beacons can only be sent over HTTP(S) | none |
| sendBeacon relative beacon | threw TypeError: Beacons can only be sent over HTTP(S) | none |
| sendBeacon http://127.0.0.1 | returned true | `http://127.0.0.1:36609/` connect-src |
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
| websocket ws://127.0.0.1 | threw SecurityError: The operation is insecure. | `ws://127.0.0.1:46751/` connect-src |
| websocket wss://127.0.0.1 | threw SecurityError: The operation is insecure. | `wss://127.0.0.1:34997/` connect-src |
| websocket ws://localhost | threw SecurityError: The operation is insecure. | `ws://localhost:42151/` connect-src |
| websocket ws://app | threw SecurityError: The operation is insecure. | `ws://app/` connect-src |
| websocket wss://app | error event, readyState 3 | none |
| websocket ws://app:port | threw SecurityError: The operation is insecure. | `ws://app:46763/` connect-src |
| websocket wss://app:port | threw SecurityError: The operation is insecure. | `wss://app:36171/` connect-src |
| fetch https://app:port | rejected TypeError: Load failed | `https://app:39415/` connect-src |
| fetch http://127.0.0.1 | rejected TypeError: Load failed | `http://127.0.0.1:36193/` connect-src |
| fetch http://localhost | rejected TypeError: Load failed | `http://localhost:38423/` connect-src |
| xhr http://127.0.0.1 | error event, status 0 | `http://127.0.0.1:39839/` connect-src |
| eventsource spar://app/events | message scheme | none |
| eventsource relative events | message scheme | none |
| eventsource http://127.0.0.1 | threw SecurityError: The operation is insecure. | `http://127.0.0.1:35945/` connect-src |
| sendBeacon spar://app/beacon | threw TypeError: Beacons can only be sent over HTTP(S) | none |
| sendBeacon relative beacon | threw TypeError: Beacons can only be sent over HTTP(S) | none |
| sendBeacon http://127.0.0.1 | returned true | `http://127.0.0.1:45965/` connect-src |
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
| websocket ws://127.0.0.1 | threw SecurityError: The operation is insecure. | `ws://127.0.0.1:37725/` connect-src |
| websocket wss://127.0.0.1 | threw SecurityError: The operation is insecure. | `wss://127.0.0.1:38065/` connect-src |
| websocket ws://localhost | threw SecurityError: The operation is insecure. | `ws://localhost:40541/` connect-src |
| websocket ws://app | threw SecurityError: The operation is insecure. | `ws://app/` connect-src |
| websocket wss://app | threw SecurityError: The operation is insecure. | `wss://app/` connect-src |
| websocket ws://app:port | threw SecurityError: The operation is insecure. | `ws://app:38141/` connect-src |
| websocket wss://app:port | threw SecurityError: The operation is insecure. | `wss://app:45299/` connect-src |
| fetch https://app:port | rejected TypeError: Load failed | `https://app:43241/` connect-src |
| fetch http://127.0.0.1 | rejected TypeError: Load failed | `http://127.0.0.1:38251/` connect-src |
| fetch http://localhost | rejected TypeError: Load failed | `http://localhost:35033/` connect-src |
| xhr http://127.0.0.1 | error event, status 0 | `http://127.0.0.1:36953/` connect-src |
| eventsource spar://app/events | message scheme | none |
| eventsource relative events | message scheme | none |
| eventsource http://127.0.0.1 | threw SecurityError: The operation is insecure. | `http://127.0.0.1:35359/` connect-src |
| sendBeacon spar://app/beacon | threw TypeError: Beacons can only be sent over HTTP(S) | none |
| sendBeacon relative beacon | threw TypeError: Beacons can only be sent over HTTP(S) | none |
| sendBeacon http://127.0.0.1 | returned true | `http://127.0.0.1:43735/` connect-src |
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
| websocket ws://127.0.0.1 | threw SecurityError: The operation is insecure. | `ws://127.0.0.1:45089/` connect-src |
| websocket wss://127.0.0.1 | threw SecurityError: The operation is insecure. | `wss://127.0.0.1:36607/` connect-src |
| websocket ws://localhost | threw SecurityError: The operation is insecure. | `ws://localhost:44195/` connect-src |
| websocket ws://app | threw SecurityError: The operation is insecure. | `ws://app/` connect-src |
| websocket wss://app | threw SecurityError: The operation is insecure. | `wss://app/` connect-src |
| websocket ws://app:port | threw SecurityError: The operation is insecure. | `ws://app:32849/` connect-src |
| websocket wss://app:port | threw SecurityError: The operation is insecure. | `wss://app:34537/` connect-src |
| fetch https://app:port | rejected TypeError: Load failed | `https://app:35677/` connect-src |
| fetch http://127.0.0.1 | rejected TypeError: Load failed | `http://127.0.0.1:42605/` connect-src |
| fetch http://localhost | rejected TypeError: Load failed | `http://localhost:40427/` connect-src |
| xhr http://127.0.0.1 | error event, status 0 | `http://127.0.0.1:46663/` connect-src |
| eventsource spar://app/events | message scheme | none |
| eventsource relative events | message scheme | none |
| eventsource http://127.0.0.1 | threw SecurityError: The operation is insecure. | `http://127.0.0.1:45873/` connect-src |
| sendBeacon spar://app/beacon | threw TypeError: Beacons can only be sent over HTTP(S) | none |
| sendBeacon relative beacon | threw TypeError: Beacons can only be sent over HTTP(S) | none |
| sendBeacon http://127.0.0.1 | returned true | `http://127.0.0.1:40719/` connect-src |
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
| defaults-1.json | - | always | GskGLRenderer | 322.0 | 693.5 | Policy: always ¦ 2D canvas: Accelerated ¦ Renderer: DMABuf (Supported buffers: Shared Memory) ¦ Native interface: EGL ¦ GL_RENDERER: llvmpipe (LLVM 20.1.2, 256 bits) |
| defaults-2.json | - | always | GskGLRenderer | 256.2 | 295.8 | Policy: always ¦ 2D canvas: Accelerated ¦ Renderer: DMABuf (Supported buffers: Shared Memory) ¦ Native interface: EGL ¦ GL_RENDERER: llvmpipe (LLVM 20.1.2, 256 bits) |
| defaults-3.json | - | always | GskGLRenderer | 257.2 | 311.4 | Policy: always ¦ 2D canvas: Accelerated ¦ Renderer: DMABuf (Supported buffers: Shared Memory) ¦ Native interface: EGL ¦ GL_RENDERER: llvmpipe (LLVM 20.1.2, 256 bits) |
| dmabuf-off-cairo-1.json | GSK_RENDERER=cairo WEBKIT_DISABLE_DMABUF_RENDERER=1 | never | GskCairoRenderer | 72.9 | 181.5 | Policy: never ¦ 2D canvas: Accelerated |
| dmabuf-off-cairo-2.json | GSK_RENDERER=cairo WEBKIT_DISABLE_DMABUF_RENDERER=1 | never | GskCairoRenderer | 72.8 | 191.9 | Policy: never ¦ 2D canvas: Accelerated |
| dmabuf-off-cairo-3.json | GSK_RENDERER=cairo WEBKIT_DISABLE_DMABUF_RENDERER=1 | never | GskCairoRenderer | 72.8 | 198.6 | Policy: never ¦ 2D canvas: Accelerated |
| never-cairo-1.json | GSK_RENDERER=cairo | never | GskCairoRenderer | 82.8 | 199.2 | Policy: never ¦ 2D canvas: Accelerated |
| never-cairo-2.json | GSK_RENDERER=cairo | never | GskCairoRenderer | 82.8 | 203.8 | Policy: never ¦ 2D canvas: Accelerated |
| never-cairo-3.json | GSK_RENDERER=cairo | never | GskCairoRenderer | 82.5 | 201.2 | Policy: never ¦ 2D canvas: Accelerated |
| never-only-1.json | - | never | GskGLRenderer | 140.4 | 252.6 | Policy: never ¦ 2D canvas: Accelerated |
| never-only-2.json | - | never | GskGLRenderer | 140.9 | 243.7 | Policy: never ¦ 2D canvas: Accelerated |
| never-only-3.json | - | never | GskGLRenderer | 140.7 | 255.1 | Policy: never ¦ 2D canvas: Accelerated |

## Versions

```
6.18.58.1-microsoft-standard-WSL2
PRETTY_NAME="Ubuntu 24.04.5 LTS"
<user> adm cdrom sudo dip plugdev users
crw-rw-rw- 1 root root 10, 258 Nov 19 21:45 /dev/dxg

/dev/dri:
total 0
crw-rw---- 1 root video  226,   0 Nov 19 21:45 card0
crw-rw---- 1 root render 226, 128 Nov 19 21:45 renderD128
libwebkitgtk-6.0-4:amd64 2.52.6-0ubuntu0.24.04.1
mesa-libgallium:amd64 25.2.8-0ubuntu0.24.04.4
32
```

