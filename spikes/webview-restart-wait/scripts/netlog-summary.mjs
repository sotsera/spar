// Prints what a browser's net log (--log-net-log, Default capture mode) says about requests to hosts other than the
// app's own origin: per source, the first URL or host, when it started and its last event, relative to the log's first
// event, plus the log's last event. Query strings are dropped. Used for the SmartScreen request at shutdown.
//   node scripts/netlog-summary.mjs <net-log.json>...
import { readFileSync } from 'node:fs';
import { basename } from 'node:path';

for (const file of process.argv.slice(2)) {
  const log = JSON.parse(readFileSync(file, 'utf8'));
  const types = Object.fromEntries(Object.entries(log.constants.logEventTypes).map(([name, value]) => [value, name]));
  const sourceTypes = Object.fromEntries(Object.entries(log.constants.logSourceType).map(([name, value]) => [value, name]));
  const start = +log.events[0].time;
  const sources = new Map();
  for (const event of log.events) {
    const id = event.source.id;
    const params = event.params ?? {};
    const target = params.url ?? params.host ?? params.group_name ?? params.address ?? null;
    let source = sources.get(id);
    if (!source) {
      source = { type: sourceTypes[event.source.type], first: +event.time - start, target: null, last: '', lastAt: 0, errors: [] };
      sources.set(id, source);
    }

    if (!source.target && target) source.target = String(target).replace(/\?.*/, '');
    if (params.net_error) source.errors.push(`${types[event.type]} ${params.net_error}`);
    source.last = types[event.type];
    source.lastAt = +event.time - start;
  }

  console.log(`${basename(file)}: ${log.events.length} events over ${+log.events.at(-1).time - start} ms; last ${types[log.events.at(-1).type]}`);
  for (const [id, source] of sources) {
    if (!source.target || /app\.localhost/.test(source.target)) continue;
    if (!/URL_REQUEST|SOCKET|CONNECT_JOB|HOST_RESOLVER|HTTP_STREAM_JOB|QUIC/.test(source.type)) continue;
    console.log(`  #${id} ${source.type} ${source.target} +${source.first}..+${source.lastAt} ms, last ${source.last}${source.errors.length ? ', errors ' + source.errors.join(' ') : ''}`);
  }
}
