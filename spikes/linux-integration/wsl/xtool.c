// X11 helper for the integration spike (X11 backend under XWayland only, against windows this spike started).
// Times are CLOCK_MONOTONIC milliseconds, the same clock as the host's marks.
// Build: clang -O2 -Wall -o xtool xtool.c -lX11 -lXtst
//   xtool find TITLE                         window id, root origin, size, _NET_WM_STATE, _NET_FRAME_EXTENTS
//   xtool waitfor TITLE MS                   wait until a viewable window with TITLE exists
//   xtool watch MS                           log ClientMessages sent to the root (WM requests such as
//                                            _NET_WM_MOVERESIZE) and ConfigureNotify of top-level windows
//   xtool drag TITLE X Y DX DY STEPS STEPMS  XTEST: press button 1 at window-relative X,Y, move by DX,DY, release
//   xtool click TITLE X Y BUTTON COUNT       XTEST click(s); COUNT 2 is a double click (80 ms apart)
//   xtool press TITLE X Y BUTTON | release BUTTON
//   xtool wmstate TITLE add|remove|toggle    ask the WM to change _NET_WM_STATE_MAXIMIZED_{VERT,HORZ} (as a pager does)
//   xtool close TITLE                        WM_DELETE_WINDOW, the message a window manager's close button sends
//   xtool iconify TITLE                      XIconifyWindow (WM_CHANGE_STATE)
#include <X11/Xatom.h>
#include <X11/Xlib.h>
#include <X11/Xutil.h>
#include <X11/extensions/XTest.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <time.h>

static double mono(void) {
  struct timespec t;
  clock_gettime(CLOCK_MONOTONIC, &t);
  return t.tv_sec * 1000.0 + t.tv_nsec / 1e6;
}

static void sleep_ms(int ms) {
  struct timespec s = {ms / 1000, (ms % 1000) * 1000000L};
  nanosleep(&s, NULL);
}

static int ignore_errors(Display *d, XErrorEvent *e) { (void)d; (void)e; return 0; }

static Window find_in(Display *d, Window w, const char *title, int depth) {
  char *name = NULL;
  if (XFetchName(d, w, &name) && name) {
    int hit = strstr(name, title) != NULL;
    XFree(name);
    if (hit) {
      XWindowAttributes a;
      if (XGetWindowAttributes(d, w, &a) && a.map_state == IsViewable) return w;
    }
  }
  if (depth == 0) return 0;
  Window root, parent, *children = NULL;
  unsigned int n = 0;
  if (!XQueryTree(d, w, &root, &parent, &children, &n)) return 0;
  Window found = 0;
  for (unsigned int i = 0; i < n && !found; i++) found = find_in(d, children[i], title, depth - 1);
  if (children) XFree(children);
  return found;
}

static Window find(Display *d, const char *title) { return find_in(d, DefaultRootWindow(d), title, 3); }

static void origin(Display *d, Window w, int *x, int *y) {
  Window child;
  XTranslateCoordinates(d, w, DefaultRootWindow(d), 0, 0, x, y, &child);
}

static void print_atoms_property(Display *d, Window w, const char *property) {
  Atom type, p = XInternAtom(d, property, False);
  int format;
  unsigned long count, after;
  unsigned char *data = NULL;
  printf(" %s=[", property);
  if (XGetWindowProperty(d, w, p, 0, 64, False, AnyPropertyType, &type, &format, &count, &after, &data) == Success && data) {
    for (unsigned long i = 0; i < count; i++) {
      if (type == XA_ATOM) {
        char *name = XGetAtomName(d, ((Atom *)data)[i]);
        printf("%s%s", i ? "," : "", name ? name : "?");
        if (name) XFree(name);
      } else if (format == 32) {
        printf("%s%ld", i ? "," : "", ((long *)data)[i]);
      }
    }
    XFree(data);
  }
  printf("]");
}

static void describe(Display *d, Window w, const char *label) {
  XWindowAttributes a;
  XGetWindowAttributes(d, w, &a);
  int x, y;
  origin(d, w, &x, &y);
  printf("XT %.3f %s xid=0x%lx root=%d,%d size=%dx%d map=%d", mono(), label, w, x, y, a.width, a.height, a.map_state);
  print_atoms_property(d, w, "_NET_WM_STATE");
  print_atoms_property(d, w, "_NET_FRAME_EXTENTS");
  print_atoms_property(d, w, "_GTK_FRAME_EXTENTS");
  printf("\n");
  fflush(stdout);
}

static void pointer(Display *d, const char *label) {
  Window r, c;
  int rx, ry, wx, wy;
  unsigned int mask;
  XQueryPointer(d, DefaultRootWindow(d), &r, &c, &rx, &ry, &wx, &wy, &mask);
  printf("XT %.3f pointer-%s root=%d,%d\n", mono(), label, rx, ry);
}

// Moves the XTEST pointer and checks that the server took the position: under rootless XWayland the first motion
// after the pointer was elsewhere is sometimes replaced by the compositor's pointer position.
static void move_to(Display *d, int x, int y) {
  for (int attempt = 0; attempt < 5; attempt++) {
    XTestFakeMotionEvent(d, -1, x, y, CurrentTime);
    XSync(d, False);
    sleep_ms(30);
    Window r, c;
    int rx, ry, wx, wy;
    unsigned int mask;
    XQueryPointer(d, DefaultRootWindow(d), &r, &c, &rx, &ry, &wx, &wy, &mask);
    if (rx == x && ry == y) return;
    printf("XT %.3f pointer-mismatch wanted=%d,%d got=%d,%d (attempt %d)\n", mono(), x, y, rx, ry, attempt + 1);
  }
}

static void client_message(Display *d, Window w, const char *type, long l0, long l1, long l2, long l3, long l4, int to_root) {
  XEvent e = {0};
  e.xclient.type = ClientMessage;
  e.xclient.window = w;
  e.xclient.message_type = XInternAtom(d, type, False);
  e.xclient.format = 32;
  e.xclient.data.l[0] = l0;
  e.xclient.data.l[1] = l1;
  e.xclient.data.l[2] = l2;
  e.xclient.data.l[3] = l3;
  e.xclient.data.l[4] = l4;
  if (to_root)
    XSendEvent(d, DefaultRootWindow(d), False, SubstructureRedirectMask | SubstructureNotifyMask, &e);
  else
    XSendEvent(d, w, False, NoEventMask, &e);
  XFlush(d);
}

static int watch(Display *d, int ms) {
  Window root = DefaultRootWindow(d);
  XSelectInput(d, root, SubstructureNotifyMask);
  XFlush(d);
  printf("XT %.3f watch-start\n", mono());
  fflush(stdout);
  double end = mono() + ms;
  while (mono() < end) {
    while (XPending(d)) {
      XEvent e;
      XNextEvent(d, &e);
      if (e.type == ClientMessage) {
        char *name = XGetAtomName(d, e.xclient.message_type);
        printf("XT %.3f client-message %s window=0x%lx data=%ld,%ld,%ld,%ld,%ld\n", mono(), name ? name : "?", e.xclient.window,
               e.xclient.data.l[0], e.xclient.data.l[1], e.xclient.data.l[2], e.xclient.data.l[3], e.xclient.data.l[4]);
        if (name) XFree(name);
      } else if (e.type == ConfigureNotify) {
        printf("XT %.3f configure window=0x%lx at=%d,%d size=%dx%d\n", mono(), e.xconfigure.window, e.xconfigure.x, e.xconfigure.y,
               e.xconfigure.width, e.xconfigure.height);
      } else if (e.type == MapNotify || e.type == UnmapNotify) {
        printf("XT %.3f %s window=0x%lx\n", mono(), e.type == MapNotify ? "map" : "unmap", e.type == MapNotify ? e.xmap.window : e.xunmap.window);
      }
      fflush(stdout);
    }
    sleep_ms(2);
  }
  return 0;
}

int main(int argc, char **argv) {
  if (argc < 2) { fprintf(stderr, "usage: see xtool.c\n"); return 2; }
  Display *d = XOpenDisplay(NULL);
  if (!d) { printf("XT no-display\n"); return 1; }
  XSetErrorHandler(ignore_errors);
  const char *cmd = argv[1];
  if (!strcmp(cmd, "watch") && argc >= 3) return watch(d, atoi(argv[2]));
  if (!strcmp(cmd, "release") && argc >= 3) {
    XTestFakeButtonEvent(d, atoi(argv[2]), False, CurrentTime);
    XFlush(d);
    printf("XT %.3f release %s\n", mono(), argv[2]);
    return 0;
  }
  if (argc < 3) return 2;
  Window w = 0;
  if (!strcmp(cmd, "waitfor")) {
    double end = mono() + (argc >= 4 ? atoi(argv[3]) : 5000);
    while (!(w = find(d, argv[2])) && mono() < end) sleep_ms(10);
    if (!w) { printf("XT %.3f waitfor-timeout %s\n", mono(), argv[2]); return 1; }
    describe(d, w, "found");
    return 0;
  }
  w = find(d, argv[2]);
  if (!w) { printf("XT %.3f window-not-found %s\n", mono(), argv[2]); return 1; }
  int ox, oy;
  origin(d, w, &ox, &oy);
  if (!strcmp(cmd, "find")) {
    describe(d, w, "find");
  } else if (!strcmp(cmd, "drag") && argc >= 9) {
    int x = atoi(argv[3]), y = atoi(argv[4]), dx = atoi(argv[5]), dy = atoi(argv[6]), steps = atoi(argv[7]), step_ms = atoi(argv[8]);
    describe(d, w, "before");
    move_to(d, ox + x, oy + y);
    sleep_ms(10);
    printf("XT %.3f press at root %d,%d\n", mono(), ox + x, oy + y);
    XTestFakeButtonEvent(d, 1, True, CurrentTime);
    XFlush(d);
    for (int i = 1; i <= steps; i++) {
      sleep_ms(step_ms);
      XTestFakeMotionEvent(d, -1, ox + x + dx * i / steps, oy + y + dy * i / steps, CurrentTime);
      XFlush(d);
    }
    sleep_ms(step_ms);
    pointer(d, "before-release");
    XTestFakeButtonEvent(d, 1, False, CurrentTime);
    XFlush(d);
    printf("XT %.3f release at root %d,%d\n", mono(), ox + x + dx, oy + y + dy);
    sleep_ms(300);
    Window again = find(d, argv[2]);
    describe(d, again ? again : w, "after");
  } else if (!strcmp(cmd, "click") && argc >= 7) {
    int x = atoi(argv[3]), y = atoi(argv[4]), button = atoi(argv[5]), count = atoi(argv[6]);
    move_to(d, ox + x, oy + y);
    sleep_ms(10);
    for (int i = 0; i < count; i++) {
      if (i) sleep_ms(80);
      printf("XT %.3f click %d button=%d at root %d,%d\n", mono(), i + 1, button, ox + x, oy + y);
      XTestFakeButtonEvent(d, button, True, CurrentTime);
      XFlush(d);
      sleep_ms(30);
      XTestFakeButtonEvent(d, button, False, CurrentTime);
      XFlush(d);
    }
  } else if (!strcmp(cmd, "press") && argc >= 6) {
    move_to(d, ox + atoi(argv[3]), oy + atoi(argv[4]));
    printf("XT %.3f press button=%s\n", mono(), argv[5]);
    XTestFakeButtonEvent(d, atoi(argv[5]), True, CurrentTime);
  } else if (!strcmp(cmd, "wmstate") && argc >= 4) {
    long action = !strcmp(argv[3], "remove") ? 0 : !strcmp(argv[3], "add") ? 1 : 2;
    client_message(d, w, "_NET_WM_STATE", action, XInternAtom(d, "_NET_WM_STATE_MAXIMIZED_VERT", False),
                   XInternAtom(d, "_NET_WM_STATE_MAXIMIZED_HORZ", False), 2, 0, 1);
    printf("XT %.3f wmstate %s sent\n", mono(), argv[3]);
  } else if (!strcmp(cmd, "close")) {
    client_message(d, w, "WM_PROTOCOLS", XInternAtom(d, "WM_DELETE_WINDOW", False), CurrentTime, 0, 0, 0, 0);
    printf("XT %.3f close (WM_DELETE_WINDOW) sent\n", mono());
  } else if (!strcmp(cmd, "iconify")) {
    XIconifyWindow(d, w, DefaultScreen(d));
    printf("XT %.3f iconify sent\n", mono());
  } else {
    fprintf(stderr, "unknown command\n");
    return 2;
  }
  XSync(d, False);
  XCloseDisplay(d);
  return 0;
}
