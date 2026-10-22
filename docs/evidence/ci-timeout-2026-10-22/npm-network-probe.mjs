import assert from 'node:assert/strict';
import { createServer } from 'node:http';
import { mkdir, readFile, writeFile } from 'node:fs/promises';
import { resolve, join } from 'node:path';
import { spawn } from 'node:child_process';

const root = resolve('artifacts/ci-39737327607/npm-probe-' + Date.now());
const cli = process.argv[2];
await mkdir(join(root, 'payload'), { recursive: true });
await writeFile(join(root, 'payload/package.json'), JSON.stringify({ name: 'spar-ci-install-probe', version: '1.0.0' }));
const calls = [];
const sockets = new Set();
const server = createServer((request, response) => {
  calls.push({ method: request.method, url: request.url });
  if (request.url.includes('/-/npm/v1/security/')) {
    request.resume(); // Keep advisory requests unanswered until npm's short fixture timeout.
  } else {
    response.writeHead(404);
    response.end('Dependency unavailable');
  }
});
server.on('connection', socket => { sockets.add(socket); socket.on('close', () => sockets.delete(socket)); });
await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
const registry = `http://127.0.0.1:${server.address().port}`;
async function npm(name, cwd, args, audit) {
  const environment = Object.fromEntries(Object.entries(process.env).filter(([key]) => !/^npm_config_(audit|logs_dir|timing|registry)$/i.test(key)));
  Object.assign(environment, { NPM_CONFIG_AUDIT: String(audit), NPM_CONFIG_TIMING: 'true', NPM_CONFIG_LOGS_DIR: join(root, 'logs'), NPM_CONFIG_REGISTRY: registry });
  const started = performance.now();
  const output = [];
  const child = spawn(process.execPath, [cli, ...args, '--fetch-timeout=1000', '--fetch-retries=0', '--fund=false'], { cwd, env: environment, windowsHide: true });
  child.stdout.on('data', data => output.push(data));
  child.stderr.on('data', data => output.push(data));
  const code = await new Promise((resolve, reject) => { child.once('error', reject); child.once('close', resolve); });
  await writeFile(join(root, name + '.log'), Buffer.concat(output));
  return { name, code, elapsedMs: Math.round(performance.now() - started) };
}
try {
  assert.equal((await npm('pack', join(root, 'payload'), ['pack', '--ignore-scripts'], false)).code, 0);
  const results = [];
  for (const audit of [true, false]) {
    const name = audit ? 'audit-on' : 'audit-off';
    const cwd = join(root, name);
    await mkdir(cwd);
    await writeFile(join(cwd, 'package.json'), JSON.stringify({ private: true, dependencies: { 'spar-ci-install-probe': 'file:../payload/spar-ci-install-probe-1.0.0.tgz' } }));
    assert.equal((await npm(name + '-lock', cwd, ['install', '--package-lock-only', '--ignore-scripts'], false)).code, 0);
    const first = calls.length;
    const result = await npm(name, cwd, ['ci', '--ignore-scripts', '--include=optional'], audit);
    result.requests = calls.slice(first);
    assert.equal(result.code, 0);
    assert.equal(JSON.parse(await readFile(join(cwd, 'node_modules/spar-ci-install-probe/package.json'), 'utf8')).version, '1.0.0');
    if (audit) assert(result.requests.some(call => call.url.includes('/-/npm/v1/security/')));
    else assert.equal(result.requests.length, 0);
    results.push(result);
  }
  const cwd = join(root, 'missing-dependency');
  await mkdir(cwd);
  const manifest = { private: true, dependencies: { 'spar-ci-install-probe': '1.0.0' } };
  await writeFile(join(cwd, 'package.json'), JSON.stringify(manifest));
  await writeFile(join(cwd, 'package-lock.json'), JSON.stringify({ lockfileVersion: 3, packages: { '': manifest, 'node_modules/spar-ci-install-probe': { version: '1.0.0', resolved: registry + '/package.tgz' } } }));
  const first = calls.length;
  const result = await npm('missing-dependency', cwd, ['ci', '--ignore-scripts', '--include=optional'], false);
  result.requests = calls.slice(first);
  assert.notEqual(result.code, 0);
  assert(result.requests.some(call => call.url === '/package.tgz'));
  results.push(result);
  await writeFile(join(root, 'receipt.json'), JSON.stringify({ passed: true, node: process.version, results }, null, 2));
  process.stdout.write(JSON.stringify({ root, results }, null, 2));
} finally {
  for (const socket of sockets) socket.destroy();
  await new Promise(resolve => server.close(resolve));
}
