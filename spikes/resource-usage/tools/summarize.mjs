// Usage: node tools/summarize.mjs <results folder> — reads results/win/*.log and results/linux/*.log and writes
// results/summary-win.txt and results/summary-linux.txt (medians and ranges across runs).
import fs from 'node:fs';
import path from 'node:path';

const root = path.resolve(process.argv[2] || 'results');
const median = (xs) => { const s = [...xs].sort((a, b) => a - b); return s.length ? (s.length % 2 ? s[(s.length - 1) / 2] : (s[s.length / 2 - 1] + s[s.length / 2]) / 2) : NaN; };
const range = (xs) => xs.length ? `${fmt(median(xs))} [${fmt(Math.min(...xs))}–${fmt(Math.max(...xs))}]` : '-';
const fmt = (x) => (Math.abs(x) >= 100 ? x.toFixed(0) : Math.abs(x) >= 10 ? x.toFixed(1) : x.toFixed(2));
const kv = (text) => Object.fromEntries([...text.matchAll(/(\w[\w-]*)=([^\s()]+)/g)].map((m) => [m[1], m[2]]));
const num = (v) => parseFloat(String(v).replace(/[a-zA-Z]+$/, ''));
const read = (dir, re) => fs.existsSync(dir) ? fs.readdirSync(dir).filter((f) => re.test(f)).sort().map((f) => ({ file: f, lines: fs.readFileSync(path.join(dir, f), 'utf8').split(/\r?\n/) })) : [];
const out = [];
const say = (s = '') => out.push(s);

function collect(logs, key, filter = () => true) {
  const rows = [];
  // Parenthesised groups (first=(...), sampleCost=(...), syncPartOnMain=(...)) are not top-level values.
  for (const { file, lines } of logs) for (const line of lines) if (line.startsWith(key + ' ') && filter(line)) rows.push({ file, line, v: kv(line.replace(/\w+=\([^)]*\)/g, '')) });
  return rows;
}

function costTable(logs) {
  const groups = new Map();
  for (const r of collect(logs, 'COST')) {
    const label = r.v.label ? ` (${r.v.label})` : '';
    const k = `${r.v.what}${label} thread=${r.v.thread ?? '-'}`;
    if (!groups.has(k)) groups.set(k, { median: [], p90: [], max: [], alloc: [], unit: (r.v.median || '').replace(/[\d.]/g, '') });
    const g = groups.get(k);
    g.median.push(num(r.v.median)); g.p90.push(num(r.v.p90)); g.max.push(num(r.v.max)); if (r.v.allocB) g.alloc.push(num(r.v.allocB));
    const sync = r.line.match(/syncPartOnMain=\(n=\d+ median=([\d.]+)ms p90=([\d.]+)ms min=[\d.]+ms max=([\d.]+)ms/); if (sync) { (g.sync ??= []).push(num(sync[1])); (g.syncMax ??= []).push(num(sync[3])); }
  }
  for (const [k, g] of groups) say(`  ${k}: median ${range(g.median)} ${g.unit}, p90 ${range(g.p90)}, max ${range(g.max)}${g.alloc.length ? `, alloc/call ${range(g.alloc)} B` : ''}${g.sync ? `; synchronous part on the main thread median ${range(g.sync)} ms, max ${range(g.syncMax)} ms` : ''}`);
}

function pingTable(logs) {
  const groups = new Map();
  for (const r of collect(logs, 'UI-PING')) {
    if (!groups.has(r.v.label)) groups.set(r.v.label, { median: [], p90: [], max: [], cost: [] });
    const g = groups.get(r.v.label);
    g.median.push(num(r.v.median)); g.p90.push(num(r.v.p90)); g.max.push(num(r.v.max));
    const c = r.line.match(/sampleCost=\(n=\d+ median=([\d.]+)ms/); if (c) g.cost.push(num(c[1]));
  }
  for (const [k, g] of groups) say(`  ${k}: ping median ${range(g.median)} ms, p90 ${range(g.p90)}, max ${range(g.max)}${g.cost.length ? `; sample median ${range(g.cost)} ms` : ''}`);
}

function backend(logs) {
  for (const label of ['host', 'host-after-gc']) {
    const rows = collect(logs, 'BACKEND-VALUES', (l) => l.includes(`label=${label} `));
    if (!rows.length) continue;
    const keys = Object.keys(rows[0].v).filter((k) => k.endsWith('MB'));
    say(`  values ${label}: ` + keys.map((k) => `${k} ${range(rows.map((r) => num(r.v[k])))}`).join('; '));
  }
  const costs = new Map();
  for (const r of collect(logs, 'BACKEND-COST')) {
    const m = r.line.match(/read=(.+?) n=\d+ median=([\d.]+)(\w+) .* allocB=(\d+)/);
    if (!m) continue;
    if (!costs.has(m[1])) costs.set(m[1], { v: [], unit: m[3], alloc: [] });
    costs.get(m[1]).v.push(num(m[2])); costs.get(m[1]).alloc.push(num(m[4]));
  }
  for (const [k, g] of costs) say(`  cost ${k}: ${range(g.v)} ${g.unit}, alloc ${range(g.alloc)} B`);
}

function sampler(logs) {
  const groups = new Map();
  for (const r of collect(logs, 'SAMPLER')) {
    if (!groups.has(r.v.label)) groups.set(r.v.label, []);
    groups.get(r.v.label).push(r.v);
  }
  for (const [k, rows] of groups) {
    say(`  ${k}: ticks ${range(rows.map((r) => num(r.ticks)))}, written ${range(rows.map((r) => num(r.written)))}, dropped ${range(rows.map((r) => num(r.dropped)))}, ` +
      `received ${range(rows.map((r) => num(r.received)))}, read+write alloc/tick ${range(rows.map((r) => num(r.readWriteAllocatedBPerTick)))} B, process-wide steady alloc/tick ${range(rows.map((r) => num(r.steadyAllocatedBPerTick)))} B`);
  }
}

function totals(logs, label, keys) {
  const rows = collect(logs, 'MEM-TOTAL', (l) => l.includes(`label=${label} `));
  say(`  ${label} (n=${rows.length}): ` + keys.map((k) => `${k} ${range(rows.map((r) => num(r.v[k])))}`).join('; '));
}

function walks(logs, kinds) {
  for (const kind of kinds) {
    const subset = logs.filter((l) => l.file.includes(`-${kind}-`));
    if (!subset.length) continue;
    say(`  ${kind} (runs=${subset.length}):`);
    const groups = new Map();
    for (const r of collect(subset, 'WALK')) {
      const last = r.line.replace(/first=\([^)]*\)/, '').replace(/repeat=\([^)]*\)/, '');
      const v = kv(last);
      const k = `${r.v.label} ${r.v.target} ${r.v.method}`;
      if (!groups.has(k)) groups.set(k, { files: [], logical: [], allocated: [], ms: [], first: [], errors: [] });
      const g = groups.get(k);
      g.files.push(num(v.files)); g.logical.push(num(v.logicalKB)); g.allocated.push(num(v.allocatedKB)); g.errors.push(num(v.errors));
      const rep = r.line.match(/repeat=\(n=\d+ median=([\d.]+)ms/); if (rep) g.ms.push(num(rep[1]));
      const first = r.line.match(/first=\([^)]*ms=([\d.]+)\)/); if (first) g.first.push(num(first[1]));
    }
    for (const [k, g] of groups) say(`    ${k}: files ${range(g.files)}, logical ${range(g.logical)} kB, allocated ${range(g.allocated)} kB, errors ${range(g.errors)}, first ${range(g.first)} ms, repeat median ${range(g.ms)} ms`);
  }
}

// ---- Windows -------------------------------------------------------------------------------------------------
{
  const dir = path.join(root, 'win');
  const memory = read(dir, /^memory-\d+\.log$/);
  if (memory.length) {
    say(`# Windows memory scenario: ${memory.length} runs`);
    say('## Processes (GetProcessInfos, command-line scan)');
    for (const r of collect(memory, 'PROCESSES')) say(`  ${r.file}: ${r.line.slice(10)}`);
    for (const r of collect(memory, 'EXTENDED')) say(`  ${r.file}: ${r.line}`);
    for (const r of collect(memory, 'THREADING')) say(`  ${r.file}: ${r.line}`);
    say('## Totals over the browser processes (MB)');
    totals(memory, 'loaded', ['processes', 'workingSetMB', 'privateWorkingSetMB', 'privateUsageMB', 'sharedCommitMB', 'qwsPrivateMB', 'qwsProportionalMB', 'hostWorkingSetMB', 'hostPrivateWorkingSetMB', 'hostPrivateUsageMB']);
    say('## Per kind, private working set / private usage / working set (kB)');
    const kinds = new Map();
    for (const r of collect(memory, 'MEM', (l) => l.includes('label=loaded '))) {
      const k = r.v.kind; if (!kinds.has(k)) kinds.set(k, { pws: [], pu: [], ws: [], qp: [] });
      const g = kinds.get(k); g.pws.push(num(r.v.privateWorkingSetKB)); g.pu.push(num(r.v.privateUsageKB)); g.ws.push(num(r.v.workingSetKB)); g.qp.push(num(r.v.qwsProportionalKB));
    }
    for (const [k, g] of kinds) say(`  ${k} (n=${g.pws.length}): private WS ${range(g.pws)}; private usage ${range(g.pu)}; WS ${range(g.ws)}; proportional ${range(g.qp)}`);
    say('## Performance counter "Working Set - Private" against EX2 PrivateWorkingSetSize (same pid, before and after the counter read)');
    const deltas = [];
    for (const { file, lines } of memory) {
      const counterFile = path.join(dir, file.replace('.log', '.counters.txt'));
      if (!fs.existsSync(counterFile)) continue;
      const counters = Object.fromEntries(fs.readFileSync(counterFile, 'utf8').split(/\r?\n/).filter((l) => l.startsWith('COUNTER')).map((l) => { const v = kv(l); return [v.pid, v]; }));
      for (const line of lines.filter((l) => l.startsWith('MEM label=before-counters') || l.startsWith('MEM label=after-counters'))) {
        const v = kv(line); const c = counters[v.pid];
        if (c && line.includes('before-counters')) {
          const after = kv(lines.find((l) => l.startsWith('MEM label=after-counters') && l.includes(`pid=${v.pid} `)) || '');
          deltas.push(`${file} ${v.kind}:${v.pid} counter=${c.wsPrivateKB} ex2-before=${v.privateWorkingSetKB} ex2-after=${after.privateWorkingSetKB} | privateBytes counter=${c.privateBytesKB} ex2=${v.privateUsageKB} | WS counter=${c.workingSetKB} ex2=${v.workingSetKB}`);
        }
      }
    }
    for (const d of deltas) say('  ' + d);
    say('## Costs');
    costTable(memory);
    say('## UI thread (STA) ping latency');
    pingTable(memory);
    say('## Backend');
    backend(memory);
    say('## Sampler sketch');
    sampler(memory);
    say();
  }
  const disk = read(dir, /^disk-.*\.log$/);
  if (disk.length) {
    say(`# Windows disk scenario: ${disk.length} runs`);
    for (const r of collect(disk, 'PAGE')) { const m = r.line.match(/message=(.*)$/); try { const j = JSON.parse(m[1]); say(`  ${r.file}: fillMs=${j.fillMs} estimate usage=${j.estimate?.usage} details=${JSON.stringify(j.estimate?.usageDetails)} steps=${JSON.stringify(j.steps)}`); } catch { say(`  ${r.file}: ${r.line.slice(0, 200)}`); } }
    for (const r of collect(disk, 'LOCKED')) say(`  ${r.file}: ${r.line}`);
    walks(disk, ['realistic', 'large']);
    say('## Largest profile parts (running, then exited), realistic run 1');
    for (const r of collect(disk.filter((l) => l.file === 'disk-realistic-1.log'), 'WALK-PART', (l) => !l.includes('logicalKB=0 allocatedKB=0'))) say(`  ${r.line}`);
    say();
  }
  const shared = read(dir, /^shared-.*\.log$/);
  if (shared.length) {
    say(`# Windows shared-folder scenario: ${shared.length} logs`);
    for (const r of collect(shared, 'SHARED-LIST')) say(`  ${r.file}: ${r.line.slice(12)}`);
    for (const r of collect(shared, 'SHARED-FRAMES')) say(`  ${r.file}: ${r.line.slice(14)}`);
    for (const r of collect(shared, 'MEM-TOTAL')) say(`  ${r.file}: ${r.line.slice(10, 120)}`);
    say();
  }
  if (out.length) fs.writeFileSync(path.join(root, 'summary-win.txt'), out.join('\n') + '\n');
}

// ---- Linux ---------------------------------------------------------------------------------------------------
out.length = 0;
{
  const dir = path.join(root, 'linux');
  const memory = read(dir, /^memory-\d+\.log$/);
  if (memory.length) {
    say(`# Linux memory scenario: ${memory.length} runs`);
    for (const r of collect(memory, 'PROCESSES')) say(`  ${r.file}: ${r.line.slice(10)}`);
    for (const r of collect(memory.slice(0, 1), 'NSPID')) say(`  ${r.file}: ${r.line}`);
    say('## Totals over the descendants (MB)');
    totals(memory, 'loaded', ['processes', 'rssMB', 'pssMB', 'ussMB', 'privateDirtyMB', 'pssAnonMB', 'pssFileMB', 'pssShmemMB', 'hostRssMB', 'hostPssMB', 'hostUssMB', 'hostPrivateDirtyMB']);
    say('## Per process name, PSS / USS / Private_Dirty / RSS (kB)');
    const names = new Map();
    for (const r of collect(memory, 'MEM', (l) => l.includes('label=loaded '))) {
      const k = r.v.name; if (!names.has(k)) names.set(k, { pss: [], uss: [], pd: [], rss: [] });
      const g = names.get(k); g.pss.push(num(r.v.pssKB)); g.uss.push(num(r.v.ussKB)); g.pd.push(num(r.v.privateDirtyKB)); g.rss.push(num(r.v.rssKB));
    }
    for (const [k, g] of names) say(`  ${k} (n=${g.pss.length}): PSS ${range(g.pss)}; USS ${range(g.uss)}; private dirty ${range(g.pd)}; RSS ${range(g.rss)}`);
    say('## Costs');
    costTable(memory);
    say('## Main-loop ping latency');
    pingTable(memory);
    say('## Backend');
    backend(memory);
    say('## Sampler sketch');
    sampler(memory);
    say();
  }
  const disk = read(dir, /^disk-.*\.log$/);
  if (disk.length) {
    say(`# Linux disk scenario: ${disk.length} runs`);
    for (const r of collect(disk, 'PAGE')) { const m = r.line.match(/message=(.*)$/); try { const j = JSON.parse(m[1]); say(`  ${r.file}: fillMs=${j.fillMs} estimate usage=${j.estimate?.usage} details=${JSON.stringify(j.estimate?.usageDetails)} steps=${JSON.stringify(j.steps)}`); } catch { say(`  ${r.file}: ${r.line.slice(0, 200)}`); } }
    for (const r of collect(disk, 'FETCH')) say(`  ${r.file}: ${r.line.slice(0, 600)}`);
    costTable(disk);
    for (const r of collect(disk, 'VIEW-GONE')) say(`  ${r.file}: ${r.line}`);
    walks(disk, ['realistic', 'large']);
    say('## Parts, realistic run 1');
    for (const r of collect(disk.filter((l) => l.file === 'disk-realistic-1.log'), 'WALK-PART', (l) => !l.includes('logicalKB=0 '))) say(`  ${r.line}`);
    say();
  }
  if (out.length) fs.writeFileSync(path.join(root, 'summary-linux.txt'), out.join('\n') + '\n');
}
console.log('summaries written to', root);
