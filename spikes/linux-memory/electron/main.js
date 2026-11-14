// Minimal Electron reference for the memory comparison. It serves the spike's web/ folder on spar://app/ (a
// privileged standard scheme), shows a 480x360 window, maps the pages' webkit.messageHandlers.spar.postMessage calls
// to IPC through a preload script, and reads the same control FIFO commands as the WebKitGTK host (load PAGE, quit).
// Usage: electron --ozone-platform=<wayland|x11> electron/ LABEL FIFO PROFILE_DIR WEB_DIR
"use strict";
const { app, BrowserWindow, protocol, ipcMain, net } = require("electron");
const fs = require("node:fs");
const nodeNet = require("node:net");
const path = require("node:path");
const { pathToFileURL } = require("node:url");

const [label, fifo, profile, webRoot] = process.argv.slice(-4);
const started = performance.now();
function mark(text) {
  const mono = Number(process.hrtime.bigint()) / 1e6;
  process.stdout.write(`M ${mono.toFixed(3)} ${(performance.now() - started).toFixed(1).padStart(9)} ${text}\n`);
}

app.setPath("userData", profile);
protocol.registerSchemesAsPrivileged([
  { scheme: "spar", privileges: { standard: true, secure: true, supportFetchAPI: true, corsEnabled: true } },
]);

let window;
let current = "";
function load(page) {
  mark("load " + page);
  current = page;
  window.loadURL(`spar://app/${page}.html`);
}

function readCommands() {
  // The FIFO is opened read-write and non-blocking and read through a socket, so no worker thread blocks on it
  // (a blocking read kept the process alive after quit), and it never reaches end-of-file between writers.
  const fd = fs.openSync(fifo, fs.constants.O_RDWR | fs.constants.O_NONBLOCK);
  const socket = new nodeNet.Socket({ fd, readable: true, writable: false });
  socket.setEncoding('utf8');
  let buffer = '';
  socket.on('data', (chunk) => {
    buffer += chunk;
    let index;
    while ((index = buffer.indexOf("\n")) >= 0) {
      const command = buffer.slice(0, index).trim();
      buffer = buffer.slice(index + 1);
      mark('command ' + command);
      if (command.startsWith('load ')) load(command.slice(5));
      else if (command === 'quit') { socket.destroy(); app.quit(); }
    }
  });
}

app.whenReady().then(() => {
  const root = path.resolve(webRoot) + path.sep;
  protocol.handle("spar", (request) => {
    const file = path.resolve(root, decodeURIComponent(new URL(request.url).pathname).replace(/^\/+/, ""));
    if (!file.startsWith(root) || !fs.existsSync(file)) return new Response("not found", { status: 404 });
    return net.fetch(pathToFileURL(file).toString());
  });
  ipcMain.on("spar", (_event, text) => mark("page " + text));
  window = new BrowserWindow({
    width: 480, height: 360, title: "Spar mem " + label, backgroundColor: "#1e1e1e",
    webPreferences: { preload: path.join(__dirname, "preload.js") },
  });
  window.webContents.on("did-finish-load", () => mark(current === "empty" ? "load-finished SPAR_READY empty" : "load-finished"));
  window.webContents.on("render-process-gone", (_e, details) => mark("render-process-gone " + details.reason));
  mark(`electron ${process.versions.electron} chrome ${process.versions.chrome} platform ${app.commandLine.getSwitchValue("ozone-platform")}`);
  load(process.env.SPAR_PAGE || "small");
  readCommands();
});

app.on("window-all-closed", () => app.quit());
app.on("quit", () => mark("exit"));
