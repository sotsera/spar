// Lists the Wayland compositor's globals (WSLg spike): shows whether any input-injection protocol (virtual keyboard,
// virtual pointer, input-method, EI) or screen-capture protocol is offered to ordinary clients.
// Build: clang -O2 -o wlglobals wlglobals.c -lwayland-client
#include <stdio.h>
#include <wayland-client.h>

static void global(void *data, struct wl_registry *registry, uint32_t name, const char *interface, uint32_t version) {
  (void)data; (void)registry; (void)name;
  printf("%s v%u\n", interface, version);
}

static void global_remove(void *data, struct wl_registry *registry, uint32_t name) { (void)data; (void)registry; (void)name; }

static const struct wl_registry_listener listener = {global, global_remove};

int main(void) {
  struct wl_display *display = wl_display_connect(NULL);
  if (!display) { fprintf(stderr, "wlglobals: cannot connect\n"); return 1; }
  struct wl_registry *registry = wl_display_get_registry(display);
  wl_registry_add_listener(registry, &listener, NULL);
  wl_display_roundtrip(display);
  wl_display_disconnect(display);
  return 0;
}
