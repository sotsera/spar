// The page's side of a large result: the SDK splits a host message at the TAB and parses the payload with JSON.parse.
const sizes = [0.0625, 1, 4, 16, 64];
console.log("== The page parsing one result message (Node " + process.version + ", V8; median of 7)");
console.log("payload MiB | split + JSON.parse ms | heap MiB after");
for (const mib of sizes) {
  const text = "a".repeat(Math.floor(mib * 1024 * 1024) - 12);
  const message = '{"type":"result","id":7,"ok":true}\t{"text":"' + text + '"}';
  const times = [];
  for (let run = 0; run < 7; run++) {
    const start = performance.now();
    const tab = message.indexOf("\t");
    JSON.parse(message.slice(0, tab));
    const value = JSON.parse(message.slice(tab + 1));
    if (value.text.length !== text.length) throw new Error("bad parse");
    times.push(performance.now() - start);
  }
  times.sort((a, b) => a - b);
  console.log(`${String(mib).padStart(11)} | ${times[3].toFixed(2).padStart(21)} | ${(process.memoryUsage().heapUsed / 1048576).toFixed(0).padStart(14)}`);
}
