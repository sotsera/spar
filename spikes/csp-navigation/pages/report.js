// Sends one result line to the host (POST /_spar/log) and to the console (read by the Playwright driver).
export async function send(message) {
  const text = JSON.stringify(message);
  console.log("SPAR_PAGE " + text);
  try {
    await fetch("/_spar/log", { method: "POST", body: text });
  } catch (error) {
    console.log("SPAR_LOG_FAILED " + error);
  }
}

export const wait = ms => new Promise(resolve => setTimeout(resolve, ms));

export const frames = n => new Promise(resolve => {
  const step = left => (left === 0 ? resolve() : requestAnimationFrame(() => step(left - 1)));
  step(n);
});

export const external = new URL(location.href).searchParams.get("ext") ?? "http://127.0.0.1:5180";
