/* GtkAlertDialog cancelled, then its parent destroyed in the same loop turn (mode 1), or after its answer (mode 0). */
#include <gtk/gtk.h>
#include <stdlib.h>
static GtkWindow *parent;
static GCancellable *cancellable;
static GMainLoop *loop;
static int mode;

static void chosen(GObject *source, GAsyncResult *result, gpointer data)
{
  GError *error = NULL;
  g_print("answer read; parent %s\n", mode ? "already destroyed" : "still there");
  int button = gtk_alert_dialog_choose_finish(GTK_ALERT_DIALOG(source), result, &error);
  g_print("answer %d (%s)\n", button, error ? error->message : "");
  g_main_loop_quit(loop);
}

static gboolean go(gpointer unused)
{
  g_cancellable_cancel(cancellable);
  if (mode)
    gtk_window_destroy(parent);
  return G_SOURCE_REMOVE;
}

int main(int argc, char **argv)
{
  mode = argc > 1 ? atoi(argv[1]) : 1;
  gtk_init();
  loop = g_main_loop_new(NULL, FALSE);
  parent = GTK_WINDOW(gtk_window_new());
  gtk_window_present(parent);
  GtkAlertDialog *dialog = gtk_alert_dialog_new("The page is not responding");
  cancellable = g_cancellable_new();
  gtk_alert_dialog_choose(dialog, parent, cancellable, chosen, NULL);
  g_timeout_add(300, go, NULL);
  g_main_loop_run(loop);
  if (!mode)
    gtk_window_destroy(parent);
  g_print("done\n");
  return 0;
}
