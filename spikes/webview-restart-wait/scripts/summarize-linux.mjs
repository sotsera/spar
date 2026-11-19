// Summarizes the Linux probe's raw logs (results/linux/<cell>.log) into one Markdown table row per cell: the first
// view and the restarts (cycles 1..n) to load finished, multi-second restarts, restarts that started a new network
// process, the previous web process's close->exit, and the network process's fate after the last close.
//   node scripts/summarize-linux.mjs [cell-name-regex]
import { readdirSync, readFileSync } from 'node:fs';
import { join } from 'node:path';

const folder = join(import.meta.dirname, '..', 'results', 'linux');
const filter = new RegExp(process.argv[2] ?? '.');
const median = (values) => {
  if (values.length === 0) return NaN;
  const sorted = [...values].sort((a, b) => a - b);
  const middle = Math.floor(sorted.length / 2);
  return sorted.length % 2 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2;
};
const fmt = (value) => (Number.isNaN(value) ? '–' : Math.round(value).toLocaleString('en-US'));
const range = (values) => (values.length ? `${fmt(median(values))} (${fmt(Math.min(...values))}–${fmt(Math.max(...values))})` : '–');

console.log('| Cell | Restarts | First view | Next view to load finished p50 (min–max) | ≥ 2 s | New network process | Previous web process close → exit p50 (min–max) | Network process after the last close |');
console.log('|---|---|---|---|---|---|---|---|');
for (const file of readdirSync(folder).filter((f) => f.endsWith('.log')).sort()) {
  const cell = file.slice(0, -4);
  if (!filter.test(cell)) continue;
  const text = readFileSync(join(folder, file), 'utf8');
  const cycles = [...text.matchAll(/cycle (\d+): committed (\d+) ms, finished (\d+) ms; new \[([^\]]*)\](.*)/g)].map((m) => ({
    index: +m[1], finished: +m[3], fresh: m[4], rest: m[5],
  }));
  if (cycles.length === 0) {
    console.log(`| ${cell} | ${/FAILED.*/.exec(text)?.[0] ?? 'no cycles'} |`);
    continue;
  }

  const restarts = cycles.filter((c) => c.index > 0);
  const finished = restarts.map((c) => c.finished);
  const webExits = restarts.flatMap((c) => [...c.rest.matchAll(/web#\d+ exit \+(\d+)/g)].map((m) => +m[1]));
  const after = /after 8 s: (.*)/.exec(text)?.[1] ?? '';
  const networks = [...after.matchAll(/network#\d+ seen \d+ (alive|gone \d+ \(\+(-?\d+) after last close\))/g)];
  const last = networks.at(-1);
  const fate = !last ? '–' : last[1] === 'alive' ? 'alive 8 s later' : `exit +${last[2]} ms`;
  console.log(`| ${cell} | ${restarts.length} | ${fmt(cycles[0].finished)} | ${range(finished)} | ${finished.filter((v) => v >= 2000).length} | ` +
    `${restarts.filter((c) => /network#/.test(c.fresh)).length} | ${range(webExits)} | ${fate} (${networks.length} network process${networks.length === 1 ? '' : 'es'} in the run) |`);
}
