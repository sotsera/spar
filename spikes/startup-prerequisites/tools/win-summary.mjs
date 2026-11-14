// Summarises the Windows Sandbox case runs: per case, the exit codes, launch to dialog as the probe saw it (from
// Process.Start to a visible #32770 window), and the app's own time from process creation to the dialog's creation
// (TDN_CREATED or the message box's first HCBT_ACTIVATE), as min-median-max over the rounds.
// Usage: node summary.mjs <folder with *.probe.json and *.jsonl>
import { readdirSync, readFileSync } from 'node:fs';
import { join } from 'node:path';

const dir = process.argv[2];
const cases = new Map();
for (const f of readdirSync(dir).filter(n => n.endsWith('.probe.json'))) {
  const probe = JSON.parse(readFileSync(join(dir, f), 'utf8'));
  const label = probe.label.replace(/-r\d+$/, '');
  const log = readFileSync(join(dir, probe.label + '.jsonl'), 'utf8').trim().split('\n').map(l => JSON.parse(l));
  const created = log.find(e => e.evt === 'taskdialog-created' || e.evt === 'messagebox-activated');
  const problem = log.find(e => e.evt === 'problem');
  const failure = ['controller-done', 'environment-done', 'runtime-version'].map(n => log.find(e => e.evt === n)).find(e => e);
  const c = cases.get(label) ?? { exits: [], probe: [], app: [], kind: problem?.kind ?? 'none', hr: failure?.hr };
  c.exits.push(probe.exitCode);
  if (probe.dialogMs) c.probe.push(Number(probe.dialogMs));
  if (created) c.app.push(created.t);
  cases.set(label, c);
}
const stat = xs => {
  if (xs.length === 0) return '-';
  const s = [...xs].sort((a, b) => a - b);
  const med = s[Math.floor((s.length - 1) / 2)];
  return `${Math.round(med)} (${Math.round(s[0])}-${Math.round(s.at(-1))})`;
};
console.log('case | kind | HRESULT of the last step | exit codes | probe: launch to dialog, ms | app: process creation to dialog, ms');
for (const [label, c] of [...cases].sort()) {
  console.log(`${label} | ${c.kind} | ${c.hr ?? '-'} | ${c.exits.join(',')} | ${stat(c.probe)} | ${stat(c.app)}`);
}
