// Gives the pages the same message entry point they use on WebKitGTK.
"use strict";
const { contextBridge, ipcRenderer } = require("electron");
contextBridge.exposeInMainWorld("webkit", {
  messageHandlers: { spar: { postMessage: (text) => ipcRenderer.send("spar", String(text)) } },
});
