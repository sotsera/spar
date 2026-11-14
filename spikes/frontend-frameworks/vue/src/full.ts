// Built with the full Vue build (VUE_FULL=1): a component whose template is compiled in the page at run time.
import { createApp, ref } from "vue";
import { finish, host, test, waitFor } from "../../shared/harness";

void (async () => {
  await test("runtime template compilation (template option) under the CSP", async () => {
    const el = host();
    const errors: string[] = [];
    const app = createApp({ setup: () => ({ n: ref(2) }), template: `<p>{{ n * 21 }}</p>` });
    app.config.errorHandler = e => { errors.push(String(e)); };
    try { app.mount(el); } catch (e) { errors.push(`mount threw: ${String(e)}`); }
    await waitFor("render", () => el.textContent === "42" || errors.length > 0, 1000, () => el.textContent).catch(() => undefined);
    return { shown: el.textContent, errors };
  });
  finish();
})();
