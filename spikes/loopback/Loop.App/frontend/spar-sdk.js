// Authored stand-in for the generated @spar/sdk: one transport per host, chosen at runtime.
const hash = new URLSearchParams(location.hash.slice(1));
const token = hash.get("token") ?? sessionStorage.getItem("spar-token");
if (token) {
  sessionStorage.setItem("spar-token", token);
  history.replaceState(null, "", location.pathname);
}

export class ContractMismatchError extends Error {}

function headers(fingerprint) {
  return { "Authorization": `Bearer ${token}`, "X-Spar-Contracts": fingerprint, "Content-Type": "application/json" };
}

export async function invoke(fingerprint, contract, command, request) {
  if (globalThis.chrome?.webview) {
    throw new Error("The WebView2 transport is not part of this spike.");
  }

  const response = await fetch(`/_spar/call/${contract}/${command}`, {
    method: "POST",
    headers: headers(fingerprint),
    body: request === undefined ? "" : JSON.stringify(request),
  });

  if (response.status === 409) {
    throw new ContractMismatchError("The app was rebuilt with different contracts. Reload the page.");
  }

  if (!response.ok) {
    const body = await response.json().catch(() => ({}));
    throw new Error(`${response.status} ${body.error ?? ""} ${body.detail ?? ""}`.trim());
  }

  return response.json();
}

/** Tells the host the UI is wired and can accept input; the host reports it separately from page load. */
export async function ready() {
  if (!globalThis.chrome?.webview) {
    await fetch("/_spar/ready", { method: "POST", headers: { "Authorization": `Bearer ${token}` } });
  }
}
