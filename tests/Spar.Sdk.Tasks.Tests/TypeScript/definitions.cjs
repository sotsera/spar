// Go-to-definition through tsserver, the process editors talk to: it opens consumer.ts and asks for the definition
// of each probe (a snippet of consumer.ts and the name inside it). Prints one JSON object: probe -> "file:line".
// Arguments: the TypeScript package folder, the scratch app folder, the probes as JSON [[snippet, name], ...].
const { spawn } = require("node:child_process");
const fs = require("node:fs");
const path = require("node:path");

const server = path.join(process.argv[2], "lib", "tsserver.js");
const directory = process.argv[3];
const probes = JSON.parse(process.argv[4]);
const consumer = path.join(directory, "consumer.ts");
const lines = fs.readFileSync(consumer, "utf8").split("\n");

const child = spawn(process.execPath, [server, "--disableAutomaticTypingAcquisition"], { stdio: ["pipe", "pipe", "inherit"] });
const waiting = new Map();
let buffered = Buffer.alloc(0);
let seq = 0;

// tsserver answers with "Content-Length: N", a blank line, then N bytes of JSON.
child.stdout.on("data", chunk => {
  buffered = Buffer.concat([buffered, chunk]);
  for (;;) {
    const header = buffered.indexOf("\r\n\r\n");
    if (header < 0) {
      return;
    }

    const length = Number(/Content-Length: (\d+)/.exec(buffered.subarray(0, header).toString())[1]);
    if (buffered.length < header + 4 + length) {
      return;
    }

    const message = JSON.parse(buffered.subarray(header + 4, header + 4 + length).toString());
    buffered = buffered.subarray(header + 4 + length);
    if (message.type === "response" && waiting.has(message.request_seq)) {
      waiting.get(message.request_seq)(message);
      waiting.delete(message.request_seq);
    }
  }
});

function request(command, args) {
  seq += 1;
  const id = seq;
  child.stdin.write(`${JSON.stringify({ seq: id, type: "request", command, arguments: args })}\n`);
  return new Promise(resolve => waiting.set(id, resolve));
}

function locate(snippet, name) {
  for (let index = 0; index < lines.length; index++) {
    const at = lines[index].indexOf(snippet);
    if (at >= 0) {
      return { line: index + 1, offset: at + snippet.indexOf(name) + 1 };
    }
  }

  return undefined;
}

async function main() {
  child.stdin.write(`${JSON.stringify({ seq: 0, type: "request", command: "open", arguments: { file: consumer } })}\n`);
  const results = {};
  for (const [snippet, name] of probes) {
    const at = locate(snippet, name);
    if (at === undefined) {
      results[snippet] = "snippet not found";
      continue;
    }

    const response = await request("definition", { file: consumer, ...at });
    results[snippet] = (response.body ?? []).map(d => `${path.relative(directory, d.file).replaceAll("\\", "/")}:${d.start.line}`).join(" ");
  }

  console.log(JSON.stringify(results));
  child.kill();
}

main().catch(error => {
  console.error(error);
  child.kill();
  process.exit(1);
});
