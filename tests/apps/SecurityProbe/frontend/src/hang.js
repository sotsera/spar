// A page that hangs in its start-up code, before it calls connectSpar: only the host's injected script could answer
// the heartbeat, and it cannot either, since the page's thread never returns. The count keeps the minifier from
// dropping a loop with an empty body.
const until = Date.now() + Number(new URLSearchParams(location.search).get("ms") ?? "600000");
let spins = 0;
while (Date.now() < until) {
  spins++;
}

document.title = `Spun ${spins} times`;
