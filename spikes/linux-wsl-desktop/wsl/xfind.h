// Shared helper for the X11 tools: finds a top-level window whose WM_NAME contains a substring. XWayland under WSLg is
// rootless and has no window-manager frames in the X tree, so toplevels are children of the root (searched two deep).
#include <X11/Xlib.h>
#include <X11/Xutil.h>
#include <string.h>

static Window xfind_in(Display *d, Window w, const char *title, int depth) {
  char *name = NULL;
  if (XFetchName(d, w, &name) && name) {
    int hit = strstr(name, title) != NULL;
    XFree(name);
    if (hit) return w;
  }
  if (depth == 0) return 0;
  Window root, parent, *children = NULL;
  unsigned int n = 0;
  if (!XQueryTree(d, w, &root, &parent, &children, &n)) return 0;
  Window found = 0;
  for (unsigned int i = 0; i < n && !found; i++) found = xfind_in(d, children[i], title, depth - 1);
  if (children) XFree(children);
  return found;
}

static Window xfind(Display *d, const char *title) { return xfind_in(d, DefaultRootWindow(d), title, 2); }

static int xignore(Display *d, XErrorEvent *e) { (void)d; (void)e; return 0; }
