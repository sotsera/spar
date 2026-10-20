import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import { execFileSync } from 'node:child_process';

// Run after the real frontend build. Compare entire notices with the installed,
// lockfile-selected package licenses, including copyright and warranty text.
const frontend = resolve(process.argv[2] ?? 'samples/Spar.NativeSample/frontend');
const read = (path) => readFile(resolve(frontend, path), 'utf8');
const normalize = (text) => text.replaceAll('\r\n', '\n').trim();
const original = await read('dist/frontend-LICENSES.txt');
const sections = [...original.matchAll(/^## (\S+) - ([^\r\n]+)\r?\n([\s\S]*?)(?=^## |$(?![\s\S]))/gm)];
assert.deepEqual(sections.map((section) => section[1]).sort(), ['react', 'react-dom', 'scheduler', 'vite']);
for (const [, name, heading, text] of sections) {
  const metadata = JSON.parse(await read(`node_modules/${name}/package.json`));
  assert.ok(heading.startsWith(`${metadata.version} (MIT`), `Wrong version/license for ${name}`);
  const installed = await read(`node_modules/${name}/${name === 'vite' ? 'LICENSE.md' : 'LICENSE'}`);
  const expected = name === 'vite' ? installed.split('# Licenses of bundled dependencies')[0] : installed;
  assert.equal(normalize(text), normalize(expected), `Incomplete license for ${name}`);
}

execFileSync(process.execPath, [resolve(frontend, 'write-notices.mjs')]);
assert.equal(await read('dist/frontend-LICENSES.txt'), original, 'Repeated notice generation changed the output');
console.log('Bundled inventory, installed license texts, and repeat generation verified.');
