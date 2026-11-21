# ubuntu-dxg-render

## series.jsonl

| Method | Kind | n | Detect ms median (range) | Process wall ms median | RSS growth MB median | Verdict | Renderer / reason |
|---|---|---|---|---|---|---|---|
| `none` | cold | 3 | 0.00 (0.00–0.00) | 2.96 (2.17–3.05) | 0.0 | unknown |  |
| `node` | cold | 3 | 0.56 (0.42–0.62) | 3.51 (3.32–3.68) | 0.2 | software | only virtual-driver nodes are openable |
| `combined` | cold | 3 | 35.81 (33.23–93.94) | 39.86 (36.24–98.26) | 69.2 | software | llvmpipe (LLVM 20.1.2, 256 bits); EGL driver swrast |
| `devices` | cold | 3 | 66.75 (21.56–69.59) | 71.95 (25.64–75.05) | 54.2 | unknown |  |
| `egl --platform surfaceless --no-context` | cold | 3 | 71.59 (71.29–73.41) | 77.42 (77.28–79.20) | 55.9 | software | driver name 'swrast'; EGL driver swrast |
| `egl --platform surfaceless` | cold | 3 | 83.93 (83.49–84.93) | 88.54 (88.31–89.57) | 73.4 | software | llvmpipe (LLVM 20.1.2, 256 bits); EGL driver swrast |
| `egl --platform device` | cold | 3 | 48.56 (34.65–83.69) | 53.11 (37.17–88.33) | 74.1 | software | llvmpipe (LLVM 20.1.2, 256 bits); EGL driver swrast |
| `egl --platform default` | cold | 3 | 67.84 (23.61–70.43) | 73.80 (27.19–76.20) | 54.1 | software | no GL renderer: eglInitialize failed (0x3001); error: eglInitialize failed (0x3001) |
| `none` | warm | 25 | 0.00 (0.00–0.00) | 1.29 (1.19–1.40) | 0.0 | unknown |  |
| `node` | warm | 25 | 0.17 (0.16–0.18) | 1.33 (1.20–1.72) | 0.2 | software | only virtual-driver nodes are openable |
| `combined` | warm | 25 | 20.82 (20.18–24.77) | 22.29 (21.67–26.33) | 73.7 | software | llvmpipe (LLVM 20.1.2, 256 bits); EGL driver swrast |
| `devices` | warm | 25 | 9.24 (8.77–11.23) | 11.97 (10.89–14.12) | 54.3 | unknown |  |
| `egl --platform surfaceless --no-context` | warm | 25 | 10.49 (9.85–12.55) | 13.22 (12.09–15.58) | 56.0 | software | driver name 'swrast'; EGL driver swrast |
| `egl --platform surfaceless` | warm | 25 | 20.47 (19.71–23.04) | 22.04 (21.20–24.68) | 73.5 | software | llvmpipe (LLVM 20.1.2, 256 bits); EGL driver swrast |
| `egl --platform device` | warm | 25 | 20.60 (19.61–23.93) | 22.21 (21.20–25.50) | 73.6 | software | llvmpipe (LLVM 20.1.2, 256 bits); EGL driver swrast |
| `egl --platform default` | warm | 25 | 9.07 (8.66–10.46) | 11.60 (10.81–13.35) | 54.3 | software | no GL renderer: eglInitialize failed (0x3001); error: eglInitialize failed (0x3001) |

Warm phase medians (ms):

- `node`: node 0.17;
- `combined`: node 0.16; load 0.15; get-display 8.82; initialize 1.18; query-driver 0.01; context-and-gl-strings 9.14; teardown 1.13;
- `devices`: load 0.20; enumerate 9.03;
- `egl --platform surfaceless --no-context`: load 0.19; get-display 9.04; initialize 1.19; query-driver 0.01; teardown 0.02;
- `egl --platform surfaceless`: load 0.20; get-display 8.83; initialize 1.18; query-driver 0.01; context-and-gl-strings 9.04; teardown 1.10;
- `egl --platform device`: load 0.20; get-display 9.08; initialize 1.08; query-driver 0.01; context-and-gl-strings 9.04; teardown 1.11;
- `egl --platform default`: load 0.20; get-display 8.87;

## gdk.jsonl

| Method | Kind | n | Detect ms median (range) | Process wall ms median | RSS growth MB median | Verdict | Renderer / reason |
|---|---|---|---|---|---|---|---|
| `gdk` | cold | 3 | 93.56 (90.42–103.13) | 101.71 (99.09–110.63) | 91.3 | software | llvmpipe (LLVM 20.1.2, 256 bits) |
| `gdk --window` | cold | 3 | 157.45 (155.98–421.55) | 169.15 (167.22–433.15) | 154.1 | software | llvmpipe (LLVM 20.1.2, 256 bits); window: GskGLRenderer |
| `gdk --window --gsk-after-init cairo` | cold | 3 | 99.07 (98.75–102.21) | 105.29 (105.17–111.63) | 98.3 | software | llvmpipe (LLVM 20.1.2, 256 bits); window: GskCairoRenderer |
| `gdk --pre-egl --window` | cold | 3 | 161.69 (160.54–162.99) | 171.97 (170.36–174.17) | 159.9 | software | llvmpipe (LLVM 20.1.2, 256 bits); window: GskGLRenderer |
| `gdk` | warm | 25 | 39.50 (37.43–43.19) | 42.17 (39.86–46.21) | 95.5 | software | llvmpipe (LLVM 20.1.2, 256 bits) |
| `gdk --window` | warm | 25 | 102.56 (100.09–110.64) | 109.52 (103.25–117.02) | 155.1 | software | llvmpipe (LLVM 20.1.2, 256 bits); window: GskGLRenderer |
| `gdk --window --gsk-after-init cairo` | warm | 25 | 47.96 (45.48–53.73) | 50.90 (48.10–57.90) | 100.3 | software | llvmpipe (LLVM 20.1.2, 256 bits); window: GskCairoRenderer |
| `gdk --pre-egl --window` | warm | 25 | 109.50 (104.86–123.10) | 116.87 (111.85–130.18) | 161.5 | software | llvmpipe (LLVM 20.1.2, 256 bits); window: GskGLRenderer |

Warm phase medians (ms):

- `gdk`: gtk-init 32.91; gl-context-and-strings 6.56;
- `gdk --window`: gtk-init 32.57; gl-context-and-strings 6.42; window-realize 63.19;
- `gdk --window --gsk-after-init cairo`: gtk-init 32.98; gl-context-and-strings 6.61; window-realize 8.12;
- `gdk --pre-egl --window`: load 0.20; get-display 9.03; initialize 1.18; query-driver 0.01; context-and-gl-strings 9.28; teardown 1.14; pre-egl 0.00; gtk-init 16.52; gl-context-and-strings 6.32; window-realize 65.01;

## Acceleration default

| Run | Env | Settings policy | GSK renderer | Private MB (all processes) | Load finished ms | webkit://gpu excerpt |
|---|---|---|---|---|---|---|
| defaults-1.json | - | always | GskGLRenderer | 321.4 | 484.4 | Policy: always ¦ 2D canvas: Accelerated ¦ Renderer: DMABuf (Supported buffers: Shared Memory) ¦ Native interface: EGL ¦ GL_RENDERER: llvmpipe (LLVM 20.1.2, 256 bits) |
| defaults-2.json | - | always | GskGLRenderer | 254.5 | 185.2 | Policy: always ¦ 2D canvas: Accelerated ¦ Renderer: DMABuf (Supported buffers: Shared Memory) ¦ Native interface: EGL ¦ GL_RENDERER: llvmpipe (LLVM 20.1.2, 256 bits) |
| defaults-3.json | - | always | GskGLRenderer | 255.9 | 187.3 | Policy: always ¦ 2D canvas: Accelerated ¦ Renderer: DMABuf (Supported buffers: Shared Memory) ¦ Native interface: EGL ¦ GL_RENDERER: llvmpipe (LLVM 20.1.2, 256 bits) |
| dmabuf-off-cairo-1.json | GSK_RENDERER=cairo WEBKIT_DISABLE_DMABUF_RENDERER=1 | never | GskCairoRenderer | 72.6 | 91.6 | Policy: never ¦ 2D canvas: Accelerated |
| dmabuf-off-cairo-2.json | GSK_RENDERER=cairo WEBKIT_DISABLE_DMABUF_RENDERER=1 | never | GskCairoRenderer | 72.5 | 85.0 | Policy: never ¦ 2D canvas: Accelerated |
| dmabuf-off-cairo-3.json | GSK_RENDERER=cairo WEBKIT_DISABLE_DMABUF_RENDERER=1 | never | GskCairoRenderer | 72.4 | 87.3 | Policy: never ¦ 2D canvas: Accelerated |
| never-cairo-1.json | GSK_RENDERER=cairo | never | GskCairoRenderer | 82.5 | 105.1 | Policy: never ¦ 2D canvas: Accelerated |
| never-cairo-2.json | GSK_RENDERER=cairo | never | GskCairoRenderer | 84.5 | 104.0 | Policy: never ¦ 2D canvas: Accelerated |
| never-cairo-3.json | GSK_RENDERER=cairo | never | GskCairoRenderer | 82.6 | 101.2 | Policy: never ¦ 2D canvas: Accelerated |
| never-only-1.json | - | never | GskGLRenderer | 140.5 | 146.4 | Policy: never ¦ 2D canvas: Accelerated |
| never-only-2.json | - | never | GskGLRenderer | 139.9 | 145.3 | Policy: never ¦ 2D canvas: Accelerated |
| never-only-3.json | - | never | GskGLRenderer | 140.6 | 146.1 | Policy: never ¦ 2D canvas: Accelerated |

## Versions

```
uid=1000(ubuntu) gid=1000(ubuntu) groups=1000(ubuntu),4(adm),20(dialout),24(cdrom),25(floppy),27(sudo),29(audio),30(dip),44(video),46(plugdev),992
6.18.58.1-microsoft-standard-WSL2
PRETTY_NAME="Ubuntu 24.04.5 LTS"
crw-rw-rw- 1 root root 10, 258 Nov 19 20:41 /dev/dxg

/dev/dri:
total 0
crw-rw---- 1 root video 226,   0 Nov 19 20:41 card0
crw-rw---- 1 root   992 226, 128 Nov 19 20:41 renderD128
libwebkitgtk-6.0-4:amd64 2.52.6-0ubuntu0.24.04.2
mesa-libgallium:amd64 25.2.8-0ubuntu0.24.04.5
```

