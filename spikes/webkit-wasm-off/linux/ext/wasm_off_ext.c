/* Web process extension for the probe. The UI process passes a string as initialization user data:
 * "off" calls jsc_options_set_boolean("useWasm", FALSE); "report" only reads. What it saw is written to
 * stderr and also exposed to every frame as window.__sparExt (a string), so the page can report it even
 * when the web process's stderr is not visible. */
#include <jsc/jsc.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <unistd.h>
#include <webkit/webkit-web-process-extension.h>

static char info[512];

static void
on_window_object_cleared(WebKitScriptWorld *world, WebKitWebPage *page, WebKitFrame *frame, gpointer user_data)
{
    (void)page;
    (void)user_data;
    JSCContext *context = webkit_frame_get_js_context_for_script_world(frame, world);
    JSCValue *value = jsc_value_new_string(context, info);
    jsc_context_set_value(context, "__sparExt", value);
    g_object_unref(value);
    g_object_unref(context);
}

G_MODULE_EXPORT void
webkit_web_process_extension_initialize_with_user_data(WebKitWebProcessExtension *extension, const GVariant *user_data)
{
    (void)extension;
    const char *mode = user_data ? g_variant_get_string((GVariant *)user_data, NULL) : "(none)";
    const char *env = getenv("JSC_useWasm");
    gboolean before = TRUE;
    gboolean exists = jsc_options_get_boolean("useWasm", &before);
    int used = snprintf(info, sizeof info, "pid=%d mode=%s env JSC_useWasm=%s useWasm before=%d exists=%d",
        (int)getpid(), mode, env ? env : "(unset)", before, exists);
    if (strcmp(mode, "off") == 0 && used > 0 && (size_t)used < sizeof info) {
        gboolean set = jsc_options_set_boolean("useWasm", FALSE);
        gboolean after = TRUE;
        jsc_options_get_boolean("useWasm", &after);
        snprintf(info + used, sizeof info - (size_t)used, "; set useWasm=false returned %d, now %d", set, after);
    }
    fprintf(stderr, "[ext] %s\n", info);
    fflush(stderr);
    g_signal_connect(webkit_script_world_get_default(), "window-object-cleared", G_CALLBACK(on_window_object_cleared), NULL);
}
