// Captures a region of an X display (Xvfb in the container) to PNG, and counts pixels of the tray icon's colours:
// blue (30,144,255) = normal, red (230,60,40) = NeedsAttention, yellow (240,180,0) = "busy" icon.
// Build: clang -O2 -Wall -o xcap xcap.c -lX11 -lz
// Run:   xcap OUT.png [X Y W H]
#include <X11/Xlib.h>
#include <X11/Xutil.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <zlib.h>

static void put32(unsigned char *p, unsigned v) { p[0] = v >> 24; p[1] = v >> 16; p[2] = v >> 8; p[3] = v; }

static void chunk(FILE *f, const char *type, const unsigned char *data, unsigned len) {
  unsigned char head[8];
  put32(head, len);
  memcpy(head + 4, type, 4);
  fwrite(head, 1, 8, f);
  if (len) fwrite(data, 1, len, f);
  unsigned crc = crc32(0, (const unsigned char *)type, 4);
  crc = crc32(crc, data, len);
  unsigned char c[4];
  put32(c, crc);
  fwrite(c, 1, 4, f);
}

static int near(int a, int b) { return abs(a - b) <= 12; }

int main(int argc, char **argv) {
  if (argc < 2) { fprintf(stderr, "usage: xcap OUT.png [X Y W H]\n"); return 2; }
  Display *d = XOpenDisplay(NULL);
  if (!d) { printf("XCAP no-display\n"); return 1; }
  Window root = DefaultRootWindow(d);
  XWindowAttributes a;
  XGetWindowAttributes(d, root, &a);
  int x = 0, y = 0, w = a.width, h = a.height;
  if (argc >= 6) { x = atoi(argv[2]); y = atoi(argv[3]); w = atoi(argv[4]); h = atoi(argv[5]); }
  XImage *img = XGetImage(d, root, x, y, w, h, AllPlanes, ZPixmap);
  if (!img) { printf("XCAP no-image\n"); return 1; }
  size_t raw_len = (size_t)(w * 3 + 1) * h;
  unsigned char *raw = malloc(raw_len);
  long blue = 0, red = 0, yellow = 0;
  int minx = w, miny = h, maxx = -1, maxy = -1;
  for (int j = 0; j < h; j++) {
    unsigned char *row = raw + (size_t)j * (w * 3 + 1);
    row[0] = 0;
    for (int i = 0; i < w; i++) {
      unsigned long p = XGetPixel(img, i, j);
      int r = (p >> 16) & 255, g = (p >> 8) & 255, b = p & 255;
      row[1 + i * 3] = r; row[2 + i * 3] = g; row[3 + i * 3] = b;
      int hit = 0;
      if (near(r, 30) && near(g, 144) && near(b, 255)) { blue++; hit = 1; }
      if (near(r, 230) && near(g, 60) && near(b, 40)) { red++; hit = 1; }
      if (near(r, 240) && near(g, 180) && near(b, 0)) { yellow++; hit = 1; }
      if (hit) { if (i < minx) minx = i; if (j < miny) miny = j; if (i > maxx) maxx = i; if (j > maxy) maxy = j; }
    }
  }
  uLongf zlen = compressBound(raw_len);
  unsigned char *z = malloc(zlen);
  compress2(z, &zlen, raw, raw_len, 6);
  FILE *f = fopen(argv[1], "wb");
  if (!f) { printf("XCAP cannot write %s\n", argv[1]); return 1; }
  fwrite("\x89PNG\r\n\x1a\n", 1, 8, f);
  unsigned char ihdr[13];
  put32(ihdr, w); put32(ihdr + 4, h);
  ihdr[8] = 8; ihdr[9] = 2; ihdr[10] = 0; ihdr[11] = 0; ihdr[12] = 0;
  chunk(f, "IHDR", ihdr, 13);
  chunk(f, "IDAT", z, zlen);
  chunk(f, "IEND", NULL, 0);
  fclose(f);
  printf("XCAP %s region=%d,%d,%dx%d blue=%ld red=%ld yellow=%ld icon-centre=%d,%d\n", argv[1], x, y, w, h, blue, red, yellow,
         maxx >= 0 ? x + (minx + maxx) / 2 : -1, maxy >= 0 ? y + (miny + maxy) / 2 : -1);
  return 0;
}
