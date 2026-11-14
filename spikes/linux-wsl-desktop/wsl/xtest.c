// XTEST input for the WSLg spike (X11 backend under XWayland). Finds a window by title and sends synthetic input
// through the XTEST extension. libXtst is loaded at run time (only libxtst6 is installed, without headers).
// Build: clang -O2 -o xtest xtest.c -lX11 -ldl
// Run:   xtest <title> info | key <keysym-name> | click <x> <y>   (x, y relative to the window)
#include "xfind.h"
#include <dlfcn.h>
#include <stdio.h>
#include <stdlib.h>
#include <time.h>

typedef Bool (*query_fn)(Display *, int *, int *, int *, int *);
typedef int (*key_fn)(Display *, unsigned int, Bool, unsigned long);
typedef int (*button_fn)(Display *, unsigned int, Bool, unsigned long);
typedef int (*motion_fn)(Display *, int, int, int, unsigned long);

int main(int argc, char **argv) {
  if (argc < 3) { fprintf(stderr, "usage: xtest <title> info|key K|click X Y\n"); return 2; }
  Display *d = XOpenDisplay(NULL);
  if (!d) { fprintf(stderr, "xtest: no display\n"); return 1; }
  XSetErrorHandler(xignore);
  void *lib = dlopen("libXtst.so.6", RTLD_NOW);
  if (!lib) { printf("XTEST_LIB_MISSING %s\n", dlerror()); return 1; }
  query_fn query = (query_fn)dlsym(lib, "XTestQueryExtension");
  key_fn key = (key_fn)dlsym(lib, "XTestFakeKeyEvent");
  button_fn button = (button_fn)dlsym(lib, "XTestFakeButtonEvent");
  motion_fn motion = (motion_fn)dlsym(lib, "XTestFakeMotionEvent");
  int ev, er, major, minor;
  if (!query(d, &ev, &er, &major, &minor)) { printf("XTEST_EXTENSION_MISSING\n"); return 1; }
  Window w = xfind(d, argv[1]);
  if (!w) { printf("XTEST_WINDOW_NOT_FOUND %s\n", argv[1]); return 1; }
  int rx, ry;
  Window child, focus;
  int revert;
  XTranslateCoordinates(d, w, DefaultRootWindow(d), 0, 0, &rx, &ry, &child);
  XGetInputFocus(d, &focus, &revert);
  printf("XTEST version=%d.%d window=0x%lx root-origin=%d,%d input-focus=0x%lx\n", major, minor, w, rx, ry, focus);
  if (!strcmp(argv[2], "key") && argc >= 4) {
    KeyCode code = XKeysymToKeycode(d, XStringToKeysym(argv[3]));
    key(d, code, True, CurrentTime);
    key(d, code, False, CurrentTime);
    printf("XTEST key %s code=%d\n", argv[3], code);
  } else if (!strcmp(argv[2], "click") && argc >= 5) {
    int x = atoi(argv[3]), y = atoi(argv[4]);
    motion(d, -1, rx + x, ry + y, CurrentTime);
    XSync(d, False);
    struct timespec s = {0, 30000000};
    nanosleep(&s, NULL);
    button(d, 1, True, CurrentTime);
    button(d, 1, False, CurrentTime);
    printf("XTEST click %d,%d (root %d,%d)\n", x, y, rx + x, ry + y);
  }
  XSync(d, False);
  XCloseDisplay(d);
  return 0;
}
