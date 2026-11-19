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
| `gdk` | cold | 3 | 109.54 (103.58–113.07) | 116.35 (113.76–120.75) | 98.8 | software | llvmpipe (LLVM 20.1.2, 256 bits) |

Warm phase medians (ms):


