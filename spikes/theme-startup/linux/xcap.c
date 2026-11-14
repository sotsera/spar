// Frame capture for the Linux host runs (spike). Grabs the X11 root window as fast as XGetImage allows, reduces each
// frame to colour statistics for a client and a title region (the same classes as the Windows capture), and writes
// one JSON line per frame with a CLOCK_MONOTONIC timestamp, so the host's marks can be merged. A PNG (half size) is
// written whenever the frame's signature changes. Xvfb has no compositor: the root window is what a person would see.
//
// Build (in the container): clang -O2 -o xcap xcap.c -lX11 -lz
// Run:   xcap <out-dir> <duration-ms> <cx> <cy> <cw> <ch> [<tx> <ty> <tw> <th>]   (stops early on SIGTERM/SIGINT)

#include <X11/Xlib.h>
#include <X11/Xutil.h>
#include <zlib.h>
#include <signal.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <time.h>

static volatile sig_atomic_t stop_requested = 0;
static void on_signal(int sig) { (void)sig; stop_requested = 1; }

static double now_ms(void) {
  struct timespec ts;
  clock_gettime(CLOCK_MONOTONIC, &ts);
  return ts.tv_sec * 1000.0 + ts.tv_nsec / 1e6;
}

typedef struct { int x, y, w, h; } rect_t;
typedef struct { uint32_t dom; double share, white, light, dark, black, app, splash; } stats_t;

static int near(int r, int g, int b, uint32_t c, int t) {
  return abs(r - (int)(c >> 16)) <= t && abs(g - (int)((c >> 8) & 0xff)) <= t && abs(b - (int)(c & 0xff)) <= t;
}

static stats_t analyse(XImage *img, rect_t r, int *bins) {
  stats_t s = {0};
  long white = 0, light = 0, dark = 0, black = 0, app = 0, splash = 0, total = 0;
  memset(bins, 0, sizeof(int) * 32768);
  for (int y = r.y; y < r.y + r.h && y < img->height; y += 2) {
    uint32_t *row = (uint32_t *)(img->data + (size_t)y * img->bytes_per_line);
    for (int x = r.x; x < r.x + r.w && x < img->width; x += 2) {
      uint32_t p = row[x] & 0xffffff;
      int R = p >> 16, G = (p >> 8) & 0xff, B = p & 0xff;
      bins[(R >> 3 << 10) | (G >> 3 << 5) | (B >> 3)]++;
      total++;
      if (R >= 0xfa && G >= 0xfa && B >= 0xfa) white++;
      else if (near(R, G, B, 0xf3f3f3, 1)) light++;
      else if (near(R, G, B, 0x1f1f1f, 1)) dark++;
      else if (R <= 8 && G <= 8 && B <= 8) black++;
      else if (B - R > 80 && B - G > 40) app++;
      else if (R - B > 50 && G - B > 15 && R >= G) splash++;
    }
  }
  int top = 0;
  for (int i = 1; i < 32768; i++) if (bins[i] > bins[top]) top = i;
  // The dominant colour exactly: the mean of the sampled pixels in the dominant bin (second pass).
  long sr = 0, sg = 0, sb = 0, n = 0;
  for (int y = r.y; y < r.y + r.h && y < img->height; y += 2) {
    uint32_t *row = (uint32_t *)(img->data + (size_t)y * img->bytes_per_line);
    for (int x = r.x; x < r.x + r.w && x < img->width; x += 2) {
      uint32_t p = row[x] & 0xffffff;
      int R = p >> 16, G = (p >> 8) & 0xff, B = p & 0xff;
      if (((R >> 3 << 10) | (G >> 3 << 5) | (B >> 3)) == top) { sr += R; sg += G; sb += B; n++; }
    }
  }
  s.dom = n ? (uint32_t)(((sr / n) << 16) | ((sg / n) << 8) | (sb / n)) : 0;
  double t = total ? (double)total : 1.0;
  s.share = bins[top] / t; s.white = white / t; s.light = light / t; s.dark = dark / t;
  s.black = black / t; s.app = app / t; s.splash = splash / t;
  return s;
}

static void bucket(char *out, stats_t s) {
  sprintf(out, "%06x%s%s", (s.dom >> 3) & 0x1f1f1f, s.app > 0.02 ? "A" : "", s.splash > 0.003 ? "S" : "");
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

static void write_png(const char *path, XImage *img, int scale) {
  int w = img->width / scale, h = img->height / scale;
  size_t rawlen = (size_t)h * (1 + w * 3);
  unsigned char *raw = malloc(rawlen);
  for (int y = 0; y < h; y++) {
    unsigned char *row = raw + (size_t)y * (1 + w * 3);
    uint32_t *src = (uint32_t *)(img->data + (size_t)y * scale * img->bytes_per_line);
    row[0] = 0;
    for (int x = 0; x < w; x++) {
      uint32_t p = src[x * scale];
      row[1 + x * 3] = p >> 16; row[2 + x * 3] = p >> 8; row[3 + x * 3] = p;
    }
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

static void json_stats(FILE *f, const char *name, stats_t s) {
  fprintf(f, "\"%s\":{\"dom\":\"#%06x\",\"domShare\":%.3f,\"white\":%.3f,\"light\":%.3f,\"dark\":%.3f,\"black\":%.3f,\"app\":%.3f,\"splash\":%.3f}",
          name, s.dom, s.share, s.white, s.light, s.dark, s.black, s.app, s.splash);
}

int main(int argc, char **argv) {
  if (argc < 7) {
    fprintf(stderr, "usage: xcap <out-dir> <duration-ms> <cx> <cy> <cw> <ch> [<tx> <ty> <tw> <th>]\n");
    return 2;
  }
  const char *dir = argv[1];
  double duration = atof(argv[2]);
  rect_t client = {atoi(argv[3]), atoi(argv[4]), atoi(argv[5]), atoi(argv[6])};
  rect_t title = {0, 0, 0, 0};
  if (argc >= 11) title = (rect_t){atoi(argv[7]), atoi(argv[8]), atoi(argv[9]), atoi(argv[10])};
  signal(SIGTERM, on_signal);
  signal(SIGINT, on_signal);

  Display *d = XOpenDisplay(NULL);
  if (!d) { fprintf(stderr, "xcap: cannot open display\n"); return 1; }
  Window root = DefaultRootWindow(d);
  XWindowAttributes ra;
  XGetWindowAttributes(d, root, &ra);
  int width = ra.width, height = ra.height;

  char path[4096];
  snprintf(path, sizeof path, "%s/frames.jsonl", dir);
  FILE *out = fopen(path, "w");
  if (!out) { fprintf(stderr, "xcap: cannot write %s\n", path); return 1; }

  int *bins = malloc(sizeof(int) * 32768);
  char last[64] = "", sigbuf[64], b1[32], b2[32];
  int count = 0, pngs = 0;
  double start = now_ms();
  while (!stop_requested && now_ms() - start < duration) {
    double at = now_ms();
    XImage *img = XGetImage(d, root, 0, 0, width, height, AllPlanes, ZPixmap);
    double took = now_ms() - at;
    if (!img) continue;
    stats_t c = analyse(img, client, bins);
    stats_t t = title.w > 0 ? analyse(img, title, bins) : c;
    bucket(b1, c); bucket(b2, t);
    snprintf(sigbuf, sizeof sigbuf, "%s|%s", b1, b2);
    char png[64] = "";
    if (strcmp(sigbuf, last) != 0 && pngs < 120) {
      snprintf(png, sizeof png, "x11-%04d.png", count);
      snprintf(path, sizeof path, "%s/%s", dir, png);
      write_png(path, img, 2);
      pngs++;
    }
    strcpy(last, sigbuf);
    fprintf(out, "{\"i\":%d,\"mono\":%.3f,\"took\":%.1f,", count, at, took);
    json_stats(out, "client", c);
    if (title.w > 0) { fputc(',', out); json_stats(out, "title", t); }
    if (png[0]) fprintf(out, ",\"png\":\"%s\"", png);
    fputs("}\n", out);
    XDestroyImage(img);
    count++;
  }
  fclose(out);
  fprintf(stderr, "xcap: %d frames in %.0f ms\n", count, now_ms() - start);
  return 0;
}
