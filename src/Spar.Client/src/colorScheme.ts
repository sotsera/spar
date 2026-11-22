// The app's own theme choice (plan section 3d): one of the host's own commands, under the contract name `spar`, which
// no app contract can take. The host saves the choice in the app's data folder, so the next start shows the window in
// it from its first frame, and the window takes it at once: its colour, title bar, the WebView's background and the
// page's `prefers-color-scheme`, together, as for a change of the system's theme.
import { unknownErrorCase, type Decoder } from "./connection.js";
import type { Call, CallOptions } from "./results.js";
import { invoke } from "./sdk.js";

/** The app's colour scheme: always light, always dark, or the system's. */
export type ColorScheme = "light" | "dark" | "system";

/** What `setColorScheme` can end with besides success. */
export interface ColorSchemeError {
  /**
   * The window took the choice, but the app's data folder could not be written, so the next start shows the earlier
   * choice. The host's log has the reason.
   */
  readonly notSaved: undefined;
}

const schemes: readonly string[] = ["light", "dark", "system"];

const decoder: Decoder<void> = {
  value: () => undefined,
  error: code => (code === "notSaved" ? undefined : unknownErrorCase),
};

/**
 * Sets the app's colour scheme: the host saves it for the next start and applies it now. In a browser on loopback
 * (development and browser tests) the choice is saved for the window, and the browser keeps its own scheme.
 *
 * ```ts
 * await setColorScheme("dark").match({
 *   success: () => undefined,
 *   errors: { notSaved: () => showNotice("Dark until the app closes: the setting could not be saved.") },
 * });
 * ```
 */
export function setColorScheme(scheme: ColorScheme, options?: CallOptions): Call<void, ColorSchemeError> {
  const encode = (): string => {
    if (!schemes.includes(scheme)) {
      throw new TypeError(`setColorScheme takes "light", "dark" or "system", not ${JSON.stringify(scheme)}.`);
    }

    return JSON.stringify(scheme);
  };
  return invoke<void, ColorSchemeError>("spar", "setColorScheme", encode, decoder, options);
}
