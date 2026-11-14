/* X11 helper for this spike's own windows (XTEST only against clients this spike started).
   xt find TITLE                          prints "xid x y w h" (root coordinates of the client window)
   xt drag TITLE X Y DX DY STEPS MS       press button 1 at window-relative X,Y, move by DX,DY in STEPS steps of MS ms, release
   xt click TITLE X Y                     click button 1 at window-relative X,Y
   xt close TITLE                         send WM_DELETE_WINDOW (as a window manager's close button does)
   Every action prints CLOCK_MONOTONIC ms so it can be merged with the host's log. */
#include <X11/Xlib.h>
#include <X11/Xatom.h>
#include <X11/extensions/XTest.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <time.h>
#include <unistd.h>

static double mono(void) { struct timespec t; clock_gettime(CLOCK_MONOTONIC, &t); return t.tv_sec * 1000.0 + t.tv_nsec / 1e6; }

static int has_title(Display *d, Window w, const char *title) {
  Atom net = XInternAtom(d, "_NET_WM_NAME", False), utf8 = XInternAtom(d, "UTF8_STRING", False);
  Atom type; int format; unsigned long n, after; unsigned char *data = NULL; int match = 0;
  if (XGetWindowProperty(d, w, net, 0, 1024, False, utf8, &type, &format, &n, &after, &data) == Success && data) {
    match = strcmp((char *)data, title) == 0; XFree(data);
    if (match) return 1;
  }
  char *name = NULL;
  if (XFetchName(d, w, &name) && name) { match = strcmp(name, title) == 0; XFree(name); }
  return match;
}

static Window find(Display *d, Window w, const char *title) {
  if (has_title(d, w, title)) return w;
  Window root, parent, *children = NULL; unsigned int n = 0, i; Window found = 0;
  if (!XQueryTree(d, w, &root, &parent, &children, &n)) return 0;
  for (i = 0; i < n && !found; i++) found = find(d, children[i], title);
  if (children) XFree(children);
  return found;
}

static void origin(Display *d, Window w, int *x, int *y, unsigned *width, unsigned *height) {
  Window root, child; int gx, gy; unsigned border, depth;
  XGetGeometry(d, w, &root, &gx, &gy, width, height, &border, &depth);
  XTranslateCoordinates(d, w, DefaultRootWindow(d), 0, 0, x, y, &child);
}

int main(int argc, char **argv) {
  if (argc < 3) { fprintf(stderr, "usage: xt find|drag|click|close TITLE ...\n"); return 2; }
  Display *d = XOpenDisplay(NULL);
  if (!d) { fprintf(stderr, "no display\n"); return 2; }
  Window w = 0;
  for (int tries = 0; tries < 100 && !w; tries++) { w = find(d, DefaultRootWindow(d), argv[2]); if (!w) usleep(50000); }
  if (!w) { fprintf(stderr, "window not found: %s\n", argv[2]); return 3; }
  int ox, oy; unsigned width, height; origin(d, w, &ox, &oy, &width, &height);
  if (strcmp(argv[1], "find") == 0) { printf("%lu %d %d %u %u\n", w, ox, oy, width, height); return 0; }
  if (strcmp(argv[1], "close") == 0) {
    XEvent e; memset(&e, 0, sizeof e);
    e.xclient.type = ClientMessage; e.xclient.window = w; e.xclient.format = 32;
    e.xclient.message_type = XInternAtom(d, "WM_PROTOCOLS", False);
    e.xclient.data.l[0] = XInternAtom(d, "WM_DELETE_WINDOW", False); e.xclient.data.l[1] = CurrentTime;
    XSendEvent(d, w, False, NoEventMask, &e); XFlush(d);
    printf("XT %.3f close sent\n", mono()); return 0;
  }
  int x = ox + atoi(argv[3]), y = oy + atoi(argv[4]);
  XTestFakeMotionEvent(d, -1, x, y, 0); XSync(d, False); usleep(50000);
  XTestFakeMotionEvent(d, -1, x, y, 0); XSync(d, False); usleep(30000);
  printf("XT %.3f press at %d,%d\n", mono(), x, y);
  XTestFakeButtonEvent(d, 1, True, 0); XSync(d, False);
  if (strcmp(argv[1], "drag") == 0) {
    int dx = atoi(argv[5]), dy = atoi(argv[6]), steps = atoi(argv[7]), ms = atoi(argv[8]);
    for (int i = 1; i <= steps; i++) {
      usleep(ms * 1000);
      XTestFakeMotionEvent(d, -1, x + dx * i / steps, y + dy * i / steps, 0); XSync(d, False);
    }
    usleep(ms * 1000);
  } else {
    usleep(40000);
  }
  XTestFakeButtonEvent(d, 1, False, 0); XSync(d, False);
  printf("XT %.3f release\n", mono());
  origin(d, w, &ox, &oy, &width, &height);
  printf("XT %.3f window now %d %d %u %u\n", mono(), ox, oy, width, height);
  XCloseDisplay(d);
  return 0;
}
