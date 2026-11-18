// Summarizes the Windows probe's raw logs (results/win/<cell>.log) into one Markdown table row per cell:
// restarts (cycles 1..n): time to the controller and to the first NavigationCompleted (p50, min, max), multi-second
// (>= 2 s) restarts, restarts in a new browser, restarts that began after the previous browser had exited, and the
// previous browser's close->exit (only for browsers that ended with their cycle's close) and the BrowserProcessExited lag.
//   node scripts/summarize-win.mjs [cell-name-regex]
import { readdirSync, readFileSync } from 'node:fs';
import { join } from 'node:path';

const folder = join(import.meta.dirname, '..', 'results', 'win');
const filter = new RegExp(process.argv[2] ?? '.');
const median = (values) => {
  if (values.length === 0) return NaN;
  const sorted = [...values].sort((a, b) => a - b);
  const middle = Math.floor(sorted.length / 2);
  return sorted.length % 2 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2;
};
const fmt = (value) => (Number.isNaN(value) ? '–' : Math.round(value).toLocaleString('en-US'));

const rows = [];
for (const file of readdirSync(folder).filter((f) => f.endsWith('.log')).sort()) {
  const cell = file.slice(0, -4);
  if (!filter.test(cell)) continue;
  const text = readFileSync(join(folder, file), 'utf8');
  const cycles = [...text.matchAll(/cycle (\d+): controller (\d+) ms, navigation (\d+) ms, browser (\d+) new (True|False)(.*)/g)].map((m) => ({
    index: +m[1], controller: +m[2], navigation: +m[3], pid: m[4], fresh: m[5] === 'True',
    exitedBefore: /exited before create True/.test(m[6]),
    waited: +(/\(waited (\d+)\)/.exec(m[6])?.[1] ?? 0),
    previousExit: /previous exit \+(\d+) ms \/ event \+(\d+) ms/.exec(m[6]),
  }));
  if (cycles.length === 0) {
    rows.push(`| ${cell} | ${/FAILED.*/.exec(text)?.[0] ?? 'no cycles'} |`);
    continue;
  }

  const restarts = cycles.filter((c) => c.index > 0);
  const controller = restarts.map((c) => c.controller);
  const toNavigation = restarts.map((c) => c.controller + c.navigation);
  const waited = restarts.map((c) => c.waited);
  // close->exit of a browser that ended with its cycle: the next cycle (or the end of the run) found it gone.
  const exits = [...text.matchAll(/cycle (\d+) browser (\d+): close->exit \+(-?\d+) ms, BrowserProcessExited \+(-?\d+) ms/g)]
    .map((m) => ({ index: +m[1], pid: m[2], exit: +m[3], event: +m[4] }))
    .filter((e) => !cycles.some((c) => c.index === e.index + 1 && c.pid === e.pid) && e.exit >= 0);
  const lag = exits.map((e) => e.event - e.exit);
  rows.push(`| ${cell} | ${restarts.length} | ${fmt(cycles[0].controller)} | ${fmt(median(waited))} | ${fmt(median(controller))} (${fmt(Math.min(...controller))}–${fmt(Math.max(...controller))}) | ` +
    `${fmt(median(toNavigation))} | ${controller.filter((v) => v >= 2000).length} | ${restarts.filter((c) => c.fresh).length} | ` +
    `${restarts.filter((c) => c.exitedBefore).length} | ${fmt(median(exits.map((e) => e.exit)))} (${fmt(Math.min(...exits.map((e) => e.exit)))}–${fmt(Math.max(...exits.map((e) => e.exit)))}) | ${fmt(median(lag))} |`);
}

console.log('| Cell | Restarts | First controller | Waited for exit p50 | Next controller p50 (min–max) | To first navigation p50 | ≥ 2 s | New browser | Old browser gone before create | Close → browser exit p50 (min–max) | `BrowserProcessExited` after exit p50 |');
console.log('|---|---|---|---|---|---|---|---|---|---|---|');
for (const row of rows) console.log(row);
