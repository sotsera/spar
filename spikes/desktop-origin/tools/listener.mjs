// Loopback leak listener for the desktop-origin spike.
//
//   node tools/listener.mjs <log file> [--lna-port <port>]
//
// Listens on 127.0.0.1 and [::1], ports 443 and 80. It only accepts connections and logs them, with the TLS SNI
// (port 443, read from the ClientHello; no TLS is spoken) or the HTTP request line and Host (port 80), then closes
// the connection. With --lna-port it also runs a plain HTTP server on 127.0.0.1:<port> that answers with permissive
// CORS headers and logs every request, preflights included (local network access checks).
// A port already in use is logged and skipped; nothing else is touched.
//
// Attribution: other apps on the machine may use the same origin, so each connection is matched to the process that
// opened it (netstat, once per burst, while the sockets are held open), and that process to its WebView2 user-data
// folder (from its command line). Both lookups are read-only.
import fs from "node:fs";
import net from "node:net";
import http from "node:http";
import { execFile } from "node:child_process";

const [logFile, ...rest] = process.argv.slice(2);
if (!logFile) {
  console.error("usage: node tools/listener.mjs <log file> [--lna-port <port>]");
  process.exit(2);
}

const lnaIndex = rest.indexOf("--lna-port");
const lnaPort = lnaIndex >= 0 ? Number(rest[lnaIndex + 1]) : 0;
const log = (entry) => {
  const line = JSON.stringify({ epoch: Date.now(), ...entry });
  fs.appendFileSync(logFile, line + "\n");
  console.log(line);
};

const run = (file, args) => new Promise((resolve) => execFile(file, args, { windowsHide: true }, (_, out) => resolve(out ?? "")));
const profiles = new Map();
let pending = [];
let batchTimer;

function profileOf(command) {
  const match = /--user-data-dir="?([^"]+?)"?(?:\s--|$)/.exec(command);
  if (!match) return command.slice(0, 100);
  return match[1].split(/[\\/]/).filter(Boolean).slice(-2).join("/");
}

async function resolveBatch() {
  const batch = pending;
  pending = [];
  batchTimer = undefined;
  const text = (await run("netstat", ["-ano", "-p", "TCP"])) + "\n" + (await run("netstat", ["-ano", "-p", "TCPv6"]));
  const owners = new Map();
  for (const line of text.split(/\r?\n/)) {
    const parts = line.trim().split(/\s+/);
    if (parts.length < 5 || parts[0] !== "TCP") continue;
    const local = parts[1].slice(parts[1].lastIndexOf(":") + 1);
    const remote = parts[2].slice(parts[2].lastIndexOf(":") + 1);
    if (remote === "443" || remote === "80") owners.set(Number(local), Number(parts[4]));
  }

  const unknown = [...new Set(batch.map((item) => owners.get(item.peerPort)).filter((pid) => pid && !profiles.has(pid)))];
  if (unknown.length) {
    const filter = unknown.map((pid) => `ProcessId=${pid}`).join(" OR ");
    const out = await run("powershell", ["-NoProfile", "-Command",
      `Get-CimInstance Win32_Process -Filter '${filter}' | ForEach-Object { [string]$_.ProcessId + '|' + $_.Name + '|' + $_.CommandLine }`]);
    for (const line of out.split(/\r?\n/)) {
      const [pid, name, ...command] = line.split("|");
      if (pid) profiles.set(Number(pid), `${name} ${profileOf(command.join("|"))}`);
    }
  }

  for (const item of batch) {
    const pid = owners.get(item.peerPort);
    item.done({ pid, owner: pid ? profiles.get(pid) : "(closed before lookup)" });
  }
}

function attribute(peerPort, done) {
  pending.push({ peerPort, done });
  batchTimer ??= setTimeout(resolveBatch, 30);
}

// TLS record (22 = handshake), ClientHello (1): skip to the extensions and find server_name (0).
function readSni(buffer) {
  try {
    if (buffer[0] !== 22 || buffer[5] !== 1) return undefined;
    let offset = 9 + 2 + 32; // record header 5, handshake header 4, version 2, random 32
    offset += 1 + buffer[offset]; // session id
    offset += 2 + buffer.readUInt16BE(offset); // cipher suites
    offset += 1 + buffer[offset]; // compression methods
    const end = offset + 2 + buffer.readUInt16BE(offset);
    offset += 2;
    while (offset + 4 <= end) {
      const type = buffer.readUInt16BE(offset);
      const length = buffer.readUInt16BE(offset + 2);
      if (type === 0) {
        const nameLength = buffer.readUInt16BE(offset + 7);
        return buffer.toString("ascii", offset + 9, offset + 9 + nameLength);
      }
      offset += 4 + length;
    }
  } catch {
    return "(unparsed)";
  }
  return "(no sni)";
}

function listen(address, port) {
  const listener = `${address}:${port}`;
  const server = net.createServer((socket) => {
    const peer = `${socket.remoteAddress}:${socket.remotePort}`;
    const seen = Date.now();
    let handled = false;
    const handle = (data) => {
      if (handled) return;
      handled = true;
      attribute(socket.remotePort, (owner) => {
        if (!data) {
          log({ kind: "connection", seen, listener, peer, ...owner, data: "(none in 1 s)" });
        } else if (port === 443) {
          log({ kind: "connection", seen, listener, peer, ...owner, tls: data[0] === 22, sni: readSni(data) });
        } else {
          const text = data.toString("latin1");
          const host = /^host:\s*(.*)$/im.exec(text)?.[1]?.trim();
          log({ kind: "connection", seen, listener, peer, ...owner, request: text.split("\r\n")[0], host });
        }
        socket.destroy();
      });
    };
    const idle = setTimeout(() => handle(undefined), 1000);
    socket.once("data", (data) => {
      clearTimeout(idle);
      handle(data);
    });
    socket.on("error", () => {});
  });
  server.on("error", (error) => log({ kind: "listen-failed", listener, error: error.code }));
  server.listen({ host: address, port, exclusive: true }, () => log({ kind: "listening", listener }));
}

for (const address of ["127.0.0.1", "::1"]) {
  for (const port of [443, 80]) listen(address, port);
}

if (lnaPort) {
  const server = http.createServer((request, response) => {
    const headers = Object.fromEntries(Object.entries(request.headers).filter(([name]) =>
      /origin|access-control|sec-fetch|host|private-network|local-network|referer/i.test(name)));
    log({ kind: "lna-request", method: request.method, url: request.url, headers });
    response.writeHead(request.url.startsWith("/lna.png") ? 404 : 200, {
      "Access-Control-Allow-Origin": request.headers.origin ?? "*",
      "Access-Control-Allow-Private-Network": "true",
      "Access-Control-Allow-Local-Network": "true",
      "Content-Type": "text/plain",
    });
    response.end("lna ok");
  });
  server.on("error", (error) => log({ kind: "listen-failed", listener: `127.0.0.1:${lnaPort}`, error: error.code }));
  server.listen(lnaPort, "127.0.0.1", () => log({ kind: "listening", listener: `127.0.0.1:${lnaPort} (lna http)` }));
}

process.on("SIGTERM", () => process.exit(0));
process.on("SIGINT", () => process.exit(0));
