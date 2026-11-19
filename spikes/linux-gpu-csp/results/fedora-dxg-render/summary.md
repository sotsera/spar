# fedora-dxg-render

## series.jsonl

| Method | Kind | n | Detect ms median (range) | Process wall ms median | RSS growth MB median | Verdict | Renderer / reason |
|---|---|---|---|---|---|---|---|
| `none` | cold | 3 | 0.00 (0.00–0.00) | 3.25 (2.80–3.62) | 0.0 | unknown |  |
| `node` | cold | 3 | 0.53 (0.45–0.66) | 4.22 (3.67–4.45) | 0.2 | software | only virtual-driver nodes are openable |
| `combined` | cold | 3 | 84.37 (67.71–104.28) | 89.94 (71.27–111.17) | 54.2 | software | llvmpipe (LLVM 21.1.8, 256 bits); EGL driver swrast |
| `devices` | cold | 3 | 66.74 (16.32–70.69) | 73.76 (21.64–77.67) | 33.2 | unknown |  |
| `egl --platform surfaceless --no-context` | cold | 3 | 73.41 (71.67–74.12) | 80.61 (78.15–81.19) | 34.6 | software | driver name 'swrast'; EGL driver swrast |
| `egl --platform surfaceless` | cold | 3 | 86.46 (64.92–92.92) | 92.03 (70.20–98.85) | 54.0 | software | llvmpipe (LLVM 21.1.8, 256 bits); EGL driver swrast |
| `egl --platform device` | cold | 3 | 70.31 (16.05–70.42) | 79.44 (23.86–80.06) | 33.9 | software | no GL renderer: eglInitialize failed (0x3001); error: eglInitialize failed (0x3001) |
| `egl --platform default` | cold | 3 | 68.92 (64.59–70.78) | 76.00 (71.53–77.35) | 33.2 | software | no GL renderer: eglInitialize failed (0x3001); error: eglInitialize failed (0x3001) |
| `none` | warm | 25 | 0.00 (0.00–0.00) | 1.30 (1.17–1.72) | 0.0 | unknown |  |
| `node` | warm | 25 | 0.17 (0.15–0.20) | 1.37 (1.22–1.47) | 0.2 | software | only virtual-driver nodes are openable |
| `combined` | warm | 25 | 22.39 (21.67–23.61) | 23.90 (23.16–25.15) | 54.4 | software | llvmpipe (LLVM 21.1.8, 256 bits); EGL driver swrast |
| `devices` | warm | 25 | 7.37 (7.13–8.00) | 9.61 (9.20–10.49) | 33.3 | unknown |  |
| `egl --platform surfaceless --no-context` | warm | 25 | 11.46 (10.69–11.97) | 13.85 (12.95–14.63) | 35.1 | software | driver name 'swrast'; EGL driver swrast |
| `egl --platform surfaceless` | warm | 25 | 22.38 (20.98–23.43) | 24.03 (22.46–25.09) | 54.4 | software | llvmpipe (LLVM 21.1.8, 256 bits); EGL driver swrast |
| `egl --platform device` | warm | 25 | 7.39 (6.92–7.91) | 12.16 (11.48–12.96) | 34.0 | software | no GL renderer: eglInitialize failed (0x3001); error: eglInitialize failed (0x3001) |
| `egl --platform default` | warm | 25 | 7.28 (6.65–8.78) | 9.54 (8.52–11.29) | 33.4 | software | no GL renderer: eglInitialize failed (0x3001); error: eglInitialize failed (0x3001) |

Warm phase medians (ms):

- `node`: node 0.17;
- `combined`: node 0.16; load 0.14; get-display 7.06; initialize 4.20; query-driver 0.01; context-and-gl-strings 9.72; teardown 1.07;
- `devices`: load 0.19; enumerate 7.18;
- `egl --platform surfaceless --no-context`: load 0.19; get-display 6.92; initialize 4.21; query-driver 0.01; teardown 0.02;
- `egl --platform surfaceless`: load 0.19; get-display 7.04; initialize 4.22; query-driver 0.01; context-and-gl-strings 9.52; teardown 1.10;
- `egl --platform device`: load 0.20; get-display 7.20;
- `egl --platform default`: load 0.19; get-display 7.10;

## gdk.jsonl

| Method | Kind | n | Detect ms median (range) | Process wall ms median | RSS growth MB median | Verdict | Renderer / reason |
|---|---|---|---|---|---|---|---|
| `gdk` | cold | 3 | 129.26 (126.44–174.82) | 139.81 (134.42–184.50) | 84.7 | software | llvmpipe (LLVM 21.1.8, 256 bits) |
| `gdk --window` | cold | 3 | 142.23 (136.82–145.79) | 154.10 (149.31–156.40) | 97.1 | software | llvmpipe (LLVM 21.1.8, 256 bits); window: GskGLRenderer |
| `gdk --window --gsk-after-init cairo` | cold | 3 | 131.83 (129.89–141.78) | 142.04 (141.01–149.75) | 90.0 | software | llvmpipe (LLVM 21.1.8, 256 bits); window: GskCairoRenderer |
| `gdk --pre-egl --window` | cold | 3 | 149.57 (148.22–152.22) | 161.13 (159.39–163.98) | 103.4 | software | llvmpipe (LLVM 21.1.8, 256 bits); window: GskGLRenderer |
| `gdk` | warm | 25 | 42.96 (40.79–48.19) | 45.57 (43.43–50.86) | 88.2 | software | llvmpipe (LLVM 21.1.8, 256 bits) |
| `gdk --window` | warm | 25 | 58.11 (56.39–60.35) | 61.15 (59.08–64.78) | 100.5 | software | llvmpipe (LLVM 21.1.8, 256 bits); window: GskGLRenderer |
| `gdk --window --gsk-after-init cairo` | warm | 25 | 51.69 (49.11–57.48) | 54.95 (51.63–60.18) | 93.6 | software | llvmpipe (LLVM 21.1.8, 256 bits); window: GskCairoRenderer |
| `gdk --pre-egl --window` | warm | 25 | 69.66 (66.54–73.89) | 74.55 (70.39–78.88) | 106.5 | software | llvmpipe (LLVM 21.1.8, 256 bits); window: GskGLRenderer |

Warm phase medians (ms):

- `gdk`: gtk-init 35.48; gl-context-and-strings 7.37;
- `gdk --window`: gtk-init 35.03; gl-context-and-strings 7.18; window-realize 15.88;
- `gdk --window --gsk-after-init cairo`: gtk-init 35.56; gl-context-and-strings 7.19; window-realize 8.62;
- `gdk --pre-egl --window`: load 0.18; get-display 7.06; initialize 4.17; query-driver 0.01; context-and-gl-strings 9.53; teardown 1.09; pre-egl 0.00; gtk-init 25.15; gl-context-and-strings 7.07; window-realize 15.68;

## Versions

```
uid=1000(probe) gid=1000(probe) groups=1000(probe),992
6.18.58.1-microsoft-standard-WSL2
PRETTY_NAME="Fedora Linux 43 (Container Image)"
crw-rw-rw- 1 root root 10, 258 Nov 19 20:41 /dev/dxg

/dev/dri:
total 0
crw-rw---- 1 root  44 226,   0 Nov 19 20:41 card0
crw-rw---- 1 root 992 226, 128 Nov 19 20:41 renderD128
webkitgtk6.0-2.54.2-1.fc43.x86_64
gtk4-4.20.4-1.fc43.x86_64
mesa-dri-drivers-25.3.6-3.fc43.x86_64
mesa-libEGL-25.3.6-3.fc43.x86_64
```

