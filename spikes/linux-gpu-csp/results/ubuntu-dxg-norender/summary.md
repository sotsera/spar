# ubuntu-dxg-norender

## series.jsonl

| Method | Kind | n | Detect ms median (range) | Process wall ms median | RSS growth MB median | Verdict | Renderer / reason |
|---|---|---|---|---|---|---|---|
| `none` | cold | 3 | 0.00 (0.00–0.00) | 2.89 (2.10–3.06) | 0.0 | unknown |  |
| `node` | cold | 3 | 0.10 (0.10–0.11) | 3.08 (2.88–3.13) | 0.2 | software | no render node is openable |
| `combined` | cold | 3 | 0.10 (0.10–0.10) | 3.10 (3.07–3.15) | 0.2 | software | no openable render node; EGL skipped |
| `devices` | cold | 3 | 70.62 (68.74–70.98) | 76.27 (75.16–77.31) | 54.3 | unknown |  |
| `egl --platform surfaceless --no-context` | cold | 3 | 74.88 (72.91–76.28) | 81.24 (78.63–82.57) | 55.9 | software | driver name 'swrast'; EGL driver swrast |
| `egl --platform surfaceless` | cold | 3 | 84.18 (35.47–94.15) | 88.86 (38.97–100.66) | 74.2 | software | llvmpipe (LLVM 20.1.2, 256 bits); EGL driver swrast |
| `egl --platform device` | cold | 3 | 44.18 (33.52–86.00) | 49.00 (36.01–90.83) | 68.4 | software | llvmpipe (LLVM 20.1.2, 256 bits); EGL driver swrast |
| `egl --platform default` | cold | 3 | 69.95 (22.79–72.42) | 76.13 (28.25–78.17) | 54.3 | software | no GL renderer: eglInitialize failed (0x3001); error: eglInitialize failed (0x3001) |
| `none` | warm | 25 | 0.00 (0.00–0.00) | 1.30 (1.22–1.38) | 0.0 | unknown |  |
| `node` | warm | 25 | 0.09 (0.08–0.10) | 1.24 (1.13–1.70) | 0.2 | software | no render node is openable |
| `combined` | warm | 25 | 0.09 (0.08–0.16) | 1.25 (1.14–1.37) | 0.2 | software | no openable render node; EGL skipped |
| `devices` | warm | 25 | 9.30 (8.93–10.12) | 11.68 (11.06–13.09) | 54.3 | unknown |  |
| `egl --platform surfaceless --no-context` | warm | 25 | 10.46 (9.85–12.82) | 13.03 (12.55–15.62) | 56.0 | software | driver name 'swrast'; EGL driver swrast |
| `egl --platform surfaceless` | warm | 25 | 21.04 (19.70–22.38) | 22.73 (21.26–24.02) | 73.6 | software | llvmpipe (LLVM 20.1.2, 256 bits); EGL driver swrast |
| `egl --platform device` | warm | 25 | 20.98 (20.00–22.06) | 22.57 (21.57–23.75) | 73.5 | software | llvmpipe (LLVM 20.1.2, 256 bits); EGL driver swrast |
| `egl --platform default` | warm | 25 | 9.31 (9.03–10.24) | 12.05 (11.52–13.03) | 54.5 | software | no GL renderer: eglInitialize failed (0x3001); error: eglInitialize failed (0x3001) |

Warm phase medians (ms):

- `node`: node 0.09;
- `combined`: node 0.09;
- `devices`: load 0.18; enumerate 9.12;
- `egl --platform surfaceless --no-context`: load 0.19; get-display 9.00; initialize 1.19; query-driver 0.01; teardown 0.02;
- `egl --platform surfaceless`: load 0.19; get-display 9.10; initialize 1.21; query-driver 0.01; context-and-gl-strings 9.24; teardown 1.12;
- `egl --platform device`: load 0.19; get-display 9.02; initialize 1.09; query-driver 0.01; context-and-gl-strings 9.29; teardown 1.09;
- `egl --platform default`: load 0.20; get-display 9.11;

## gdk.jsonl

| Method | Kind | n | Detect ms median (range) | Process wall ms median | RSS growth MB median | Verdict | Renderer / reason |
|---|---|---|---|---|---|---|---|
| `gdk` | cold | 3 | 90.28 (89.97–121.74) | 98.95 (96.45–128.97) | 91.2 | software | llvmpipe (LLVM 20.1.2, 256 bits) |
| `gdk --window` | cold | 3 | 155.37 (152.86–419.41) | 166.38 (163.65–431.38) | 153.2 | software | llvmpipe (LLVM 20.1.2, 256 bits); window: GskGLRenderer |
| `gdk --window --gsk-after-init cairo` | cold | 3 | 100.49 (99.76–101.15) | 109.09 (107.21–110.41) | 98.6 | software | llvmpipe (LLVM 20.1.2, 256 bits); window: GskCairoRenderer |
| `gdk --pre-egl --window` | cold | 3 | 165.18 (162.08–170.12) | 175.91 (173.18–180.95) | 161.2 | software | llvmpipe (LLVM 20.1.2, 256 bits); window: GskGLRenderer |
| `gdk` | warm | 25 | 38.97 (34.33–43.93) | 41.64 (36.69–52.25) | 95.5 | software | llvmpipe (LLVM 20.1.2, 256 bits) |
| `gdk --window` | warm | 25 | 102.59 (99.73–119.22) | 109.60 (105.73–125.88) | 155.7 | software | llvmpipe (LLVM 20.1.2, 256 bits); window: GskGLRenderer |
| `gdk --window --gsk-after-init cairo` | warm | 25 | 47.15 (44.61–52.65) | 49.72 (46.99–55.24) | 100.3 | software | llvmpipe (LLVM 20.1.2, 256 bits); window: GskCairoRenderer |
| `gdk --pre-egl --window` | warm | 25 | 107.44 (104.48–122.41) | 114.85 (111.83–129.36) | 161.7 | software | llvmpipe (LLVM 20.1.2, 256 bits); window: GskGLRenderer |

Warm phase medians (ms):

- `gdk`: gtk-init 32.51; gl-context-and-strings 6.50;
- `gdk --window`: gtk-init 32.44; gl-context-and-strings 6.48; window-realize 63.54;
- `gdk --window --gsk-after-init cairo`: gtk-init 32.68; gl-context-and-strings 6.47; window-realize 7.98;
- `gdk --pre-egl --window`: load 0.20; get-display 8.92; initialize 1.16; query-driver 0.01; context-and-gl-strings 9.10; teardown 1.11; pre-egl 0.00; gtk-init 16.31; gl-context-and-strings 6.22; window-realize 64.74;

## Versions

```
uid=1000(ubuntu) gid=1000(ubuntu) groups=1000(ubuntu),4(adm),20(dialout),24(cdrom),25(floppy),27(sudo),29(audio),30(dip),44(video),46(plugdev)
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

