// Summarizes the runner's timelines: node spikes/sandbox-runner/summarize.mjs artifacts/sandbox-runner/sessions/<name>...
// One row per session (sandbox steps) and one per job (stage, run, collect), in milliseconds.
import { readFileSync, existsSync } from 'node:fs';
import { basename, join } from 'node:path';

const sessions = [];
const jobs = [];
for (const folder of process.argv.slice(2)) {
  const file = join(folder, 'timeline.jsonl');
  if (!existsSync(file)) continue;
  const steps = readFileSync(file, 'utf8').split('\n').filter(Boolean).map(line => JSON.parse(line));
  const one = name => steps.find(s => s.step === name);
  const name = basename(folder);
  const minimizes = steps.filter(s => s.step === 'client-minimize');
  sessions.push({
    session: name,
    server: one('list-before')?.serverProcesses,
    start: one('start')?.ms,
    'exec-ready': one('exec-ready-System')?.ms,
    'start→exec': one('exec-ready-System')?.sinceStart_ms,
    connect: one('connect')?.ms ?? '-',
    minimizes: minimizes.length || '-',
    'last-minimize': minimizes.at(-1)?.since_connect_ms ?? '-',
    stop: one('stop')?.ms,
    close: one('close-clients')?.ms,
    'left-after-close': one('close-clients')?.windowsLeftAfterClose,
    total: one('total')?.ms,
  });
  for (const run of steps.filter(s => s.step === 'run')) {
    const visible = steps.find(s => s.step === 'stage-visible' && s.run === run.run);
    const copy = steps.find(s => s.step === 'stage-copy' && s.run === run.run);
    const collect = steps.find(s => s.step === 'collect' && s.run === run.run);
    const counters = /total="(\d+)" executed="(\d+)" passed="(\d+)" failed="(\d+)"/.exec(collect?.trx ?? '');
    jobs.push({
      session: name,
      id: run.run,
      as: run.as,
      copy: copy?.ms,
      visible: visible?.ms,
      'visible-probes': visible?.attempts,
      run: run.ms,
      exit: run.guestExit,
      timedOut: run.timedOut,
      collect: collect?.ms,
      'exit-file': collect?.exitFile,
      tests: counters ? `${counters[3]}/${counters[1]} passed, ${counters[4]} failed` : collect?.trx,
    });
  }
}

const table = rows => {
  if (rows.length === 0) return '';
  const keys = Object.keys(rows[0]);
  return [`| ${keys.join(' | ')} |`, `|${keys.map(() => '---').join('|')}|`, ...rows.map(r => `| ${keys.map(k => r[k] ?? '').join(' | ')} |`)].join('\n');
};
console.log(table(sessions));
console.log();
console.log(table(jobs));
