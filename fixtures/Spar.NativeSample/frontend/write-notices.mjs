import { readFile, writeFile } from 'node:fs/promises';

// Vite inventories bundled npm dependencies, but its virtual modulepreload helper
// is emitted code too. Retain the installed Vite core notice for that helper.
const vite = JSON.parse(await readFile(new URL('./node_modules/vite/package.json', import.meta.url), 'utf8'));
const viteLicense = await readFile(new URL('./node_modules/vite/LICENSE.md', import.meta.url), 'utf8');
const boundary = viteLicense.indexOf('# Licenses of bundled dependencies');
if (boundary < 0) {
  throw new Error('Review the changed Vite license layout before packaging.');
}

const output = new URL('./dist/frontend-LICENSES.txt', import.meta.url);
const generated = await readFile(output, 'utf8');
if (!generated.trim()) {
  throw new Error('The Vite build emitted no dependency notices.');
}

// Re-running this step must not duplicate our additional notice.
const previous = generated.indexOf('\n## vite - ');
const dependencies = previous < 0 ? generated : generated.slice(0, previous);
const helper = `\n## vite - ${vite.version} (MIT, modulepreload polyfill)\n\n${viteLicense.slice(0, boundary)}`;
await writeFile(output, dependencies + helper, 'utf8');
