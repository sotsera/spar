// Usage: node tools/summarize.mjs <log folder> [<out file>]
// Reads every <case>-<n>.log of one platform (host logs: "T KEY k=v ..."), groups runs by case and prints:
// detection (hang start -> HB-UNRESPONSIVE), misses, false alarms, probe round trips and timer ticks per phase
// (phases are delimited by ACTION lines), ProcessFailed / WebKit responsiveness events, recovery times, stress costs,
// pump latency, and an offline simulation of other ping intervals and miss counts from the 1 s probe pings.
import fs from 'node:fs';
import path from 'node:path';

const dir = process.argv[2];
const outFile = process.argv[3];
const lines = [];
const say = (s = '') => lines.push(s);

const med = (a) => {
  if (!a.length) return NaN;
  const s = [...a].sort((x, y) => x - y);
  return s.length % 2 ? s[(s.length - 1) / 2] : (s[s.length / 2 - 1] + s[s.length / 2]) / 2;
};
const q = (a, p) => {
  if (!a.length) return NaN;
  const s = [...a].sort((x, y) => x - y);
  return s[Math.min(s.length - 1, Math.round(p * (s.length - 1)))];
};
const f0 = (x) => (Number.isFinite(x) ? x.toFixed(0) : '-');
const f1 = (x) => (Number.isFinite(x) ? x.toFixed(1) : '-');
const range = (a, f = f0) => (a.length ? `${f(med(a))} (${f(Math.min(...a))}-${f(Math.max(...a))}, n=${a.length})` : '-');

function parse(file) {
  const events = [];
  for (const raw of fs.readFileSync(file, 'utf8').split(/\r?\n/)) {
    const m = raw.match(/^(\d+(?:\.\d+)?) (\S+) ?(.*)$/);
    if (!m) continue;
    const kv = {};
    for (const p of m[3].matchAll(/(\w[\w-]*)=(\S*)/g)) kv[p[1]] = p[2];
    events.push({ t: Number(m[1]), key: m[2], text: m[3], kv });
    if (m[2] === 'FINISH') break; // teardown (pagehide, hidden) is not part of the measurement
  }
  return events;
}

const files = fs.readdirSync(dir).filter((f) => /-\d+\.log$/.test(f));
const cases = new Map();
for (const f of files) {
  const name = f.replace(/-\d+\.log$/, '');
  if (!cases.has(name)) cases.set(name, []);
  cases.get(name).push({ file: f, ev: parse(path.join(dir, f)) });
}

// Phases: [start, first ACTION), then each ACTION until the next.
function phases(ev) {
  const actions = ev.filter((e) => e.key === 'ACTION');
  const list = [{ name: 'start', from: 0, to: actions.length ? actions[0].t : Infinity }];
  actions.forEach((a, i) => list.push({ name: a.kv.arg ? `${a.kv.verb}:${a.kv.arg}` : a.kv.verb, from: a.t, to: i + 1 < actions.length ? actions[i + 1].t : Infinity }));
  return list;
}

function simulate(ev, intervalS, misses) {
  // Detector pings = every intervalS-th probe ping; a tick finds the previous detector ping unanswered -> miss.
  const pings = ev.filter((e) => e.key === 'HB-PING' && e.kv.hb === 'probe').map((e) => ({ seq: Number(e.kv.seq), t: e.t }));
  const pongAt = new Map(ev.filter((e) => e.key === 'HB-PONG' && e.kv.hb === 'probe').map((e) => [Number(e.kv.seq), e.t]));
  const chosen = pings.filter((p) => p.seq % intervalS === 0);
  let missed = 0;
  const alarms = [];
  for (let i = 1; i < chosen.length; i++) {
    const prev = chosen[i - 1];
    const answered = pongAt.has(prev.seq) && pongAt.get(prev.seq) <= chosen[i].t;
    // Any answer of any probe ping before this tick also proves the page alive (the real detector resets on any pong).
    const anyAlive = [...pongAt.entries()].some(([s, t]) => s >= prev.seq && t > prev.t && t <= chosen[i].t);
    if (answered || anyAlive) missed = 0;
    else if (++missed === misses) alarms.push(chosen[i].t);
  }
  return alarms;
}

const sims = [[1, 3], [1, 5], [2, 3], [2, 5], [3, 3], [5, 2], [5, 3], [5, 4], [10, 2], [10, 3]];

say(`# Summary of ${dir} (${files.length} runs)`);
for (const [name, runs] of [...cases.entries()].sort()) {
  say();
  say(`## ${name} (${runs.length} runs)`);
  const detect = [], missesAt = [], falseAlarms = [], recoveredAfter = [];
  const rttByPhase = new Map(), tickByPhase = new Map(), framesByPhase = new Map(), visByPhase = new Map();
  const pump = new Map();
  const notes = [];
  const recov = new Map();
  const simRes = new Map(sims.map((s) => [s.join('x'), { detect: [], alarms: 0 }]));
  for (const { file, ev } of runs) {
    const hang = ev.find((e) => e.key === 'HANG');
    const unresp = ev.filter((e) => e.key === 'HB-UNRESPONSIVE');
    const ph = phases(ev);
    const phaseOf = (t) => ph.filter((p) => t >= p.from && t < p.to).map((p) => p.name)[0] ?? 'start';
    const add = (map, key, v) => { if (!map.has(key)) map.set(key, []); map.get(key).push(v); };
    for (const e of ev) {
      if (e.key === 'HB-PONG' && e.kv.hb === 'probe') add(rttByPhase, phaseOf(e.t), Number(e.kv.rttMs));
      if (e.key === 'TICK' && Number(e.kv.n) > 1) { add(tickByPhase, phaseOf(e.t), Number(e.kv.dtMs)); add(framesByPhase, phaseOf(e.t), Number(e.kv.frames)); add(visByPhase, phaseOf(e.t), e.kv.vis); }
      if (e.key === 'PUMP-PHASE') add(pump, e.kv.phase, `${e.kv.n} p50=${e.kv.median} p99=${e.kv.p99} max=${e.kv.max}`);
      if (['PROCESS-FAILED', 'WEB-PROCESS-TERMINATED', 'WEBKIT-RESPONSIVE', 'DIALOG-SHOW', 'DIALOG-CLOSED', 'DIALOG-AUTO-CLOSE', 'SUSPEND', 'RESUME', 'MEMORY-TARGET', 'CONTROLLER-VISIBLE', 'VIS', 'STRESS-END', 'MEMORY', 'INPUT', 'HB-PAUSE', 'HB-RESUME', 'KILL-RENDERER', 'BURN', 'REFUSED', 'ACTION-FAILED', 'HOST-FAILURE', 'TIMEOUT', 'EXEC', 'NAV-COMPLETED'].includes(e.key)) {
        const rel = hang ? ` (hang+${f0(e.t - hang.t)})` : '';
        notes.push(`${file} ${f0(e.t)}${rel} ${e.key} ${e.text.slice(0, 160)}`);
      }
      if (e.key.startsWith('RECOVERY-') && e.key !== 'RECOVERY-START') add(recov, `${e.kv.kind} ${e.key}`, Number(e.kv.ms));
    }
    const actionT = ev.filter((e) => e.key === 'ACTION' && ['reload', 'kill', 'nav'].includes(e.kv.verb));
    for (const a of actionT) {
      const ready = ev.find((e) => e.t > a.t && e.key === 'READY');
      const doc = ev.find((e) => e.t > a.t && e.key === 'DOC');
      add(recov, `from ACTION ${a.kv.verb}${a.kv.arg ? ':' + a.kv.arg : ''} to DOC`, doc ? doc.t - a.t : NaN);
      add(recov, `from ACTION ${a.kv.verb}${a.kv.arg ? ':' + a.kv.arg : ''} to READY`, ready ? ready.t - a.t : NaN);
      const seen = ev.filter((e) => e.t > a.t && e.key === 'PAGE' && e.text.startsWith('state-seen')).map((e) => e.text.split(':')[1]);
      if (seen.length) add(recov, 'backend counter seen after recovery', Number(seen[0]));
    }
    if (hang && unresp.length && unresp[0].t > hang.t) {
      detect.push(unresp[0].t - hang.t);
      missesAt.push(Number(unresp[0].kv.missed));
    }
    for (const u of unresp) if (!hang || u.t < hang.t) falseAlarms.push(`${file}@${f0(u.t)}`);
    if (name.startsWith('busy') || name === 'worker') for (const u of unresp) if (hang && u.t > hang.t) falseAlarms.push(`${file}@hang+${f0(u.t - hang.t)} (${name})`);
    for (const r of ev.filter((e) => e.key === 'HB-RESPONSIVE')) recoveredAfter.push(Number(r.kv.afterMs));
    for (const [k, v] of simRes) {
      const [i, m] = k.split('x').map(Number);
      const alarms = simulate(ev, i, m);
      const first = hang ? alarms.find((t) => t > hang.t) : undefined;
      if (first !== undefined && !(name.startsWith('busy') || name === 'worker')) v.detect.push(first - hang.t);
      v.alarms += alarms.filter((t) => !hang || t < hang.t || name.startsWith('busy') || name === 'worker').length;
    }
  }
  if (detect.length) say(`detection ms (hang start -> unresponsive): ${range(detect)}; misses at alarm ${[...new Set(missesAt)].join('/')}`);
  else say('detection: no alarm after a hang in any run');
  say(`alarms not caused by an endless hang: ${falseAlarms.length ? falseAlarms.join(', ') : 'none'}`);
  if (recoveredAfter.length) say(`responsive again after alarm (ms): ${range(recoveredAfter)}`);
  for (const [k, v] of recov) say(`recovery ${k}: ${range(v.filter(Number.isFinite))}${v.some((x) => !Number.isFinite(x)) ? ` never in ${v.filter((x) => !Number.isFinite(x)).length}` : ''}`);
  for (const [phase, v] of rttByPhase) say(`probe rtt ms [${phase}]: median ${f1(med(v))} p99 ${f1(q(v, 0.99))} max ${f1(Math.max(...v))} (n=${v.length})`);
  for (const [phase, v] of tickByPhase) {
    const fr = framesByPhase.get(phase) ?? [];
    const vis = [...new Set(visByPhase.get(phase) ?? [])].join('/');
    say(`timer tick ms [${phase}]: median ${f0(med(v))} p99 ${f0(q(v, 0.99))} max ${f0(Math.max(...v))} (n=${v.length}); rAF/s median ${f0(med(fr))} min ${f0(Math.min(...fr))}; vis ${vis}`);
  }
  for (const [phase, v] of pump) say(`pump [${phase}]: ${v.join(' | ')}`);
  const simLine = [...simRes.entries()].map(([k, v]) => `${k}: ${v.detect.length ? range(v.detect) : 'no detect'}${v.alarms ? ` alarms=${v.alarms}` : ''}`);
  say(`simulated (interval s x misses, from 1 s probes): ${simLine.join('; ')}`);
  if (notes.length) {
    say('events:');
    for (const n of notes.slice(0, 60)) say(`  ${n}`);
    if (notes.length > 60) say(`  ... ${notes.length - 60} more`);
  }
}

const text = lines.join('\n') + '\n';
if (outFile) fs.writeFileSync(outFile, text);
process.stdout.write(text);
