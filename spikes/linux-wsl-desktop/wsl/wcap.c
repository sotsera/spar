// X11 window capture for the WSLg spike. XWayland under WSLg is rootless: the root window holds no picture of the
// desktop, so this grabs one window (found by title) with XGetImage as fast as possible, classifies a central region
// (dominant colour; white, light #f3f3f3, dark #1f1f1f, black shares), writes one JSON line per frame with a
// CLOCK_MONOTONIC stamp (merged with the host's marks) and a PNG when the class changes.
// Build: clang -O2 -o wcap wcap.c -lX11 -lz
// Run:   wcap <out-dir> <title-substring> <duration-ms>   (waits for the window; stops on SIGTERM or when it is gone)
#include "xfind.h"
#include <zlib.h>
#include <signal.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <time.h>

static volatile sig_atomic_t stop_requested = 0;
static void on_signal(int sig) { (void)sig; stop_requested = 1; }
static double now_ms(void) { struct timespec ts; clock_gettime(CLOCK_MONOTONIC, &ts); return ts.tv_sec * 1000.0 + ts.tv_nsec / 1e6; }
static int near(int r, int g, int b, uint32_t c, int t) { return abs(r - (int)(c >> 16)) <= t && abs(g - (int)((c >> 8) & 0xff)) <= t && abs(b - (int)(c & 0xff)) <= t; }

static const char *classify(XImage *img, uint32_t *dom, double *shares) {
  static int bins[32768];
  memset(bins, 0, sizeof bins);
  long w = 0, l = 0, k = 0, b = 0, n = 0;
  int x0 = img->width * 15 / 100, x1 = img->width * 85 / 100, y0 = img->height * 25 / 100, y1 = img->height * 85 / 100;
  for (int y = y0; y < y1; y += 3) {
    uint32_t *row = (uint32_t *)(img->data + (size_t)y * img->bytes_per_line);
    for (int x = x0; x < x1; x += 3) {
      uint32_t p = row[x] & 0xffffff;
      int R = p >> 16, G = (p >> 8) & 0xff, B = p & 0xff;
      n++;
      if (R >= 0xfa && G >= 0xfa && B >= 0xfa) w++;
      else if (near(R, G, B, 0xf3f3f3, 2)) l++;
      else if (near(R, G, B, 0x1f1f1f, 2)) k++;
      else if (R <= 8 && G <= 8 && B <= 8) b++;
      bins[(R >> 3 << 10) | (G >> 3 << 5) | (B >> 3)]++;
    }
  }
  int top = 0;
  for (int i = 1; i < 32768; i++) if (bins[i] > bins[top]) top = i;
  *dom = ((uint32_t)(top >> 10) << 19) | ((uint32_t)((top >> 5) & 31) << 11) | ((uint32_t)(top & 31) << 3);
  double t = n ? (double)n : 1;
  shares[0] = w / t; shares[1] = l / t; shares[2] = k / t; shares[3] = b / t;
  return shares[0] > 0.5 ? "white" : shares[1] > 0.5 ? "light" : shares[2] > 0.5 ? "dark" : shares[3] > 0.5 ? "black" : "other";
}

static void put32(unsigned char *p, uint32_t v) { p[0] = v >> 24; p[1] = v >> 16; p[2] = v >> 8; p[3] = v; }

static void chunk(FILE *f, const char *type, const unsigned char *data, uint32_t len) {
  unsigned char head[8];
  put32(head, len);
  memcpy(head + 4, type, 4);
  fwrite(head, 1, 8, f);
  if (len) fwrite(data, 1, len, f);
  uLong crc = crc32(0, (const Bytef *)type, 4);
  if (len) crc = crc32(crc, data, len);
  unsigned char tail[4];
  put32(tail, (uint32_t)crc);
  fwrite(tail, 1, 4, f);
}

static void write_png(const char *path, XImage *img) {
  int w = img->width, h = img->height;
  size_t rawlen = (size_t)h * (1 + w * 3);
  unsigned char *raw = malloc(rawlen);
  for (int y = 0; y < h; y++) {
    unsigned char *row = raw + (size_t)y * (1 + w * 3);
    uint32_t *src = (uint32_t *)(img->data + (size_t)y * img->bytes_per_line);
    row[0] = 0;
    for (int x = 0; x < w; x++) { uint32_t p = src[x]; row[1 + x * 3] = p >> 16; row[2 + x * 3] = p >> 8; row[3 + x * 3] = p; }
  }
  uLongf zlen = compressBound(rawlen);
  unsigned char *z = malloc(zlen);
  compress2(z, &zlen, raw, rawlen, 1);
  FILE *f = fopen(path, "wb");
  if (f) {
    static const unsigned char sig[8] = {0x89, 'P', 'N', 'G', 0x0d, 0x0a, 0x1a, 0x0a};
    unsigned char ihdr[13] = {0};
    fwrite(sig, 1, 8, f);
    put32(ihdr, w); put32(ihdr + 4, h); ihdr[8] = 8; ihdr[9] = 2;
    chunk(f, "IHDR", ihdr, 13);
    chunk(f, "IDAT", z, (uint32_t)zlen);
    chunk(f, "IEND", NULL, 0);
    fclose(f);
  }
  free(z);
  free(raw);
}

int main(int argc, char **argv) {
  if (argc < 4) { fprintf(stderr, "usage: wcap <out-dir> <title> <duration-ms>\n"); return 2; }
  const char *dir = argv[1], *title = argv[2];
  double duration = atof(argv[3]);
  signal(SIGTERM, on_signal);
  signal(SIGINT, on_signal);
  Display *d = XOpenDisplay(NULL);
  if (!d) { fprintf(stderr, "wcap: cannot open display\n"); return 1; }
  XSetErrorHandler(xignore);
  char path[4096];
  snprintf(path, sizeof path, "%s/x11-frames.jsonl", dir);
  FILE *out = fopen(path, "w");
  if (!out) return 1;
  double start = now_ms();
  Window w = 0;
  while (!stop_requested && !(w = xfind(d, title))) {
    if (now_ms() - start > 20000) break;
    struct timespec s = {0, 500000};
    nanosleep(&s, NULL);
  }
  if (!w) { fprintf(out, "{\"error\":\"window not found\"}\n"); fclose(out); return 1; }
  fprintf(out, "{\"found\":\"0x%lx\",\"mono\":%.3f}\n", w, now_ms());
  char last[16] = "";
  int count = 0, pngs = 0, misses = 0;
  double found = now_ms();
  while (!stop_requested && now_ms() - found < duration) {
    XWindowAttributes a;
    if (!XGetWindowAttributes(d, w, &a)) break;
    double at = now_ms();
    XImage *img = a.map_state == IsViewable ? XGetImage(d, w, 0, 0, a.width, a.height, AllPlanes, ZPixmap) : NULL;
    double took = now_ms() - at;
    if (!img) {
      if (misses++ < 5) fprintf(out, "{\"mono\":%.3f,\"unviewable\":%d}\n", at, a.map_state);
      continue;
    }
    uint32_t dom;
    double s[4];
    const char *cls = classify(img, &dom, s);
    char png[64] = "";
    if (strcmp(cls, last) != 0 && pngs < 40) {
      snprintf(png, sizeof png, "x11-%04d.png", count);
      snprintf(path, sizeof path, "%s/%s", dir, png);
      write_png(path, img);
      pngs++;
    }
    snprintf(last, sizeof last, "%s", cls);
    fprintf(out, "{\"i\":%d,\"mono\":%.3f,\"took\":%.1f,\"size\":\"%dx%d\",\"class\":\"%s\",\"dom\":\"#%06x\",\"white\":%.3f,\"light\":%.3f,\"dark\":%.3f,\"black\":%.3f%s%s%s}\n",
            count, at, took, a.width, a.height, cls, dom, s[0], s[1], s[2], s[3], png[0] ? ",\"png\":\"" : "", png, png[0] ? "\"" : "");
    XDestroyImage(img);
    count++;
  }
  fclose(out);
  fprintf(stderr, "wcap: %d frames\n", count);
  return 0;
}
