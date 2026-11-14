// Usage: node tools/netlog.mjs <net log .json> <CLOSE-START FILETIME>
// Summarize a Chromium net log: every URL request and DNS/socket activity, with times relative to a given FILETIME.
import fs from 'node:fs';
const [file, closeFt] = process.argv.slice(2);
let text = fs.readFileSync(file, 'utf8').trim();
if (!text.endsWith('}')) text = text.replace(/,\s*$/, '') + ']}';
const log = JSON.parse(text);
const c = log.constants;
const typeName = Object.fromEntries(Object.entries(c.logEventTypes).map(([k, v]) => [v, k]));
const srcName = Object.fromEntries(Object.entries(c.logSourceType).map(([k, v]) => [v, k]));
const offset = Number(c.timeTickOffset);
const closeUnix = closeFt ? (BigInt(closeFt) - 116444736000000000n) / 10000n : null;
const rel = (t) => closeUnix === null ? Number(t) : Number(t) + offset - Number(closeUnix);
const sources = new Map();
for (const e of log.events) {
  const id = e.source.id;
  if (!sources.has(id)) sources.set(id, { type: srcName[e.source.type], first: rel(e.time), last: rel(e.time), url: null, host: null, events: [] });
  const s = sources.get(id);
  s.last = rel(e.time);
  const t = typeName[e.type];
  if (e.params?.url && !s.url) s.url = e.params.url;
  if (e.params?.host && !s.host) s.host = e.params.host;
  if (e.params?.dns_query_type !== undefined && e.params?.host) s.host = e.params.host;
  s.events.push(`${rel(e.time).toFixed(0)}:${t}${e.phase === 1 ? '+' : e.phase === 2 ? '-' : ''}`);
}
for (const [id, s] of sources) {
  if (!['URL_REQUEST', 'HOST_RESOLVER_IMPL_JOB', 'HOST_RESOLVER_DNS_TASK', 'SOCKET', 'HTTP_STREAM_JOB', 'CONNECT_JOB', 'TRANSPORT_CONNECT_JOB', 'SSL_CONNECT_JOB', 'HTTP2_SESSION', 'QUIC_SESSION', 'DNS_TRANSACTION'].includes(s.type)) continue;
  console.log(`${String(id).padStart(5)} ${s.type.padEnd(24)} ${s.first.toFixed(0).padStart(7)} .. ${s.last.toFixed(0).padStart(7)} ${s.url ?? s.host ?? ''}`);
}
const ends = log.events.map((e) => rel(e.time));
console.log('last event at', Math.max(...ends).toFixed(0), 'ms relative');
