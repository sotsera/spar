// Prints the key events of spike JSON-lines logs in a compact form: results, permission events, page media replies,
// DPI and layout marks, errors. Usage: node summarize.mjs <file.jsonl>...
import { readFileSync } from 'node:fs';

const keep = new Set(['permission-requested', 'result', 'step-error', 'ui-error', 'scenario-error', 'wndproc-error', 'media-refused',
  'drive-refused', 'ready-timeout', 'dpi-changed', 'dpi-cache-check', 'nc-provider', 'caption-command', 'exit', 'feature']);
for (const file of process.argv.slice(2)) {
  console.log(`=== ${file.split(/[\/]/).pop()}`);
  for (const line of readFileSync(file, 'utf8').split('\n')) {
    if (!line.trim()) continue;
    const e = JSON.parse(line);
    if (e.evt === 'page') {
      const m = JSON.parse(e.json);
      if (m.type === 'media') console.log(`${e.t.toFixed(0)} page ${JSON.stringify(m)}`);
      continue;
    }
    if (!keep.has(e.evt)) continue;
    const { t, evt, ...rest } = e;
    console.log(`${t.toFixed(0)} ${evt} ${JSON.stringify(rest)}`);
  }
}
