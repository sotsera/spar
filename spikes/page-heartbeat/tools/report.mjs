// Usage (from spikes/page-heartbeat): node tools/report.mjs [<out file>]
// Compact tables for RESULTS.md from the raw host logs in results/win, results/linux and results/sandbox/<group>.
// Everything is read up to the run's FINISH line (teardown is not measured), except where a table says otherwise.
// Times are milliseconds from the host's own clock (log column 1). Medians with (min-max) over runs.
import fs from 'node:fs';
import path from 'node:path';

const out = [];
const say = (s = '') => out.push(s);

const med = (a) => {
  if (!a.length) return NaN;
  const s = [...a].sort((x, y) => x - y);
  return s.length % 2 ? s[(s.length - 1) / 2] : (s[s.length / 2 - 1] + s[s.length / 2]) / 2;
};
const fx = (x, d = 1) => (Number.isFinite(x) ? x.toFixed(d) : '-');
const sec = (a) => (a.length ? `${fx(med(a) / 1000)} (${fx(Math.min(...a) / 1000)}-${fx(Math.max(...a) / 1000)})` : '-');
const ms = (a, d = 0) => (a.length ? `${fx(med(a), d)} (${fx(Math.min(...a), d)}-${fx(Math.max(...a), d)})` : '-');

function parse(file) {
  const all = [];
  for (const raw of fs.readFileSync(file, 'utf8').split(/\r?\n/)) {
    const m = raw.match(/^(\d+(?:\.\d+)?) (\S+) ?(.*)$/);
    if (!m) continue;
    const kv = {};
    for (const p of m[3].matchAll(/(\w[\w-]*)=(\S*)/g)) kv[p[1]] = p[2];
    all.push({ t: Number(m[1]), key: m[2], text: m[3], kv });
  }
  const fin = all.findIndex((e) => e.key === 'FINISH');
  return { ev: fin >= 0 ? all.slice(0, fin + 1) : all, all, finished: fin >= 0 };
}

function load(dir) {
  const cases = new Map();
  for (const f of fs.readdirSync(dir).filter((x) => /-\d+\.log$/.test(x)).sort()) {
    const name = f.replace(/-\d+\.log$/, '');
    if (!cases.has(name)) cases.set(name, []);
    cases.get(name).push({ file: f, ...parse(path.join(dir, f)) });
  }
  return cases;
}

const hosts = [
  ['Windows host', load('results/win')],
  ['Linux container', load('results/linux')],
];
const sandbox = new Map();
for (const g of fs.readdirSync('results/sandbox')) {
  for (const [k, v] of load(path.join('results/sandbox', g))) sandbox.set(k, v);
}
hosts.push(['Windows Sandbox', sandbox]);

const first = (ev, key, after = -Infinity) => ev.find((e) => e.key === key && e.t > after);
const finite = (name) => /^(busy|dialog-recover|wk-busy)/.test(name);

// ---- 1. Detection --------------------------------------------------------------------------------------------------
say('## Detection: hang start (page HANG message) to HB-UNRESPONSIVE, 5 s interval, 3 misses');
say();
say('| Host | Case | Runs | Alarmed | Detection s, median (min-max) | Responsive again after alarm, s |');
say('|---|---|---|---|---|---|');
for (const [host, cases] of hosts) {
  for (const [name, runs] of cases) {
    const det = [], back = [];
    let alarmed = 0, withHang = 0;
    for (const r of runs) {
      const hang = first(r.ev, 'HANG');
      if (!hang) continue;
      withHang++;
      const u = first(r.ev, 'HB-UNRESPONSIVE', hang.t);
      if (u) { alarmed++; det.push(u.t - hang.t); }
      const resp = first(r.ev, 'HB-RESPONSIVE', u ? u.t : Infinity);
      if (resp) back.push(Number(resp.kv.afterMs));
    }
    if (!withHang) continue;
    say(`| ${host} | ${name} | ${runs.length} | ${alarmed}/${withHang} | ${sec(det)} | ${back.length ? sec(back) : '-'} |`);
  }
}
say();

// ---- 2. Alarms without an endless main-thread hang -----------------------------------------------------------------
say('## Alarms in runs without an endless main-thread hang (healthy pages, window states, finite work)');
say();
say('| Host | Case | Runs | Runs with an alarm | Probe round trip ms: median of run medians / max | Page 1 s timer interval ms: median / max | rAF per s: median / min |');
say('|---|---|---|---|---|---|---|');
const endless = (r) => r.ev.some((e) => e.key === 'HANG' && /kind=(idle|handler|microtask|load)/.test(e.text));
for (const [host, cases] of hosts) {
  for (const [name, runs] of cases) {
    if (runs.some(endless) && !finite(name)) continue;
    let alarmedRuns = 0;
    const rttMed = [], rttMax = [], tick = [], frames = [];
    for (const r of runs) {
      if (r.ev.some((e) => e.key === 'HB-UNRESPONSIVE')) alarmedRuns++;
      const rtt = r.ev.filter((e) => e.key === 'HB-PONG' && e.kv.hb === 'probe').map((e) => Number(e.kv.rttMs));
      if (rtt.length) { rttMed.push(med(rtt)); rttMax.push(Math.max(...rtt)); }
      for (const e of r.ev) if (e.key === 'TICK' && Number(e.kv.n) > 1) { tick.push(Number(e.kv.dtMs)); frames.push(Number(e.kv.frames)); }
    }
    say(`| ${host} | ${name} | ${runs.length} | ${alarmedRuns} | ${rttMed.length ? `${fx(med(rttMed))} / ${fx(Math.max(...rttMax))}` : 'no probe'} | ${tick.length ? `${fx(med(tick), 0)} / ${fx(Math.max(...tick), 0)}` : '-'} | ${frames.length ? `${fx(med(frames), 0)} / ${fx(Math.min(...frames), 0)}` : '-'} |`);
  }
}
say();

// ---- 3. Window and WebView states, per phase -------------------------------------------------------------------------
say('## Ungated pings per phase in the state cases (gate off): probe round trip, page timer, rAF, page visibility');
say();
say('| Host | Case | Phase | Probe rtt ms median / max | Probes answered / sent | Timer interval ms median / max | rAF/s median | visibilityState |');
say('|---|---|---|---|---|---|---|---|');
const stateCase = /^(st-|ctl-|minimize|cover$|offscreen|suspend|mem-low|gate-minimize-hang|onscreen)/;
for (const [host, cases] of hosts) {
  for (const [name, runs] of cases) {
    if (!stateCase.test(name)) continue;
    const phases = new Map();
    for (const r of runs) {
      const actions = r.ev.filter((e) => e.key === 'ACTION');
      const phaseOf = (t) => {
        let p = 'start';
        for (const a of actions) if (t >= a.t) p = a.kv.verb === 'sh' ? `sh:${a.text.includes('3000 3000') ? 'move-offscreen' : 'move-back'}` : a.kv.verb + (a.kv.arg ? ':' + a.kv.arg : '');
        return p;
      };
      const get = (p) => { if (!phases.has(p)) phases.set(p, { rtt: [], sent: 0, ans: 0, tick: [], fr: [], vis: new Set() }); return phases.get(p); };
      for (const e of r.ev) {
        if (e.key === 'HB-PING' && e.kv.hb === 'probe') get(phaseOf(e.t)).sent++;
        if (e.key === 'HB-PONG' && e.kv.hb === 'probe') { const p = get(phaseOf(e.t)); p.ans++; p.rtt.push(Number(e.kv.rttMs)); p.vis.add(e.kv.vis); }
        if (e.key === 'TICK' && Number(e.kv.n) > 1) { const p = get(phaseOf(e.t)); p.tick.push(Number(e.kv.dtMs)); p.fr.push(Number(e.kv.frames)); }
      }
    }
    for (const [p, v] of phases) {
      if (p === 'start') continue;
      say(`| ${host} | ${name} | ${p} | ${v.rtt.length ? `${fx(med(v.rtt))} / ${fx(Math.max(...v.rtt))}` : '-'} | ${v.ans} / ${v.sent} | ${v.tick.length ? `${fx(med(v.tick), 0)} / ${fx(Math.max(...v.tick), 0)}` : 'none'} | ${v.fr.length ? fx(med(v.fr), 0) : '-'} | ${[...v.vis].join('/') || '-'} |`);
    }
  }
}
say();

// ---- 4. Engine signals --------------------------------------------------------------------------------------------
say('## Engine signals: ProcessFailed (WebView2) and is-web-process-responsive / web-process-terminated (WebKitGTK)');
say();
say('Times are from the hang start, and from the last action before the event. WebKit flips after FINISH (teardown) are excluded.');
say();
say('| Host | Case | Runs | Event | Runs with it | From hang, s | From last action, s (action) |');
say('|---|---|---|---|---|---|---|');
for (const [host, cases] of hosts) {
  for (const [name, runs] of cases) {
    const groups = new Map();
    for (const r of runs) {
      const hang = first(r.ev, 'HANG');
      const seen = new Set();
      let last = null;
      for (const e of r.ev) {
        if (e.key === 'ACTION') last = e;
        let label = null;
        if (e.key === 'PROCESS-FAILED') label = `ProcessFailed ${e.kv.kind}/${e.kv.reason}`;
        if (e.key === 'WEBKIT-RESPONSIVE') label = `is-web-process-responsive=${e.kv.value}`;
        if (e.key === 'WEB-PROCESS-TERMINATED') label = `web-process-terminated ${e.kv.reason}`;
        if (!label || seen.has(label)) continue;
        seen.add(label);
        if (!groups.has(label)) groups.set(label, { runs: 0, fromHang: [], fromAction: [], actions: new Set() });
        const g = groups.get(label);
        g.runs++;
        if (hang && e.t > hang.t) g.fromHang.push(e.t - hang.t);
        if (last) { g.fromAction.push(e.t - last.t); g.actions.add(last.kv.verb + (last.kv.arg && last.kv.verb !== 'sh' ? ':' + last.kv.arg : '')); }
      }
    }
    for (const [label, g] of groups) say(`| ${host} | ${name} | ${runs.length} | ${label} | ${g.runs} | ${sec(g.fromHang)} | ${sec(g.fromAction)} (${[...g.actions].join(', ')}) |`);
  }
}
say();

// ---- 5. Recovery --------------------------------------------------------------------------------------------------
say('## Recovery: from the recovery action to the new document (DOC), DOMContentLoaded (READY) and the first heartbeat answer');
say();
say('"never" counts runs where the step did not happen before FINISH. Backend counter: the value the new document read from the host (it was 2 before the hang in the hung cases, 1 in reload-load/kill-load which never load).');
say();
say('| Host | Case | Runs | Action | DOC ms | READY ms | First answer ms | Never ready | Backend counter seen |');
say('|---|---|---|---|---|---|---|---|---|');
for (const [host, cases] of hosts) {
  for (const [name, runs] of cases) {
    const rows = new Map();
    for (const r of runs) {
      // A kill is followed within milliseconds by the host's one automatic reload after the renderer exited: both are
      // one recovery, timed from the first start.
      const starts = r.ev.filter((e) => e.key === 'RECOVERY-START');
      const continues = (s, i) => s.kv.kind === 'auto-reload-after-exit' && i > 0 && s.t - starts[i - 1].t < 1000;
      starts.forEach((s, i) => {
        if (continues(s, i)) return;
        const viaAuto = i + 1 < starts.length && continues(starts[i + 1], i + 1);
        const kind = s.kv.kind + (viaAuto ? ' + auto reload' : '');
        if (!rows.has(kind)) rows.set(kind, { doc: [], ready: [], pong: [], never: 0, n: 0, counter: [] });
        const row = rows.get(kind);
        row.n++;
        const next = starts.find((e, j) => j > i && !continues(e, j));
        const until = next ? next.t : Infinity;
        const within = (key) => r.ev.find((e) => e.key === key && e.t >= s.t && e.t < until);
        const doc = within('RECOVERY-DOC'), ready = within('RECOVERY-READY'), pong = within('RECOVERY-PONG');
        if (doc) row.doc.push(doc.t - s.t);
        if (ready) row.ready.push(ready.t - s.t); else row.never++;
        if (pong) row.pong.push(pong.t - s.t);
        const seen = r.ev.find((e) => e.key === 'PAGE' && e.text.startsWith('state-seen') && e.t > s.t && e.t < until);
        if (seen) row.counter.push(Number(seen.text.split(':')[1]));
      });
    }
    for (const [kind, v] of rows) say(`| ${host} | ${name} | ${runs.length} | ${kind} | ${ms(v.doc)} | ${ms(v.ready)} | ${ms(v.pong)} | ${v.never}/${v.n} | ${v.counter.length ? [...new Set(v.counter)].join('/') : '-'} |`);
  }
}
say();

// ---- 6. Posting to a hung page: memory and pump --------------------------------------------------------------------
say('## Posting 1,000 messages of 1 KiB per second for 30 s: private memory (kB) before, at the end and 8 s later');
say();
say('| Host | Case | Runs | Messages sent | Mean / max post µs | Host before -> later | Renderer before -> peak -> later | Browser before -> later |');
say('|---|---|---|---|---|---|---|---|');
for (const [host, cases] of hosts) {
  for (const name of ['stress-healthy', 'stress-hung']) {
    const runs = cases.get(name);
    if (!runs) continue;
    const sent = [], mean = [], max = [], hostB = [], hostL = [], rB = [], rP = [], rL = [], bB = [], bL = [];
    for (const r of runs) {
      const end = first(r.ev, 'STRESS-END');
      if (end) { sent.push(Number(end.kv.sent)); mean.push(Number(end.kv.meanPostUs)); max.push(Number(end.kv.maxPostUs)); }
      const mem = r.ev.filter((e) => e.key === 'MEMORY');
      const renderer = (e) => Number((e.text.match(/(?:renderer|web):(\d+)/) || [])[1]);
      const browser = (e) => Number((e.text.match(/browser:(\d+)/) || [])[1]);
      const before = mem.find((e) => e.kv.label === 'before'), later = mem.find((e) => e.kv.label === 'later');
      if (before && later) {
        hostB.push(Number(before.kv.hostKb)); hostL.push(Number(later.kv.hostKb));
        rB.push(renderer(before)); rL.push(renderer(later)); rP.push(Math.max(...mem.map(renderer)));
        if (Number.isFinite(browser(before))) { bB.push(browser(before)); bL.push(browser(later)); }
      }
    }
    say(`| ${host} | ${name} | ${runs.length} | ${ms(sent)} | ${fx(med(mean))} / ${fx(Math.max(...max), 0)} | ${fx(med(hostB), 0)} -> ${fx(med(hostL), 0)} | ${fx(med(rB), 0)} -> ${fx(med(rP), 0)} -> ${fx(med(rL), 0)} | ${bB.length ? `${fx(med(bB), 0)} -> ${fx(med(bL), 0)}` : '-'} |`);
  }
}
say();

// ---- 7. UI-thread pump latency ---------------------------------------------------------------------------------------
say('## UI-thread pump latency (post to run, 1 ms cadence) by phase, all runs of a case: median of p99 / worst max, ms');
say();
say('| Host | Case | Phase | Samples | p99 (median over runs) | Max (worst run) |');
say('|---|---|---|---|---|---|');
const pumpCases = /^(idle|handler|pf-pings|pf-quiet|stress-hung|stress-healthy|reload|wk-pings|baseline)$/;
for (const [host, cases] of hosts) {
  for (const [name, runs] of cases) {
    if (!pumpCases.test(name)) continue;
    const phases = new Map();
    for (const r of runs) {
      for (const e of r.ev.filter((x) => x.key === 'PUMP-PHASE')) {
        if (!phases.has(e.kv.phase)) phases.set(e.kv.phase, { n: 0, p99: [], max: [] });
        const p = phases.get(e.kv.phase);
        p.n += Number(e.kv.n); p.p99.push(parseFloat(e.kv.p99)); p.max.push(parseFloat(e.kv.max));
      }
    }
    for (const [p, v] of phases) say(`| ${host} | ${name} | ${p} | ${v.n} | ${fx(med(v.p99), 2)} | ${fx(Math.max(...v.max), 2)} |`);
  }
}
say();

// ---- 8. Other intervals, simulated from the 1 s probe ---------------------------------------------------------------
// A detector with interval I s and M misses is replayed on the probe pings whose sequence number is a multiple of I.
const sims = [[1, 3], [2, 3], [2, 5], [3, 3], [5, 2], [5, 3], [5, 4], [10, 2]];
function simulate(ev, interval, misses) {
  const pings = ev.filter((e) => e.key === 'HB-PING' && e.kv.hb === 'probe').map((e) => ({ seq: Number(e.kv.seq), t: e.t }));
  const pongs = ev.filter((e) => e.key === 'HB-PONG' && e.kv.hb === 'probe').map((e) => ({ seq: Number(e.kv.seq), t: e.t }));
  const chosen = pings.filter((p) => p.seq % interval === 0);
  let missed = 0;
  const alarms = [];
  for (let i = 1; i < chosen.length; i++) {
    const prev = chosen[i - 1];
    const alive = pongs.some((p) => p.seq >= prev.seq && p.t > prev.t && p.t <= chosen[i].t);
    if (alive) missed = 0;
    else if (++missed === misses) alarms.push(chosen[i].t);
  }
  return alarms;
}
say('## Other intervals and miss counts, replayed on the 1 s probe pings (Windows host and Linux container)');
say();
say('Detection over idle, handler and microtask runs; false-alarm runs over runs without an endless hang (baseline, anim, tasks100, tasks1000, worker, busy3, busy10, stress-healthy, state cases). busy20 is listed separately: a 20 s task exceeds most thresholds.');
say();
say('| Interval x misses | Detection s, median (min-max) | False-alarm runs | busy20 runs alarmed |');
say('|---|---|---|---|');
const healthyNames = /^(baseline|anim|tasks100|tasks1000|worker|busy3|busy10|stress-healthy|st-|ctl-hidden|mem-low)/;
for (const [i, m] of sims) {
  const det = []; let fa = 0, total = 0, b20 = 0, b20n = 0;
  for (const [, cases] of hosts.slice(0, 2)) {
    for (const [name, runs] of cases) {
      for (const r of runs) {
        const alarms = simulate(r.ev, i, m);
        const hang = first(r.ev, 'HANG');
        if (/^(idle|handler|microtask)$/.test(name) && hang) { const a = alarms.find((t) => t > hang.t); if (a) det.push(a - hang.t); }
        if (healthyNames.test(name)) { total++; if (alarms.length) fa++; }
        if (name === 'busy20') { b20n++; if (alarms.length) b20++; }
      }
    }
  }
  say(`| ${i} s x ${m} | ${sec(det)} | ${fa}/${total} | ${b20}/${b20n} |`);
}
say();

// ---- 9. Completeness -----------------------------------------------------------------------------------------------
say('## Completeness: runs per case, and runs without a FINISH line');
say();
say('| Host | Cases | Runs | Runs without FINISH |');
say('|---|---|---|---|');
for (const [host, cases] of hosts) {
  const runs = [...cases.values()].flat();
  say(`| ${host} | ${cases.size} | ${runs.length} | ${runs.filter((r) => !r.finished).map((r) => r.file).join(', ') || 'none'} |`);
}

const text = out.join('\n') + '\n';
if (process.argv[2]) fs.writeFileSync(process.argv[2], text);
process.stdout.write(text);
