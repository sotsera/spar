// The start-up probe's first page: the theme style and a splash, whose start the window's width picks (start.js), a
// plain fast start unless a test asked for another. Once started it shows its status, and gives the tests the SDK's
// setColorScheme with what the page sees afterwards (window.probe: this test page's own global).
import "./probe.css";
import { setColorScheme } from "@spar/app/sdk";
import { firstPageStarts, runStart } from "./start.js";

// 40 CSS pixels a step, so a scale that rounds the width by a pixel or two still finds its start.
const start = firstPageStarts[Math.round((innerWidth - 800) / 40)] ?? firstPageStarts[0];
const dark = matchMedia("(prefers-color-scheme: dark)");

/** What the theme style gives the page now, read where the app's own page does not paint. */
function look() {
  const root = getComputedStyle(document.documentElement);
  return {
    dark: dark.matches,
    background: root.backgroundColor,
    colorScheme: root.colorScheme,
    themeApplied: document.getElementById("spar-theme")?.sheet != null,
    meta: document.querySelector('meta[name="color-scheme"]')?.content ?? null,
  };
}

window.probe = {
  look,
  /** Sets the scheme, and resolves with how the call ended and what the page sees once it has. */
  async choose(scheme) {
    const outcome = await setColorScheme(scheme).match({
      success: () => "saved",
      errors: { notSaved: () => "notSaved" },
      failure: error => `${error.kind} ${error.code}`,
    });

    // WebKitGTK updates a media query list's matches at its next rendering update, after the style already changed.
    await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)));
    return { outcome, ...look() };
  },
};

await runStart(new URLSearchParams(start));
document.getElementById("status").textContent = "Ready";
