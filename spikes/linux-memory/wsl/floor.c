/* Minimal GTK window in C (no .NET, no WebKit): the floor for the memory comparison.
   Usage: floor-gtk3|floor-gtk4 SECONDS TITLE   Prints "SPAR_READY" 200 ms after the window is shown, exits after SECONDS. */
#include <gtk/gtk.h>
#include <stdio.h>
#include <stdlib.h>

static GMainLoop *loop;
static gboolean ready(gpointer data) { (void)data; printf("SPAR_READY floor\n"); fflush(stdout); return G_SOURCE_REMOVE; }
static gboolean done(gpointer data) { (void)data; g_main_loop_quit(loop); return G_SOURCE_REMOVE; }

int main(int argc, char **argv) {
  int seconds = argc > 1 ? atoi(argv[1]) : 5;
  const char *title = argc > 2 ? argv[2] : "Spar mem floor";
#ifdef SPAR_GTK4
  if (!gtk_init_check()) return 2;
  GtkWidget *window = gtk_window_new();
  gtk_window_set_child(GTK_WINDOW(window), gtk_label_new("Spar memory floor: C + GTK only"));
  gtk_window_set_title(GTK_WINDOW(window), title);
  gtk_window_set_default_size(GTK_WINDOW(window), 480, 360);
  gtk_window_present(GTK_WINDOW(window));
#else
  if (!gtk_init_check(NULL, NULL)) return 2;
  GtkWidget *window = gtk_window_new(GTK_WINDOW_TOPLEVEL);
  gtk_container_add(GTK_CONTAINER(window), gtk_label_new("Spar memory floor: C + GTK only"));
  gtk_window_set_title(GTK_WINDOW(window), title);
  gtk_window_set_default_size(GTK_WINDOW(window), 480, 360);
  gtk_widget_show_all(window);
#endif
  loop = g_main_loop_new(NULL, FALSE);
  g_timeout_add(200, ready, NULL);
  g_timeout_add(seconds * 1000, done, NULL);
  g_main_loop_run(loop);
  return 0;
}
