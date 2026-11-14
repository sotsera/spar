// Builds a 10,000-row, 6-column table (plain DOM) with per-row data objects kept alive, then reports to the host.
"use strict";
(function () {
  const started = performance.now();
  const statuses = ["online", "offline", "degraded", "maintenance"];
  const data = [];
  for (let i = 0; i < 10000; i++) {
    data.push({ id: i + 1, name: "sql-instance-" + String(i).padStart(5, "0"), status: statuses[i % 4],
      cpu: (i * 37) % 100, memory: 1024 + ((i * 7919) % 65536), seen: new Date(1767225600000 + i * 60000).toISOString() });
  }
  const table = document.createElement("table");
  const head = table.createTHead().insertRow();
  for (const name of ["Id", "Name", "Status", "CPU %", "Memory MB", "Last seen"]) {
    const th = document.createElement("th"); th.textContent = name; head.appendChild(th);
  }
  const body = table.createTBody();
  for (const row of data) {
    const tr = body.insertRow();
    for (const value of [row.id, row.name, row.status, row.cpu, row.memory, row.seen]) {
      tr.insertCell().textContent = String(value);
    }
  }
  document.getElementById("list").appendChild(table);
  window.sparRows = data;
  requestAnimationFrame(() => requestAnimationFrame(() => {
    const report = { rows: body.rows.length, nodes: document.getElementsByTagName("*").length,
      rootChildren: document.getElementById("root").childElementCount, ms: Math.round(performance.now() - started) };
    if (window.webkit && window.webkit.messageHandlers && window.webkit.messageHandlers.spar) {
      window.webkit.messageHandlers.spar.postMessage("SPAR_HEAVY " + JSON.stringify(report));
    }
  }));
})();
